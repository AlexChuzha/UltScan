using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UltScan;

public partial class TextOverlayWindow : Window
{
    private readonly Rect _captureRect;
    private System.Windows.Media.Brush _defaultBackground = System.Windows.Media.Brushes.Transparent;
    private System.Windows.Media.Brush _defaultBorderBrush = System.Windows.Media.Brushes.Transparent;
    private Thickness _defaultBorderThickness = new(0);
    private System.Threading.CancellationTokenSource? _translationCts;
    private string _lastCandidate = string.Empty;
    private string _lastStable = string.Empty;
    private DateTime _lastChangeUtc = DateTime.UtcNow;

    public TextOverlayWindow(Rect rect)
    {
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;

        _captureRect = rect;

        Left = _captureRect.Left;
        Top = _captureRect.Top;
        Width = _captureRect.Width;
        Height = _captureRect.Height;

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
        _lastStable = _lastCandidate;
        _lastChangeUtc = DateTime.UtcNow;
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
                await HandleCandidateAsync(app, text);

                await Task.Delay(Math.Max(200, app.Settings.Translation.PollIntervalMs), token);
            }
        }, token);
    }

    private async Task HandleCandidateAsync(App app, string text)
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

        var ratio = GetChangeRatio(normalized, _lastStable);
        if (ratio < app.Settings.Translation.MinChangeRatio)
        {
            return;
        }

        _lastStable = normalized;

        var translated = await TranslationService.TranslateAsync(
            text,
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
            RenderPlainText(translated);
            if (app.Settings.ExperimentalMode)
            {
                Editor.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 76, 217, 100));
            }
        });
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

    private void RenderPlainText(string text)
    {
        LayoutCanvas.Visibility = Visibility.Collapsed;
        LayoutCanvas.Children.Clear();

        Editor.Visibility = Visibility.Visible;
        Editor.Text = text;
        Editor.CaretIndex = Editor.Text.Length;
        Editor.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 242, 242, 242));
    }

    private void RenderLayout(OcrLayoutResult layout)
    {
        Editor.Visibility = Visibility.Collapsed;
        Editor.Text = string.Empty;

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
