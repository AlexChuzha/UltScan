using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UltScan;

public partial class TextOverlayWindow : Window
{
    public TextOverlayWindow(Rect rect)
    {
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;

        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;

        SourceInitialized += (_, __) => EnableClickThrough();

        Loaded += (_, __) =>
        {
            Editor.CaretIndex = Editor.Text.Length;
        };
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

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
