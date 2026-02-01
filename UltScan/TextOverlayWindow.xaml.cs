using System;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace UltScan;

public partial class TextOverlayWindow : Window
{
    private Rect _captureRect;
    private System.Windows.Media.Brush _defaultBackground = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Brush _defaultBorderBrush = System.Windows.Media.Brushes.Transparent;
    private Thickness _defaultBorderThickness = new(0);
    private System.Threading.CancellationTokenSource? _translationCts;
    private string _lastCandidate = string.Empty;
    private string _lastStable = string.Empty;
    private DateTime _lastChangeUtc = DateTime.UtcNow;
    private List<OcrLineLayout> _lastLayoutLines = new();

    public TextOverlayWindow(Rect rect)
    {
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;

        _captureRect = rect;

        ConfigureLayout();

        SourceInitialized += (_, __) => EnableClickThrough();

        Loaded += async (_, __) => await StartRecognitionAsync();
        Closed += (_, __) => _translationCts?.Cancel();

        CacheDefaults();
    }

    private void CacheDefaults()
    {
        _defaultBackground = Card.Background;
        _defaultBorderBrush = Card.BorderBrush;
        _defaultBorderThickness = Card.BorderThickness;
    }

    private void ConfigureLayout()
    {
        var app = (App)System.Windows.Application.Current;
        var orientation = app.Settings.Overlay.Orientation;
        Width = _captureRect.Width;
        Height = _captureRect.Height;

        var screen = Forms.Screen.FromRectangle(new Rectangle(
            (int)_captureRect.X,
            (int)_captureRect.Y,
            Math.Max(1, (int)_captureRect.Width),
            Math.Max(1, (int)_captureRect.Height))).WorkingArea;

        var desired = GetPositionForOrientation(orientation);
        if (!Fits(desired, screen))
        {
            var fallback = GetFallbackOrientation(orientation);
            desired = GetPositionForOrientation(fallback);
        }

        Left = desired.Left;
        Top = desired.Top;
        ClampToScreen();
    }

    private Rect GetPositionForOrientation(OverlayOrientation orientation)
    {
        return orientation switch
        {
            OverlayOrientation.Bottom => new Rect(_captureRect.Left, _captureRect.Bottom, Width, Height),
            OverlayOrientation.Top => new Rect(_captureRect.Left, _captureRect.Top - Height, Width, Height),
            OverlayOrientation.Left => new Rect(_captureRect.Left - Width, _captureRect.Top, Width, Height),
            _ => new Rect(_captureRect.Right, _captureRect.Top, Width, Height)
        };
    }

    private static OverlayOrientation GetFallbackOrientation(OverlayOrientation orientation)
    {
        return orientation switch
        {
            OverlayOrientation.Bottom => OverlayOrientation.Top,
            OverlayOrientation.Top => OverlayOrientation.Bottom,
            OverlayOrientation.Left => OverlayOrientation.Right,
            _ => OverlayOrientation.Left
        };
    }

    private static bool Fits(Rect rect, Rectangle bounds)
    {
        return rect.Left >= bounds.Left &&
               rect.Top >= bounds.Top &&
               rect.Right <= bounds.Right &&
               rect.Bottom <= bounds.Bottom;
    }

    private void ClampToScreen()
    {
        var rect = new Rectangle((int)Left, (int)Top, (int)Math.Max(1, Width), (int)Math.Max(1, Height));
        var screen = Forms.Screen.FromRectangle(rect).WorkingArea;

        if (Left + Width > screen.Right)
        {
            Left = screen.Right - Width;
        }

        if (Top + Height > screen.Bottom)
        {
            Top = screen.Bottom - Height;
        }

        if (Left < screen.Left) Left = screen.Left;
        if (Top < screen.Top) Top = screen.Top;
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
    }

