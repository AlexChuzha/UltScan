using System;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace UltScan;

public partial class TextOverlayWindow : Window
{
    private const double PlatePadding = 12;
    private const int ShortTextLimit = 10;
    private Rect _captureRect;
    private System.Windows.Media.Brush _defaultBackground = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Brush _defaultOverlayBackground = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Brush _defaultBorderBrush = System.Windows.Media.Brushes.Transparent;
    private Thickness _defaultBorderThickness = new(0);
    private System.Windows.Media.Brush _defaultOuterBorderBrush = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Brush _defaultInnerBorderBrush = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Brush _defaultTextBrush = System.Windows.Media.Brushes.White;

    private System.Windows.Media.FontFamily _defaultOriginalFontFamily = System.Windows.SystemFonts.MessageFontFamily;
    private System.Windows.Media.Brush _translatedTextBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 76, 217, 100));
    private System.Windows.Media.Brush _captionTextBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 200, 255, 212));
    private System.Threading.CancellationTokenSource? _translationCts;
    private System.Threading.CancellationTokenSource? _manualCts;
    private int _operationId;
    private string _lastCandidate = string.Empty;
    private string _lastStable = string.Empty;
    private DateTime _lastChangeUtc = DateTime.UtcNow;
    private List<OcrLineLayout> _lastLayoutLines = new();
    private string _bestCandidateText = string.Empty;
    private int _bestCandidateScore;
    private List<OcrLineLayout> _bestCandidateLines = new();
    private Rect _holeRect;
    private Rect _translationRect;
    private DateTime _lastDebugLogUtc = DateTime.MinValue;
    private bool _transparencyEnabled = true;

    public TextOverlayWindow(Rect rect)
    {
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;

        _captureRect = rect;

        ConfigureLayout();

        SourceInitialized += (_, __) => EnableClickThrough();

        Loaded += async (_, __) => await StartRecognitionAsync();
        Closed += (_, __) =>
        {
            _translationCts?.Cancel();
            _manualCts?.Cancel();
        };

        CacheDefaults();
        ApplyOverlayAppearance();
    }

    private void CacheDefaults()
    {
        _defaultBackground = Card.Background;
        _defaultOverlayBackground = OverlayPath.Fill;
        _defaultBorderBrush = Card.BorderBrush;
        _defaultBorderThickness = Card.BorderThickness;
        _defaultTextBrush = Editor.Foreground;
        _defaultOriginalFontFamily = Editor.FontFamily;
        _defaultOuterBorderBrush = OuterBorder.BorderBrush;
        _defaultInnerBorderBrush = InnerBorder.BorderBrush;
    }

    private void ApplyOverlayAppearance()
    {
        var app = (App)System.Windows.Application.Current;
        var opacity = _transparencyEnabled ? app.Settings.Overlay.Opacity : 1.0;
        ApplyOverlayOpacity(opacity);
        _translatedTextBrush = new SolidColorBrush(ParseColorOrDefault(
            app.Settings.Translation.TranslatedTextColor,
            System.Windows.Media.Color.FromArgb(255, 76, 217, 100)));
        _captionTextBrush = new SolidColorBrush(ParseColorOrDefault(
            app.Settings.Translation.CaptionTextColor,
            System.Windows.Media.Color.FromArgb(255, 200, 255, 212)));
        ApplyTextFonts();
        UpdateTranslatedTextColors();
    }

    public void ApplyAppearanceFromSettings()
    {
        ApplyOverlayAppearance();
    }

    public void SetTransparencyEnabled(bool enabled)
    {
        _transparencyEnabled = enabled;
        ApplyOverlayAppearance();
    }

    public async Task ForceTranslateAsync()
    {
        await ForceRefreshAsync();
    }

    public async Task ForceRefreshAsync()
    {
        var app = (App)System.Windows.Application.Current;
        _translationCts?.Cancel();
        _manualCts?.Cancel();
        var cts = new CancellationTokenSource();
        _manualCts = cts;
        var opId = Interlocked.Increment(ref _operationId);

        try
        {
            if (app.Settings.Translation.Enabled)
            {
                await ForceTranslateAsyncInternal(app, opId, cts.Token);
            }
            else if (app.Settings.ExperimentalMode)
            {
                var layout = await ScreenTextRecognizer.RecognizeLayoutAsync(_captureRect, this);
                if (IsOpCanceled(opId, cts.Token))
                {
                    return;
                }

                if (layout.Lines.Count > 0)
                {
                    RenderLayout(layout);
                }
                else
                {
                    RenderPlainText(layout.Text);
                }
            }
            else
            {
                var text = await ScreenTextRecognizer.RecognizeTextAsync(_captureRect, this);
                if (IsOpCanceled(opId, cts.Token))
                {
                    return;
                }

                RenderPlainText(text);
            }
        }
        finally
        {
            if (_manualCts == cts)
            {
                _manualCts = null;
            }

            if (app.Settings.Translation.Enabled)
            {
                StartTranslationLoop(app);
            }
        }
    }

    private async Task ForceTranslateAsyncInternal(App app, int opId, CancellationToken token)
    {
        var layout = await ScreenTextRecognizer.RecognizeLayoutAsync(_captureRect, this);
        if (IsOpCanceled(opId, token))
        {
            return;
        }

        var text = layout.Text;
        var normalized = NormalizeForCompare(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        _lastCandidate = normalized;
        _lastStable = string.Empty;
        _lastChangeUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(app.Settings.Translation.StabilizationMs + 50);
        _bestCandidateText = text;
        _bestCandidateScore = layout.QualityScore;
        _bestCandidateLines = layout.Lines.ToList();
        await HandleCandidateAsync(app, text, layout.Lines, layout.QualityScore);
    }

    private bool IsOpCanceled(int opId, CancellationToken token)
    {
        return token.IsCancellationRequested || opId != _operationId;
    }

    private void UpdateTranslatedTextColors()
    {
        if (TranslationPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        TranslatedTextBlock.Foreground = _translatedTextBrush;

        if (TranslatedTextBlock.Inlines.Count == 0)
        {
            return;
        }

        var useCaption = true;
        foreach (var inline in TranslatedTextBlock.Inlines)
        {
            if (inline is System.Windows.Documents.Run run)
            {
                run.Foreground = useCaption ? _captionTextBrush : _translatedTextBrush;
                useCaption = !useCaption;
            }
        }
    }
    private void ApplyTextFonts()
    {
        var app = (App)System.Windows.Application.Current;
        var font = ResolveFontFamily(app.Settings.Translation.OverlayFontFamily, _defaultOriginalFontFamily);

        Editor.FontFamily = font;
        OriginalTextBlock.FontFamily = font;
        TranslatedTextBlock.FontFamily = font;
    }

    private static System.Windows.Media.FontFamily ResolveFontFamily(string value, System.Windows.Media.FontFamily fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return new System.Windows.Media.FontFamily(value);
        }
        catch
        {
            return fallback;
        }
    }

    private void ApplyOverlayOpacity(double opacity)
    {
        if (_defaultBackground is not SolidColorBrush brush)
        {
            return;
        }

        var clamped = Math.Max(0.1, Math.Min(1.0, opacity));
        var cardColor = brush.Color;
        var cardUpdated = System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(clamped * 255),
            cardColor.R,
            cardColor.G,
            cardColor.B);

        Card.Background = new SolidColorBrush(cardUpdated);
        _defaultBackground = Card.Background;

        if (_defaultOverlayBackground is SolidColorBrush overlayBrush)
        {
            var overlayColor = overlayBrush.Color;
            var overlayUpdated = System.Windows.Media.Color.FromArgb(
                (byte)Math.Round(clamped * 255),
                overlayColor.R,
                overlayColor.G,
                overlayColor.B);
            OverlayPath.Fill = new SolidColorBrush(overlayUpdated);
            _defaultOverlayBackground = OverlayPath.Fill;
        }
    }

    private static System.Windows.Media.Color ParseColorOrDefault(string value, System.Windows.Media.Color fallback)
    {
        try
        {
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(value);
            if (converted is System.Windows.Media.Color parsed)
            {
                return parsed;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private void ConfigureLayout()
    {
        UpdateWindowLayout();
    }

    private void UpdateWindowLayout()
    {
        var app = (App)System.Windows.Application.Current;
        var translationWidth = Math.Max(1, _captureRect.Width);
        var translationHeight = Math.Max(1, ComputeCardHeight(translationWidth));

        var screen = Forms.Screen.FromRectangle(new Rectangle(
            (int)_captureRect.X,
            (int)_captureRect.Y,
            Math.Max(1, (int)_captureRect.Width),
            Math.Max(1, (int)_captureRect.Height))).WorkingArea;

        var preferred = app.Settings.Overlay.Orientation;
        var chosen = preferred;
        Rect windowRect = default;
        Rect holeRect = default;
        Rect translationRect = default;

        foreach (var orientation in GetOrientationOrder(preferred))
        {
            var candidate = ComputeLayout(orientation, translationWidth, translationHeight);
            if (Fits(candidate.WindowRect, screen))
            {
                chosen = orientation;
                windowRect = candidate.WindowRect;
                holeRect = candidate.HoleRect;
                translationRect = candidate.TranslationRect;
                break;
            }
        }

        if (windowRect.Width <= 0 || windowRect.Height <= 0)
        {
            var fallback = ComputeLayout(chosen, translationWidth, translationHeight);
            windowRect = fallback.WindowRect;
            holeRect = fallback.HoleRect;
            translationRect = fallback.TranslationRect;
        }

        _holeRect = holeRect;
        _translationRect = translationRect;

        Left = windowRect.Left;
        Top = windowRect.Top;
        Width = windowRect.Width;
        Height = windowRect.Height;

        System.Windows.Controls.Canvas.SetLeft(OuterBorder, _holeRect.X);
        System.Windows.Controls.Canvas.SetTop(OuterBorder, _holeRect.Y);
        OuterBorder.Width = _holeRect.Width;
        OuterBorder.Height = _holeRect.Height;

        System.Windows.Controls.Canvas.SetLeft(Card, _translationRect.X);
        System.Windows.Controls.Canvas.SetTop(Card, _translationRect.Y);
        Card.Width = _translationRect.Width;
        Card.Height = _translationRect.Height;

        UpdateOverlayGeometry();
    }

    private static IEnumerable<OverlayOrientation> GetOrientationOrder(OverlayOrientation preferred)
    {
        var order = new[]
        {
            OverlayOrientation.Right,
            OverlayOrientation.Left,
            OverlayOrientation.Bottom,
            OverlayOrientation.Top
        };

        return order.Where(o => o == preferred).Concat(order.Where(o => o != preferred));
    }

    private (Rect WindowRect, Rect HoleRect, Rect TranslationRect) ComputeLayout(
        OverlayOrientation orientation,
        double translationWidth,
        double translationHeight)
    {
        var captureWidth = _captureRect.Width;
        var captureHeight = _captureRect.Height;
        var pad = PlatePadding;

        var padLeft = 0.0;
        var padTop = 0.0;
        var padRight = 0.0;
        var padBottom = 0.0;

        switch (orientation)
        {
            case OverlayOrientation.Left:
                padTop = pad;
                padRight = pad;
                padBottom = pad;
                break;
            case OverlayOrientation.Bottom:
                padLeft = pad;
                padTop = pad;
                padRight = pad;
                break;
            case OverlayOrientation.Top:
                padLeft = pad;
                padRight = pad;
                padBottom = pad;
                break;
            default:
                padLeft = pad;
                padTop = pad;
                padBottom = pad;
                break;
        }

        var holeX = padLeft + (orientation == OverlayOrientation.Left ? translationWidth : 0);
        var holeY = padTop + (orientation == OverlayOrientation.Top ? translationHeight : 0);

        var windowWidth = holeX + captureWidth + padRight + (orientation == OverlayOrientation.Right ? translationWidth : 0);
        var windowHeight = holeY + captureHeight + padBottom + (orientation == OverlayOrientation.Bottom ? translationHeight : 0);

        if (orientation == OverlayOrientation.Left || orientation == OverlayOrientation.Right)
        {
            windowHeight = Math.Max(windowHeight, padTop + translationHeight + padBottom);
        }
        else
        {
            windowWidth = Math.Max(windowWidth, padLeft + translationWidth + padRight);
        }

        var windowRect = new Rect(_captureRect.Left - holeX, _captureRect.Top - holeY, windowWidth, windowHeight);
        var holeRect = new Rect(holeX, holeY, captureWidth, captureHeight);

        var translationX = orientation == OverlayOrientation.Right
            ? holeX + captureWidth
            : padLeft;

        var translationY = orientation == OverlayOrientation.Bottom
            ? holeY + captureHeight
            : padTop;

        var translationRect = new Rect(translationX, translationY, translationWidth, translationHeight);

        return (windowRect, holeRect, translationRect);
    }

    private static bool Fits(Rect rect, Rectangle bounds)
    {
        return rect.Left >= bounds.Left &&
               rect.Top >= bounds.Top &&
               rect.Right <= bounds.Right &&
               rect.Bottom <= bounds.Bottom;
    }

    private async Task StartRecognitionAsync()
    {
        Opacity = 0;
        try
        {
            var app = (App)System.Windows.Application.Current;
            if (app.Settings.Translation.Enabled)
            {
                await RenderInitialTextAsync(app);
                StartTranslationLoop(app);
            }
            else if (app.Settings.ExperimentalMode)
            {
                var layout = await ScreenTextRecognizer.RecognizeLayoutAsync(_captureRect, this);
                if (layout.Lines.Count > 0)
                {
                    RenderLayout(layout);
                }
                else
                {
                    RenderPlainText(layout.Text);
                }
            }
            else
            {
                var text = await ScreenTextRecognizer.RecognizeTextAsync(_captureRect, this);
                RenderPlainText(text);
            }
        }
        finally
        {
            Opacity = 1;
        }
    }

    private async Task RenderInitialTextAsync(App app)
    {
        var layout = await ScreenTextRecognizer.RecognizeLayoutAsync(_captureRect, this);
        var text = layout.Text;
        RenderPlainText(text);
        _lastCandidate = NormalizeForCompare(text);
        _lastStable = string.Empty;
        _lastChangeUtc = DateTime.UtcNow;
        _lastLayoutLines = layout.Lines.ToList();
        _bestCandidateText = text;
        _bestCandidateScore = layout.QualityScore;
        _bestCandidateLines = layout.Lines.ToList();
    }

    private void StartTranslationLoop(App app)
    {
        _translationCts?.Cancel();
        _translationCts = new System.Threading.CancellationTokenSource();
        var token = _translationCts.Token;
        var opId = _operationId;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var layout = await ScreenTextRecognizer.RecognizeLayoutAsync(_captureRect, this);
                if (IsOpCanceled(opId, token))
                {
                    break;
                }
                var text = layout.Text;
                await HandleCandidateAsync(app, text, layout.Lines, layout.QualityScore);

                if (IsOpCanceled(opId, token))
                {
                    break;
                }
                await Task.Delay(Math.Max(200, app.Settings.Translation.PollIntervalMs), token);
            }
        }, token);
    }

    private async Task HandleCandidateAsync(App app, string text, IReadOnlyList<OcrLineLayout> lines, int qualityScore = 0)
    {
        var normalized = NormalizeForCompare(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            LogDebug("skip: empty normalized");
            return;
        }

        var isSame = string.Equals(normalized, _lastCandidate, StringComparison.Ordinal);
        if (!isSame && IsShortTextTransition(normalized, _lastCandidate))
        {
            _lastCandidate = normalized;
            _lastChangeUtc = DateTime.UtcNow;
            _bestCandidateText = text;
            _bestCandidateScore = qualityScore;
            _bestCandidateLines = lines.ToList();
            ShowTranslationStatus(app);
            LogDebug("accept short change");
            return;
        }
        if (!isSame)
        {
            var ratio = GetChangeRatio(normalized, _lastCandidate);
            if (ratio < app.Settings.Translation.MinChangeRatio)
            {
                if (qualityScore > _bestCandidateScore)
                {
                    _bestCandidateScore = qualityScore;
                    _bestCandidateText = text;
                    _bestCandidateLines = lines.ToList();
                }
                LogDebug($"noise: ratio={ratio:F3} score={qualityScore} best={_bestCandidateScore}");
            }
            else
            {
                if (ratio >= 0.6)
                {
                    _lastCandidate = normalized;
                    _lastChangeUtc = DateTime.UtcNow;
                    _bestCandidateText = text;
                    _bestCandidateScore = qualityScore;
                    _bestCandidateLines = lines.ToList();
                    ShowTranslationStatus(app);
                    LogDebug($"accept big change: ratio={ratio:F3} score={qualityScore}");
                    return;
                }

                if (_bestCandidateScore > 0)
                {
                    var allowDrop = Math.Max(30, (int)(_bestCandidateScore * 0.15));
                    if (qualityScore + allowDrop < _bestCandidateScore)
                    {
                        LogDebug($"reject drop: ratio={ratio:F3} score={qualityScore} best={_bestCandidateScore}");
                        goto ContinueStability;
                    }
                }

                _lastCandidate = normalized;
                _lastChangeUtc = DateTime.UtcNow;
                _bestCandidateText = text;
                _bestCandidateScore = qualityScore;
                _bestCandidateLines = lines.ToList();
                ShowTranslationStatus(app);
                LogDebug($"accept new: ratio={ratio:F3} score={qualityScore}");
                return;
            }
        }

    ContinueStability:
        if (qualityScore > _bestCandidateScore)
        {
            _bestCandidateScore = qualityScore;
            _bestCandidateText = text;
            _bestCandidateLines = lines.ToList();
            LogDebug($"improve best: score={qualityScore}");
        }

        var stableFor = DateTime.UtcNow - _lastChangeUtc;
        if (stableFor.TotalMilliseconds < app.Settings.Translation.StabilizationMs)
        {
            LogDebug($"wait stable: {stableFor.TotalMilliseconds:F0}ms");
            return;
        }

        if (string.Equals(normalized, _lastStable, StringComparison.Ordinal))
        {
            LogDebug("skip: already stable");
            return;
        }

        if (!string.IsNullOrEmpty(_lastStable))
        {
            var ratio = GetChangeRatio(normalized, _lastStable);
            if (ratio < app.Settings.Translation.MinChangeRatio && !IsShortTextTransition(normalized, _lastStable))
            {
                LogDebug($"skip: min change ratio {ratio:F3}");
                return;
            }
        }

        _lastStable = normalized;
        _lastLayoutLines = _bestCandidateLines.ToList();
        var stableText = string.IsNullOrWhiteSpace(_bestCandidateText) ? text : _bestCandidateText;

        var blocks = app.Settings.Translation.Mode == TranslationMode.VisualNovel
            ? TrySplitCaptionsAndBodies(_lastLayoutLines)
            : null;

        string? translated = null;
        IReadOnlyList<string>? translatedBodies = null;

        if (blocks != null)
        {
            translatedBodies = await TranslateBodiesAsync(
                blocks,
                app.Settings.Translation.SourceLanguage,
                app.Settings.Translation.TargetLanguage,
                app.Settings.Translation.ProjectId,
                app.Settings.Translation.ApiKey,
                app.Settings.Translation.Provider);
        }
        else
        {
            translated = await TranslationService.TranslateAsync(
                stableText,
                app.Settings.Translation.SourceLanguage,
                app.Settings.Translation.TargetLanguage,
                app.Settings.Translation.ProjectId,
                app.Settings.Translation.ApiKey,
                app.Settings.Translation.Provider);
        }

        if (translated == null && translatedBodies == null)
        {
            LogDebug("skip: translation null");
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (app.Settings.ExperimentalMode)
            {
                var stamp = DateTime.Now.ToString("HH:mm:ss");
                var header = string.Format(app.Localization["Overlay.TranslatedHeader"], stamp);
                RenderExperimentalTranslationWithBlocks(stableText, header, translated, blocks, translatedBodies);
            }
            else
            {
                RenderTranslationWithBlocks(stableText, translated, blocks, translatedBodies);
            }

            HideTranslationStatus();
        });
        LogDebug("translate: done");
    }

    private static bool IsShortTextTransition(string current, string previous)
    {
        var curLen = current.Length;
        var prevLen = previous.Length;
        return curLen <= ShortTextLimit || prevLen <= ShortTextLimit;
    }

    private void LogDebug(string message)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDebugLogUtc).TotalMilliseconds < 200)
        {
            return;
        }

        _lastDebugLogUtc = now;

        try
        {
            var root = System.IO.Path.Combine(AppContext.BaseDirectory, "Errors");
            System.IO.Directory.CreateDirectory(root);
            var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            System.IO.File.AppendAllText(System.IO.Path.Combine(root, "translation_debug.txt"),
                line + Environment.NewLine);
        }
        catch
        {
        }
    }

    public void UpdateCaptureRect(Rect rect)
    {
        _captureRect = rect;
        ConfigureLayout();
    }

    private static string NormalizeForCompare(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        bool wasSpace = false;
        foreach (var ch in text.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!wasSpace)
                {
                    sb.Append(' ');
                    wasSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                wasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static double GetChangeRatio(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
        {
            return 0;
        }

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 0 : (double)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var d0 = new int[m + 1];
        var d1 = new int[m + 1];

        for (int j = 0; j <= m; j++)
        {
            d0[j] = j;
        }

        for (int i = 1; i <= n; i++)
        {
            d1[0] = i;
            for (int j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d1[j] = Math.Min(
                    Math.Min(d1[j - 1] + 1, d0[j] + 1),
                    d0[j - 1] + cost);
            }

            var temp = d0;
            d0 = d1;
            d1 = temp;
        }

        return d0[m];
    }

    private void RenderPlainText(string text, bool isTranslated = false)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();
        TranslationPanel.Visibility = Visibility.Collapsed;

        EditorPanel.Visibility = Visibility.Visible;
        Editor.Text = text;
        Editor.CaretIndex = Editor.Text.Length;
        Editor.Foreground = isTranslated ? _translatedTextBrush : _defaultTextBrush;
        Editor.FontWeight = GetTranslatedFontWeight(isTranslated);
        AdjustHeightToContent(EditorPanel);
        HideTranslationStatus();
    }

    private void RenderExperimentalTranslation(string original, string header, string translated)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();

        Editor.Visibility = Visibility.Collapsed;
        Editor.Text = string.Empty;
        EditorPanel.Visibility = Visibility.Collapsed;

        TranslationPanel.Visibility = Visibility.Visible;
        OriginalTextBlock.Visibility = Visibility.Visible;
        TranslationHeaderTextBlock.Visibility = Visibility.Visible;
        OriginalTextBlock.Text = original;
        TranslationHeaderTextBlock.Text = header;
        TranslatedTextBlock.Text = translated;
        TranslatedTextBlock.Foreground = _translatedTextBrush;
        TranslatedTextBlock.FontWeight = GetTranslatedFontWeight(isTranslated: true);
        TranslationStatusTextBlock.Visibility = Visibility.Collapsed;
        AdjustHeightToContent(TranslationPanel);
    }

    private void RenderExperimentalTranslationFromLayout(string name, string speech, string header, string translatedText)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();

        Editor.Visibility = Visibility.Collapsed;
        Editor.Text = string.Empty;
        EditorPanel.Visibility = Visibility.Collapsed;

        TranslationPanel.Visibility = Visibility.Visible;
        OriginalTextBlock.Visibility = Visibility.Visible;
        TranslationHeaderTextBlock.Visibility = Visibility.Visible;
        OriginalTextBlock.Text = string.IsNullOrWhiteSpace(name)
            ? speech
            : name + Environment.NewLine + speech;
        TranslationHeaderTextBlock.Text = header;
        TranslatedTextBlock.Text = translatedText;
        TranslatedTextBlock.Foreground = _translatedTextBrush;
        TranslatedTextBlock.FontWeight = GetTranslatedFontWeight(isTranslated: true);
        TranslationStatusTextBlock.Visibility = Visibility.Collapsed;
        AdjustHeightToContent(TranslationPanel);
    }

    private void RenderTranslatedWithName(string name, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            RenderPlainText(translatedText, isTranslated: true);
            return;
        }

        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();

        Editor.Visibility = Visibility.Collapsed;
        Editor.Text = string.Empty;
        EditorPanel.Visibility = Visibility.Collapsed;

        TranslationPanel.Visibility = Visibility.Visible;
        OriginalTextBlock.Visibility = Visibility.Visible;
        TranslationHeaderTextBlock.Visibility = Visibility.Visible;
        OriginalTextBlock.Text = name;
        TranslationHeaderTextBlock.Text = string.Empty;
        TranslatedTextBlock.Text = translatedText;
        TranslatedTextBlock.Foreground = _translatedTextBrush;
        TranslatedTextBlock.FontWeight = GetTranslatedFontWeight(isTranslated: true);
        TranslationStatusTextBlock.Visibility = Visibility.Collapsed;
        AdjustHeightToContent(TranslationPanel);
    }

    private void ShowTranslationStatus(App app)
    {
        Dispatcher.Invoke(() =>
        {
            var label = app.Localization["Overlay.Translating"];
            if (TranslationPanel.Visibility == Visibility.Visible)
            {
                TranslationStatusTextBlock.Text = label;
                TranslationStatusTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                EditorStatusTextBlock.Text = label;
                EditorStatusTextBlock.Visibility = Visibility.Visible;
            }
        });
    }

    private void HideTranslationStatus()
    {
        TranslationStatusTextBlock.Visibility = Visibility.Collapsed;
        EditorStatusTextBlock.Visibility = Visibility.Collapsed;
    }

    private FontWeight GetTranslatedFontWeight(bool isTranslated)
    {
        if (!isTranslated)
        {
            return FontWeights.Normal;
        }

        var app = (App)System.Windows.Application.Current;
        return app.Settings.Translation.TranslatedBold ? FontWeights.Bold : FontWeights.Normal;
    }

    private sealed record VnBlock(string Caption, List<string> BodyLines);

    private static List<VnBlock>? TrySplitCaptionsAndBodies(IReadOnlyList<OcrLineLayout> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return null;
        }

        var ordered = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .OrderBy(l => l.Bounds.Y)
            .ToList();

        if (ordered.Count == 0)
        {
            return null;
        }

        var avgLen = ordered.Average(l => l.Text.Trim().Length);
        var avgWidth = ordered.Average(l => l.Bounds.Width);

        var gaps = new List<double>();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var gap = ordered[i + 1].Bounds.Y - (ordered[i].Bounds.Y + ordered[i].Bounds.Height);
            if (gap >= 0)
            {
                gaps.Add(gap);
            }
        }

        var avgGap = gaps.Count > 0 ? gaps.Average() : 0;

        bool IsCaptionLine(OcrLineLayout line, int index)
        {
            var text = line.Text.Trim();
            var len = text.Length;
            var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var width = line.Bounds.Width;

            var lenScore = len <= Math.Max(6, avgLen * 0.65);
            var widthScore = width <= avgWidth * 0.75;

            var last = text[^1];
            var punctScore = last != '.' && last != '?' && last != '!' && last != ':' && last != ';' && last != ',';

            var gapAbove = index > 0
                ? line.Bounds.Y - (ordered[index - 1].Bounds.Y + ordered[index - 1].Bounds.Height)
                : avgGap;
            var gapBelow = index + 1 < ordered.Count
                ? ordered[index + 1].Bounds.Y - (line.Bounds.Y + line.Bounds.Height)
                : avgGap;
            var gapScore = avgGap > 0 && (gapAbove >= avgGap * 1.4 || gapBelow >= avgGap * 1.4);

            var wordScore = wordCount <= 3;

            var score = 0;
            if (lenScore) score++;
            if (widthScore) score++;
            if (punctScore) score++;
            if (gapScore) score++;
            if (wordScore) score++;

            return score >= 3;
        }

        var blocks = new List<VnBlock>();
        VnBlock? current = null;
        bool hasCaption = false;

        for (int i = 0; i < ordered.Count; i++)
        {
            var line = ordered[i];
            var text = line.Text.Trim();

            if (IsCaptionLine(line, i))
            {
                hasCaption = true;
                if (current != null)
                {
                    blocks.Add(current);
                }

                current = new VnBlock(text, new List<string>());
            }
            else
            {
                if (current == null)
                {
                    current = new VnBlock(string.Empty, new List<string>());
                }

                current.BodyLines.Add(text);
            }
        }

        if (current != null)
        {
            blocks.Add(current);
        }

        return hasCaption ? blocks : null;
    }

    private static async Task<IReadOnlyList<string>?> TranslateBodiesAsync(
        IReadOnlyList<VnBlock> blocks,
        string sourceLanguage,
        string targetLanguage,
        string projectId,
        string? apiKeyOverride,
        string provider)
    {
        var bodies = blocks
            .Select(b => string.Join(Environment.NewLine, b.BodyLines))
            .ToList();

        if (bodies.Count == 0)
        {
            return Array.Empty<string>();
        }

        const string separator = "\n\uE000\n";
        var combined = string.Join(separator, bodies);

        var translated = await TranslationService.TranslateAsync(
            combined,
            sourceLanguage,
            targetLanguage,
            projectId,
            apiKeyOverride,
            provider);

        if (translated == null)
        {
            return null;
        }

        var split = translated.Split(new[] { separator }, StringSplitOptions.None);
        if (split.Length == bodies.Count)
        {
            return split;
        }

        var perBlock = new List<string>(bodies.Count);
        for (int i = 0; i < bodies.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(bodies[i]))
            {
                perBlock.Add(string.Empty);
                continue;
            }

            var blockTranslation = await TranslationService.TranslateAsync(
                bodies[i],
                sourceLanguage,
                targetLanguage,
                projectId,
                apiKeyOverride,
                provider);

            perBlock.Add(blockTranslation ?? string.Empty);
        }

        return perBlock;
    }

    private void RenderTranslationWithBlocks(
        string originalText,
        string? translated,
        IReadOnlyList<VnBlock>? blocks,
        IReadOnlyList<string>? translatedBodies)
    {
        if (blocks == null || translatedBodies == null)
        {
            RenderPlainText(translated ?? originalText, isTranslated: translated != null);
            return;
        }

        RenderStructuredTranslation(blocks, translatedBodies);
    }

    private void RenderExperimentalTranslationWithBlocks(
        string originalText,
        string header,
        string? translated,
        IReadOnlyList<VnBlock>? blocks,
        IReadOnlyList<string>? translatedBodies)
    {
        if (blocks == null || translatedBodies == null)
        {
            RenderExperimentalTranslation(originalText, header, translated ?? string.Empty);
            return;
        }

        var originalStructured = ComposeStructuredText(blocks, blocks.Select(b => string.Join(Environment.NewLine, b.BodyLines)).ToList());
        var translatedStructured = ComposeStructuredText(blocks, translatedBodies);
        RenderExperimentalTranslation(originalStructured, header, translatedStructured);
    }

    private static string ComposeStructuredText(IReadOnlyList<VnBlock> blocks, IReadOnlyList<string> bodies)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.AppendLine(blocks[i].Caption);
            if (bodies.Count > i && !string.IsNullOrWhiteSpace(bodies[i]))
            {
                sb.Append(bodies[i]);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void RenderStructuredTranslation(IReadOnlyList<VnBlock> blocks, IReadOnlyList<string> bodies)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();
        EditorPanel.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Collapsed;
        Editor.Text = string.Empty;

        TranslationPanel.Visibility = Visibility.Visible;
        OriginalTextBlock.Visibility = Visibility.Collapsed;
        TranslationHeaderTextBlock.Visibility = Visibility.Collapsed;
        TranslationStatusTextBlock.Visibility = Visibility.Collapsed;

        TranslatedTextBlock.Inlines.Clear();

        for (int i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                TranslatedTextBlock.Inlines.Add(new System.Windows.Documents.LineBreak());
                TranslatedTextBlock.Inlines.Add(new System.Windows.Documents.LineBreak());
            }

            var captionRun = new System.Windows.Documents.Run(blocks[i].Caption)
            {
                Foreground = _captionTextBrush
            };
            TranslatedTextBlock.Inlines.Add(captionRun);
            TranslatedTextBlock.Inlines.Add(new System.Windows.Documents.LineBreak());

            var bodyText = bodies.Count > i ? bodies[i] : string.Empty;
            var bodyRun = new System.Windows.Documents.Run(string.IsNullOrWhiteSpace(bodyText) ? "\u200B" : bodyText)
            {
                Foreground = _translatedTextBrush,
                FontWeight = GetTranslatedFontWeight(isTranslated: true)
            };
            TranslatedTextBlock.Inlines.Add(bodyRun);
        }

        AdjustHeightToContent(TranslationPanel);
        HideTranslationStatus();
    }

    private void RenderLayout(OcrLayoutResult layout)
    {
        Editor.Visibility = Visibility.Collapsed;
        Editor.Text = string.Empty;
        TranslationPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Collapsed;

        LayoutCanvas.Visibility = Visibility.Visible;
        LayoutCanvas.Children.Clear();

        foreach (var line in layout.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = line.Text,
                FontSize = Editor.FontSize,
                FontWeight = Editor.FontWeight,
                FontFamily = Editor.FontFamily,
                Foreground = Editor.Foreground,
                TextWrapping = TextWrapping.NoWrap
            };

            var localX = (line.Bounds.X - _captureRect.X) + _holeRect.X;
            var localY = (line.Bounds.Y - _captureRect.Y) + _holeRect.Y;

            System.Windows.Controls.Canvas.SetLeft(textBlock, localX);
            System.Windows.Controls.Canvas.SetTop(textBlock, localY);
            LayoutCanvas.Children.Add(textBlock);
        }

        AdjustHeightToContent(Editor);
    }

    private void AdjustHeightToContent(FrameworkElement element)
    {
        UpdateWindowLayout();
    }

    private void EnableClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExstyle);
        var updatedStyle = new IntPtr(exStyle.ToInt64() | WsExTransparent | WsExToolwindow);
        SetWindowLongPtr(hwnd, GwlExstyle, updatedStyle);
    }

    public void SetHighlight(bool isHighlighted)
    {
        if (isHighlighted)
        {
            OuterBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 255, 255));
            InnerBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 0, 0));
        }
        else
        {
            OuterBorder.BorderBrush = _defaultOuterBorderBrush;
            InnerBorder.BorderBrush = _defaultInnerBorderBrush;
            Card.Background = _defaultBackground;
            Card.BorderBrush = _defaultBorderBrush;
            Card.BorderThickness = _defaultBorderThickness;
        }
    }

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private double ComputeCardHeight(double width)
    {
        var availableWidth = Math.Max(1, width - Card.Padding.Left - Card.Padding.Right);
        Card.Width = width;
        Card.Measure(new System.Windows.Size(availableWidth, double.PositiveInfinity));
        return Card.DesiredSize.Height;
    }

    private void UpdateOverlayGeometry()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var full = new RectangleGeometry(new Rect(0, 0, Width, Height));
        var hole = new RectangleGeometry(_holeRect);
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
        OverlayPath.Data = combined;
    }
}








