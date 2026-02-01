using System;
using System.Drawing;
using Forms = System.Windows.Forms;
using System.Windows;

namespace UltScan;

public partial class PinButtonWindow : Window
{
    private readonly Action _onClose;
    private readonly Action<bool> _onHoverChanged;

    public PinButtonWindow(Rect anchorRect, Action onClose, Action<bool> onHoverChanged, Rect? avoidRect)
    {
        InitializeComponent();

        _onClose = onClose;
        _onHoverChanged = onHoverChanged;

        var screen = Forms.Screen.FromRectangle(new Rectangle(
            (int)anchorRect.X,
            (int)anchorRect.Y,
            Math.Max(1, (int)anchorRect.Width),
            Math.Max(1, (int)anchorRect.Height)));

        var bounds = screen.WorkingArea;
        var candidates = new[]
        {
            new System.Windows.Point(anchorRect.Left + 6, anchorRect.Top - Height - 6),          // above-left
            new System.Windows.Point(anchorRect.Right - Width - 6, anchorRect.Top - Height - 6), // above-right
            new System.Windows.Point(anchorRect.Left - Width - 6, anchorRect.Top + 6),           // left-outside
            new System.Windows.Point(anchorRect.Right + 6, anchorRect.Top + 6),                  // right-outside
            new System.Windows.Point(anchorRect.Left + 6, anchorRect.Bottom + 6),                // below-left (outside)
            new System.Windows.Point(anchorRect.Right - Width - 6, anchorRect.Bottom + 6)        // below-right (outside)
        };

        var chosen = FindCandidate(candidates, bounds, anchorRect, avoidRect, requireAvoidOutput: true)
                     ?? FindCandidate(candidates, bounds, anchorRect, avoidRect, requireAvoidOutput: false)
                     ?? ClampCandidate(candidates[0], bounds);

        var left = chosen.X;
        var top = chosen.Y;

        Left = left;
        Top = top;
    }

    private static System.Windows.Point? FindCandidate(
        System.Windows.Point[] candidates,
        Rectangle bounds,
        Rect captureRect,
        Rect? avoidRect,
        bool requireAvoidOutput)
    {
        foreach (var pt in candidates)
        {
            if (pt.X < bounds.Left || pt.Y < bounds.Top ||
                pt.X + 28 > bounds.Right || pt.Y + 28 > bounds.Bottom)
            {
                continue;
            }

            var rect = new System.Windows.Rect(pt.X, pt.Y, 28, 28);
            if (rect.IntersectsWith(captureRect))
            {
                continue;
            }

            if (requireAvoidOutput && avoidRect.HasValue && rect.IntersectsWith(avoidRect.Value))
            {
                continue;
            }

            return pt;
        }

        return null;
    }

    private static System.Windows.Point ClampCandidate(System.Windows.Point candidate, Rectangle bounds)
    {
        var clampedX = Math.Max(bounds.Left, Math.Min(candidate.X, bounds.Right - 28));
        var clampedY = Math.Max(bounds.Top, Math.Min(candidate.Y, bounds.Bottom - 28));
        return new System.Windows.Point(clampedX, clampedY);
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
