using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace UltScan;

public partial class MainWindow : Window
{
    private bool _isDragging;
    private WpfPoint _start;
    private WpfPoint _end;

    public event EventHandler<WpfRect>? SelectionCompleted;

    public MainWindow()
    {
        InitializeComponent();

        // Чтобы окно не появлялось само по себе при создании
        Hide();
    }

    public void StartCaptureMode()
    {
        // Полный экран (по основному монитору)
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        SelectionRect.Visibility = Visibility.Collapsed;
        _isDragging = false;

        Show();
        Activate();
        Focus();
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelCapture();
            e.Handled = true;
        }
    }

    private void CancelCapture()
    {
        _isDragging = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        Hide();
    }

    private void Window_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        _isDragging = true;
        _start = e.GetPosition(RootCanvas);
        _end = _start;

        SelectionRect.Visibility = Visibility.Visible;
        UpdateSelectionVisual(_start, _end);

        CaptureMouse();
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isDragging) return;

        _end = e.GetPosition(RootCanvas);
        UpdateSelectionVisual(_start, _end);
    }

    private void Window_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        ReleaseMouseCapture();

        _end = e.GetPosition(RootCanvas);
        UpdateSelectionVisual(_start, _end);

        var rect = GetNormalizedRect(_start, _end);

        if (rect.Width < 10 || rect.Height < 10)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionCompleted?.Invoke(this, rect);

        SelectionRect.Visibility = Visibility.Collapsed;
        Hide();
    }

    private void UpdateSelectionVisual(WpfPoint a, WpfPoint b)
    {
        var rect = GetNormalizedRect(a, b);

        Canvas.SetLeft(SelectionRect, rect.Left);
        Canvas.SetTop(SelectionRect, rect.Top);
        SelectionRect.Width = rect.Width;
        SelectionRect.Height = rect.Height;
    }

    private static WpfRect GetNormalizedRect(WpfPoint a, WpfPoint b)
    {
        var x1 = Math.Min(a.X, b.X);
        var y1 = Math.Min(a.Y, b.Y);
        var x2 = Math.Max(a.X, b.X);
        var y2 = Math.Max(a.Y, b.Y);
        return new WpfRect(x1, y1, x2 - x1, y2 - y1);
    }
}
