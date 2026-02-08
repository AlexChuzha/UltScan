using System;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace UltScan;

public partial class TextOverlayWindow : Window
{
    private const double PlatePadding = 12;
    private const int ShortTextLimit = 10;
    private const double MinCaptureSize = 50;
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
    private string _lastTranslatedText = string.Empty;
    private bool _resizeMode;
    private bool _ctrlResizeEnabled;
    private bool _deferredLayoutPassScheduled;
    private ResizeHandle? _activeResizeHandle;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _ctrlPollTimer;
    private bool _lastCtrlDown;

    public event EventHandler<Rect>? CaptureRectChanged;

    public TextOverlayWindow(Rect rect)
    {
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;

        _captureRect = rect;

        ConfigureLayout();

        SourceInitialized += (_, __) =>
        {
            EnableClickThrough();
            AttachWindowHook();
            SyncCtrlResizeState();
        };

        Loaded += async (_, __) => await StartRecognitionAsync();
        Closed += (_, __) =>
        {
            _translationCts?.Cancel();
            _manualCts?.Cancel();
            DetachWindowHook();
            StopCtrlPolling();
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
        UpdateCtrlResizeFeature();
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
                ShowTranslationStatus(app);
                await ForceTranslateAsyncInternal(app, opId, cts.Token);
            }
            else if (app.Settings.ExperimentalMode)
            {
                var layout = await ScreenTextRecognizer.RecognizeLayoutAsync(_captureRect, this);
                if (IsOpCanceled(opId, cts.Token))
                {
                    return;
                }
                RenderRecognizedLayout(layout);
                TranslationLogger.LogPair(layout.Text, string.Empty);
            }
            else
            {
                var text = await ScreenTextRecognizer.RecognizeTextAsync(_captureRect, this);
                if (IsOpCanceled(opId, cts.Token))
                {
                    return;
                }

                RenderPlainText(text);
                TranslationLogger.LogPair(text, string.Empty);
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
        var size = Math.Max(10, app.Settings.Translation.OverlayFontSize);

        Editor.FontFamily = font;
        OriginalTextBlock.FontFamily = font;
        TranslatedTextBlock.FontFamily = font;
        Editor.FontSize = size;
        OriginalTextBlock.FontSize = size;
        TranslatedTextBlock.FontSize = size;

        if (TranslationPanel.Visibility == Visibility.Visible)
        {
            AdjustHeightToContent(TranslationPanel);
        }
        else if (EditorPanel.Visibility == Visibility.Visible)
        {
            AdjustHeightToContent(EditorPanel);
        }
        else if (LayoutCanvas.Visibility == Visibility.Visible)
        {
            AdjustHeightToContent(LayoutCanvas);
        }
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
            var cappedHeight = Math.Min(translationHeight, GetMaxTranslationHeight(orientation, screen));
            var candidate = ComputeLayout(orientation, translationWidth, cappedHeight);
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
            var cappedHeight = Math.Min(translationHeight, GetMaxTranslationHeight(chosen, screen));
            var fallback = ComputeLayout(chosen, translationWidth, cappedHeight);
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
        if (Editor?.Document != null)
        {
            Editor.Document.PageWidth = GetEditorPageWidth();
        }

        UpdateOverlayGeometry();
        if (System.Windows.Application.Current is App appInstance)
        {
            appInstance.UpdatePinWindowPosition();
        }
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

    private double GetMaxTranslationHeight(OverlayOrientation orientation, Rectangle bounds)
    {
        var pad = PlatePadding;
        switch (orientation)
        {
            case OverlayOrientation.Top:
                return Math.Max(1, _captureRect.Top - bounds.Top - pad);
            case OverlayOrientation.Bottom:
                return Math.Max(1, bounds.Bottom - _captureRect.Bottom - pad);
            case OverlayOrientation.Left:
            case OverlayOrientation.Right:
            default:
                var top = _captureRect.Top - pad;
                var limit = bounds.Bottom - top - pad;
                return Math.Max(1, limit);
        }
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
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await ForceRefreshAsync();
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
                if (IsBetterCandidate(text, qualityScore))
                {
                    _bestCandidateScore = qualityScore;
                    _bestCandidateText = text;
                    _bestCandidateLines = lines.ToList();
                }
                LogDebug($"noise: ratio={ratio:F3} score={qualityScore} best={_bestCandidateScore} len={text.Length}");
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
        if (IsBetterCandidate(text, qualityScore))
        {
            _bestCandidateScore = qualityScore;
            _bestCandidateText = text;
            _bestCandidateLines = lines.ToList();
            LogDebug($"improve best: score={qualityScore} len={text.Length}");
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

        var translatedText = translatedBodies != null && blocks != null
            ? ComposeStructuredText(blocks, translatedBodies)
            : (translated ?? string.Empty);

        TranslationLogger.LogPair(stableText, translatedText);

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

    private bool IsBetterCandidate(string text, int score)
    {
        if (score > _bestCandidateScore)
        {
            return true;
        }

        var lengthGain = text.Length - _bestCandidateText.Length;
        if (lengthGain >= 8 && score >= _bestCandidateScore - 6)
        {
            return true;
        }

        return false;
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
        SetEditorDocumentSingleBlock(
            text,
            isTranslated ? _translatedTextBrush : _defaultTextBrush,
            GetTranslatedFontWeight(isTranslated));
        AdjustHeightToContent(EditorPanel);
        HideTranslationStatus();
        _lastTranslatedText = isTranslated ? text : string.Empty;
    }

    private void RenderRecognizedLayout(OcrLayoutResult layout)
    {
        if (layout.Lines.Count == 0)
        {
            RenderPlainText(layout.Text);
            return;
        }

        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();
        TranslationPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;

        var lines = layout.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .OrderBy(l => l.Bounds.Y)
            .ThenBy(l => l.Bounds.X)
            .ToList();

        if (lines.Count == 0)
        {
            RenderPlainText(layout.Text);
            return;
        }

        if (lines.Count <= 1)
        {
            RenderPlainText(layout.Text);
            return;
        }

        // Some OCR passes return a full layout.Text but a partial Lines collection.
        // In that case, prefer full plain rendering over truncated formatted output.
        var normalizedLayout = NormalizeForCompare(layout.Text);
        var linesText = string.Join(" ", lines.Select(l => l.Text.Trim()));
        var normalizedLines = NormalizeForCompare(linesText);
        if (!string.IsNullOrWhiteSpace(normalizedLayout))
        {
            var coverage = normalizedLines.Length / (double)normalizedLayout.Length;
            if (coverage < 0.75)
            {
                RenderPlainText(layout.Text);
                return;
            }
        }

        var doc = new System.Windows.Documents.FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity,
            PageWidth = GetEditorPageWidth()
        };

        var widthBase = Math.Max(1.0, _captureRect.Width);
        var leftBase = _captureRect.X;
        var cardWidth = Card.ActualWidth > 1 ? Card.ActualWidth : _captureRect.Width;
        var contentWidth = Math.Max(1.0, cardWidth - Card.Padding.Left - Card.Padding.Right);
        var scale = contentWidth / widthBase;

        OcrLineLayout? prev = null;
        foreach (var line in lines)
        {
            var indent = Math.Max(0, (line.Bounds.X - leftBase) * scale);
            var topGap = 0.0;
            if (prev != null)
            {
                var rawGap = line.Bounds.Y - (prev.Bounds.Y + prev.Bounds.Height);
                if (rawGap > 0)
                {
                    topGap = Math.Min(18, rawGap * scale);
                }
            }

            var paragraph = new System.Windows.Documents.Paragraph
            {
                Margin = new Thickness(indent, topGap, 0, 0)
            };
            paragraph.Inlines.Add(new System.Windows.Documents.Run(line.Text)
            {
                Foreground = _defaultTextBrush,
                FontWeight = FontWeights.Normal
            });
            doc.Blocks.Add(paragraph);
            prev = line;
        }

        Editor.Document = doc;
        AdjustHeightToContent(EditorPanel);
        ScheduleDeferredLayoutPass();
        HideTranslationStatus();
        _lastTranslatedText = string.Empty;
    }

    private void SetEditorDocumentSingleBlock(string text, System.Windows.Media.Brush brush, FontWeight fontWeight)
    {
        var doc = new System.Windows.Documents.FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity,
            PageWidth = GetEditorPageWidth()
        };

        var paragraph = new System.Windows.Documents.Paragraph
        {
            Margin = new Thickness(0)
        };
        paragraph.Inlines.Add(new System.Windows.Documents.Run(text ?? string.Empty)
        {
            Foreground = brush,
            FontWeight = fontWeight
        });
        doc.Blocks.Add(paragraph);
        Editor.Document = doc;
        ScheduleDeferredLayoutPass();
    }

    private void ClearEditorDocument()
    {
        Editor.Document = new System.Windows.Documents.FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity,
            PageWidth = GetEditorPageWidth()
        };
    }

    private double GetEditorPageWidth()
    {
        var cardWidth = Card.ActualWidth > 1 ? Card.ActualWidth : _captureRect.Width;
        return Math.Max(1.0, cardWidth - Card.Padding.Left - Card.Padding.Right);
    }

    private void ScheduleDeferredLayoutPass()
    {
        if (_deferredLayoutPassScheduled)
        {
            return;
        }

        _deferredLayoutPassScheduled = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            _deferredLayoutPassScheduled = false;
            UpdateWindowLayout();
        }, DispatcherPriority.Loaded);
    }

    private void RenderTranslatedTextOnly(string translatedText)
    {
        RenderTranslationPanel(
            originalText: string.Empty,
            headerText: string.Empty,
            showOriginal: false,
            showHeader: false,
            translatedText: translatedText);
    }

    private void RenderTranslationPanel(
        string originalText,
        string headerText,
        bool showOriginal,
        bool showHeader,
        string translatedText)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();

        Editor.Visibility = Visibility.Collapsed;
        ClearEditorDocument();
        EditorPanel.Visibility = Visibility.Collapsed;

        TranslationPanel.Visibility = Visibility.Visible;
        OriginalTextBlock.Visibility = showOriginal ? Visibility.Visible : Visibility.Collapsed;
        TranslationHeaderTextBlock.Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;
        OriginalTextBlock.Text = originalText;
        TranslationHeaderTextBlock.Text = headerText;
        TranslatedTextBlock.Text = translatedText;
        TranslatedTextBlock.Foreground = _translatedTextBrush;
        TranslatedTextBlock.FontWeight = GetTranslatedFontWeight(isTranslated: true);
        HideTranslationStatus();
        AdjustHeightToContent(TranslationPanel);
        _lastTranslatedText = translatedText;
    }

    private void RenderExperimentalTranslation(string original, string header, string translated)
    {
        RenderTranslationPanel(
            originalText: original,
            headerText: header,
            showOriginal: true,
            showHeader: true,
            translatedText: translated);
    }

    private void RenderExperimentalTranslationFromLayout(string name, string speech, string header, string translatedText)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();

        Editor.Visibility = Visibility.Collapsed;
        ClearEditorDocument();
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
        TranslationStatusTextBlock.Visibility = Visibility.Hidden;
        AdjustHeightToContent(TranslationPanel);
        _lastTranslatedText = translatedText;
    }

    private void RenderTranslatedWithName(string name, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            RenderTranslatedTextOnly(translatedText);
            return;
        }

        RenderTranslationPanel(
            originalText: name,
            headerText: string.Empty,
            showOriginal: true,
            showHeader: false,
            translatedText: translatedText);
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
        TranslationStatusTextBlock.Visibility = Visibility.Hidden;
        EditorStatusTextBlock.Visibility = Visibility.Hidden;
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
            if (translated != null)
            {
                RenderTranslatedTextOnly(translated);
            }
            else
            {
                RenderPlainText(originalText);
            }
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
        ClearEditorDocument();

        TranslationPanel.Visibility = Visibility.Visible;
        OriginalTextBlock.Visibility = Visibility.Collapsed;
        TranslationHeaderTextBlock.Visibility = Visibility.Collapsed;
        TranslationStatusTextBlock.Visibility = Visibility.Hidden;

        TranslatedTextBlock.Inlines.Clear();
        _lastTranslatedText = ComposeStructuredText(blocks, bodies);

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

    public void CopyTranslatedTextToClipboard()
    {
        if (string.IsNullOrWhiteSpace(_lastTranslatedText))
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(_lastTranslatedText);
        }
        catch
        {
        }
    }

    private void AdjustHeightToContent(FrameworkElement element)
    {
        UpdateWindowLayout();
    }

    private void EnableClickThrough()
    {
        UpdateWindowStyles(clickThrough: true, noActivate: true);
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

    private void UpdateCtrlResizeFeature()
    {
        var app = (App)System.Windows.Application.Current;
        var enabled = app.Settings.Overlay.CtrlResizeEnabled;
        if (_ctrlResizeEnabled == enabled)
        {
            return;
        }

        _ctrlResizeEnabled = enabled;

        if (!enabled)
        {
            SetResizeMode(false);
            StopCtrlPolling();
            return;
        }

        StartCtrlPolling();
    }

    private void StartCtrlPolling()
    {
        if (_ctrlPollTimer != null)
        {
            return;
        }

        _lastCtrlDown = !IsCtrlDown();
        _ctrlPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _ctrlPollTimer.Tick += (_, __) =>
        {
            if (!_ctrlResizeEnabled)
            {
                return;
            }

            var down = IsCtrlDown();
            if (down == _lastCtrlDown)
            {
                return;
            }

            _lastCtrlDown = down;
            SetResizeMode(down);
        };
        _ctrlPollTimer.Start();
    }

    private void SyncCtrlResizeState()
    {
        if (!_ctrlResizeEnabled)
        {
            return;
        }

        var down = IsCtrlDown();
        _lastCtrlDown = down;
        SetResizeMode(down);
    }

    private void StopCtrlPolling()
    {
        if (_ctrlPollTimer == null)
        {
            return;
        }

        _ctrlPollTimer.Stop();
        _ctrlPollTimer = null;
        _lastCtrlDown = false;
    }

    private static bool IsCtrlDown()
    {
        return (GetAsyncKeyState(VkControl) & 0x8000) != 0
            || (GetAsyncKeyState(VkLcontrol) & 0x8000) != 0
            || (GetAsyncKeyState(VkRcontrol) & 0x8000) != 0;
    }

    private void SetResizeMode(bool enabled)
    {
        if (_resizeMode == enabled)
        {
            return;
        }

        _resizeMode = enabled;
        ResizeOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ResizeOverlay.IsHitTestVisible = enabled;
        UpdateWindowStyles(clickThrough: !enabled, noActivate: enabled);
    }

    private void UpdateWindowStyles(bool clickThrough, bool noActivate)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExstyle);
        var style = exStyle.ToInt64();

        if (clickThrough)
        {
            style |= WsExTransparent;
        }
        else
        {
            style &= ~WsExTransparent;
        }

        if (noActivate)
        {
            style |= WsExNoActivate;
        }
        else
        {
            style &= ~WsExNoActivate;
        }

        style |= WsExToolwindow;
        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style));
    }

    private void AttachWindowHook()
    {
        if (_hwndSource != null)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
    }

    private void DetachWindowHook()
    {
        if (_hwndSource == null)
        {
            return;
        }

        _hwndSource.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate && _resizeMode)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (!_resizeMode || sender is not Thumb thumb)
        {
            return;
        }

        _activeResizeHandle = ParseResizeHandle(thumb.Tag);
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizeMode || _activeResizeHandle == null)
        {
            return;
        }

        var rect = _captureRect;
        var left = rect.Left;
        var top = rect.Top;
        var width = rect.Width;
        var height = rect.Height;

        var dx = e.HorizontalChange;
        var dy = e.VerticalChange;

        var handle = _activeResizeHandle.Value;
        if (handle == ResizeHandle.Left || handle == ResizeHandle.TopLeft || handle == ResizeHandle.BottomLeft)
        {
            var newLeft = left + dx;
            var newWidth = width - dx;
            if (newWidth < MinCaptureSize)
            {
                newLeft = left + (width - MinCaptureSize);
                newWidth = MinCaptureSize;
            }

            left = newLeft;
            width = newWidth;
        }
        else if (handle == ResizeHandle.Right || handle == ResizeHandle.TopRight || handle == ResizeHandle.BottomRight)
        {
            var newWidth = width + dx;
            width = Math.Max(MinCaptureSize, newWidth);
        }

        if (handle == ResizeHandle.Top || handle == ResizeHandle.TopLeft || handle == ResizeHandle.TopRight)
        {
            var newTop = top + dy;
            var newHeight = height - dy;
            if (newHeight < MinCaptureSize)
            {
                newTop = top + (height - MinCaptureSize);
                newHeight = MinCaptureSize;
            }

            top = newTop;
            height = newHeight;
        }
        else if (handle == ResizeHandle.Bottom || handle == ResizeHandle.BottomLeft || handle == ResizeHandle.BottomRight)
        {
            var newHeight = height + dy;
            height = Math.Max(MinCaptureSize, newHeight);
        }

        ApplyCaptureRectFromResize(new Rect(left, top, width, height));
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _activeResizeHandle = null;
    }

    private void ApplyCaptureRectFromResize(Rect rect)
    {
        _captureRect = rect;
        CaptureRectChanged?.Invoke(this, rect);
    }

    private static ResizeHandle? ParseResizeHandle(object? tag)
    {
        if (tag is string text && Enum.TryParse(text, out ResizeHandle handle))
        {
            return handle;
        }

        return null;
    }

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const int VkControl = 0x11;
    private const int VkLcontrol = 0xA2;
    private const int VkRcontrol = 0xA3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern short GetAsyncKeyState(int vKey);

    private enum ResizeHandle
    {
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private double ComputeCardHeight(double width)
    {
        var availableWidth = Math.Max(1, width - Card.Padding.Left - Card.Padding.Right);
        Card.Width = width;
        Card.Height = double.NaN;
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


