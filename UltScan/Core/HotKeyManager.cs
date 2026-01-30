using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UltScan;

internal sealed class HotKeyManager : IDisposable
{
    private readonly int _id;
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;

    public event EventHandler? HotKeyPressed;

    public HotKeyManager(Window messageWindow, ModifierKeys modifiers, uint vk)
    {
        _id = GetHashCode(); // достаточно уникально в рамках процесса

        var helper = new WindowInteropHelper(messageWindow);
        _hwnd = helper.EnsureHandle();

        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("HwndSource is null.");
        _source.AddHook(WndProc);

        if (!RegisterHotKey(_hwnd, _id, (uint)modifiers, vk))
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"UltScan: не удалось зарегистрировать хоткей. Возможно, он занят другим приложением. Win32Error={err}");
        }
    }

    public void Dispose()
    {
        UnregisterHotKey(_hwnd, _id);
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            HotKeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

[Flags]
internal enum ModifierKeys : uint
{
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}
