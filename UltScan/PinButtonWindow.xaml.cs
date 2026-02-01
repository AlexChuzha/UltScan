using System;
using System.Drawing;
using Forms = System.Windows.Forms;
using System.Windows;

namespace UltScan;

public partial class PinButtonWindow : Window
{
    private readonly Action _onClose;
    private readonly Action<bool> _onHoverChanged;

    public PinButtonWindow(Rect anchorRect, Action onClose, Action<bool> onHoverChanged)
    {
        InitializeComponent();

        _onClose = onClose;
        _onHoverChanged = onHoverChanged;

        var left = anchorRect.Right - Width - 6;
        var top = anchorRect.Top + 6;

        var screen = Forms.Screen.FromRectangle(new Rectangle(
            (int)anchorRect.X,
            (int)anchorRect.Y,
            Math.Max(1, (int)anchorRect.Width),
            Math.Max(1, (int)anchorRect.Height)));

        var bounds = screen.WorkingArea;
        left = Math.Max(bounds.Left, Math.Min(left, bounds.Right - Width));
        top = Math.Max(bounds.Top, Math.Min(top, bounds.Bottom - Height));

        Left = left;
        Top = top;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _onClose();
        Close();
    }

    private void CloseButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _onHoverChanged(true);
    }

    private void CloseButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _onHoverChanged(false);
    }
}
