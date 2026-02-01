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
            var text = await ScreenTextRecognizer.RecognizeTextAsync(_captureRect, this);
            Editor.Text = text;
            Editor.CaretIndex = Editor.Text.Length;
        }
        finally
        {
            Opacity = 1;
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