    private void StartTranslationLoop(App app)
    {
        _translationCts?.Cancel();
        _translationCts = new System.Threading.CancellationTokenSource();
        var token = _translationCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var layout = await ScreenTextRecognizer.RecognizeLayoutAsync(_captureRect, this);
                var text = layout.Text;
                await HandleCandidateAsync(app, text, layout.Lines);

                await Task.Delay(Math.Max(200, app.Settings.Translation.PollIntervalMs), token);
            }
        }, token);
    }

    private async Task HandleCandidateAsync(App app, string text, IReadOnlyList<OcrLineLayout> lines)
    {
        var normalized = NormalizeForCompare(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!string.Equals(normalized, _lastCandidate, StringComparison.Ordinal))
        {
            _lastCandidate = normalized;
            _lastChangeUtc = DateTime.UtcNow;
            ShowTranslationStatus(app);
            return;
        }

        var stableFor = DateTime.UtcNow - _lastChangeUtc;
        if (stableFor.TotalMilliseconds < app.Settings.Translation.StabilizationMs)
        {
            return;
        }

        if (string.Equals(normalized, _lastStable, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_lastStable))
        {
            var ratio = GetChangeRatio(normalized, _lastStable);
            if (ratio < app.Settings.Translation.MinChangeRatio)
            {
                return;
            }
        }

        _lastStable = normalized;
        _lastLayoutLines = lines.ToList();

        var split = app.Settings.ExperimentalMode ? TrySplitNameAndSpeech(_lastLayoutLines) : null;
        var textToTranslate = text;

        var translated = await TranslationService.TranslateAsync(
            textToTranslate,
            app.Settings.Translation.SourceLanguage,
            app.Settings.Translation.TargetLanguage,
            app.Settings.Translation.ProjectId,
            app.Settings.Translation.ApiKey,
            app.Settings.Translation.Provider);

        if (translated == null)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (app.Settings.ExperimentalMode)
            {
                var stamp = DateTime.Now.ToString("HH:mm:ss");
                var header = string.Format(app.Localization["Overlay.TranslatedHeader"], stamp);
                if (split != null)
                {
                    RenderExperimentalTranslationFromLayout(split.Value.name, split.Value.speech, header, translated);
                }
                else
                {
                    RenderExperimentalTranslation(text, header, translated);
                }
            }
            else
            {
                RenderPlainText(translated, isTranslated: true);
            }

            HideTranslationStatus();
        });
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
        Editor.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 242, 242, 242));
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
        OriginalTextBlock.Text = original;
        TranslationHeaderTextBlock.Text = header;
        TranslatedTextBlock.Text = translated;
        TranslatedTextBlock.FontWeight = GetTranslatedFontWeight(isTranslated: true);
        AdjustHeightToContent(TranslationPanel);
    }

    private void RenderExperimentalTranslationFromLayout(string name, string speech, string header, string translatedText)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();

        Editor.Visibility = Visibility.Collapsed;
        Editor.Text = string.Empty;

        TranslationPanel.Visibility = Visibility.Visible;
        OriginalTextBlock.Text = string.IsNullOrWhiteSpace(name)
            ? speech
            : name + Environment.NewLine + speech;
        TranslationHeaderTextBlock.Text = header;
        TranslatedTextBlock.Text = translatedText;
        TranslatedTextBlock.FontWeight = GetTranslatedFontWeight(isTranslated: true);
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

    private (string name, string speech)? TrySplitNameAndSpeech(IReadOnlyList<OcrLineLayout> lines)
    {
        if (lines == null || lines.Count < 2)
        {
            return null;
        }

        var ordered = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .OrderBy(l => l.Bounds.Y)
            .ToList();

        if (ordered.Count < 2)
        {
            return null;
        }

        var first = ordered[0].Text.Trim();
        var rest = ordered.Skip(1).Select(l => l.Text.Trim()).ToList();
        var avgRestLen = rest.Count > 0 ? rest.Average(t => t.Length) : 0;
        if (avgRestLen <= 0)
        {
            return null;
        }

        var looksLikeName = first.Length <= 30 && first.Length <= avgRestLen * 0.9;
        if (!looksLikeName)
        {
            return null;
        }

        var speech = string.Join(Environment.NewLine, rest);
        return (first, speech);
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
                Foreground = Editor.Foreground,
                TextWrapping = TextWrapping.NoWrap
            };

            var localX = line.Bounds.X - _captureRect.X;
            var localY = line.Bounds.Y - _captureRect.Y;

            System.Windows.Controls.Canvas.SetLeft(textBlock, localX);
            System.Windows.Controls.Canvas.SetTop(textBlock, localY);
            LayoutCanvas.Children.Add(textBlock);
        }

        AdjustHeightToContent(Editor);
    }

    private void AdjustHeightToContent(FrameworkElement element)
    {
        var minHeight = _captureRect.Height;
        var availableWidth = Math.Max(1, Width - Card.Padding.Left - Card.Padding.Right);

        element.Measure(new System.Windows.Size(availableWidth, double.PositiveInfinity));
        var desired = element.DesiredSize.Height + Card.Padding.Top + Card.Padding.Bottom;

        var rect = new Rectangle((int)Left, (int)Top, (int)Math.Max(1, Width), 1);
        var screen = Forms.Screen.FromRectangle(rect).WorkingArea;
        var maxHeight = screen.Height;

        var newHeight = Math.Max(minHeight, desired);
        if (newHeight > maxHeight)
        {
            newHeight = maxHeight;
        }

        Height = newHeight;
        ClampToScreen();
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
            Card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 68, 68, 68));
            Card.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 255, 255));
            Card.BorderThickness = new Thickness(2);
        }
        else
        {
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
}
