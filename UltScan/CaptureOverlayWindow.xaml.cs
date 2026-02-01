using System;
using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Interop;

namespace UltScan;

public partial class CaptureOverlayWindow : Window
{
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private bool _resizeMode;

    public event EventHandler<Rect>? RectChanged;

    public CaptureOverlayWindow(Rect rect)
    {
        InitializeComponent();

        ShowActivated = false;
        Focusable = false;

        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;

        SourceInitialized += (_, __) => EnableClickThrough();
        Loaded += (_, __) => InstallKeyboardHook();
        Closed += (_, __) => UninstallKeyboardHook();
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

    private void SetResizeMode(bool enabled)
    {
        if (_resizeMode == enabled)
        {
            return;
        }

        _resizeMode = enabled;
        ResizeThumb.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        OuterBorder.BorderBrush = enabled
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 255, 255))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 0, 0, 0));

        InnerBorder.BorderBrush = enabled
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0, 0, 0))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(204, 255, 255, 255));

        SetClickThrough(!enabled);
    }

    private void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExstyle);
        var style = exStyle.ToInt64();
        if (enabled)
        {
            style |= WsExTransparent | WsExToolwindow;
        }
        else
        {
            style &= ~WsExTransparent;
            style |= WsExToolwindow;
        }

        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style));
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizeMode)
        {
            return;
        }

        var newWidth = Math.Max(50, Width + e.HorizontalChange);
        var newHeight = Math.Max(50, Height + e.VerticalChange);
        Width = newWidth;
        Height = newHeight;
        RectChanged?.Invoke(this, new Rect(Left, Top, Width, Height));
    }

    private void InstallKeyboardHook()
    {
        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);
    }

    private void UninstallKeyboardHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = GetModuleHandle(curModule?.ModuleName);
        return SetWindowsHookEx(WhKeyboardLl, proc, moduleHandle, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg == WmKeyDown || msg == WmSysKeyDown)
            {
                var vkCode = Marshal.ReadInt32(lParam);
                if (IsCtrlKey(vkCode))
                {
                    Dispatcher.Invoke(() => SetResizeMode(true));
                }
            }
            else if (msg == WmKeyUp || msg == WmSysKeyUp)
            {
                var vkCode = Marshal.ReadInt32(lParam);
                if (IsCtrlKey(vkCode))
                {
                    Dispatcher.Invoke(() => SetResizeMode(false));
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsCtrlKey(int vkCode)
    {
        return vkCode == VkControl || vkCode == VkLcontrol || vkCode == VkRcontrol;
    }

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkLcontrol = 0xA2;
    private const int VkRcontrol = 0xA3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
}
