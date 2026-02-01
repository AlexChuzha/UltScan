using System;
using System.Drawing;
using Forms = System.Windows.Forms;
using System.Windows;

namespace UltScan;

public partial class PinButtonWindow : Window
{
    private readonly Action _onClose;
    private readonly Action<bool> _onHoverChanged;
    private Rect? _avoidRect;
    private Rect _anchorRect;

    public PinButtonWindow(Rect anchorRect, Action onClose, Action<bool> onHoverChanged, Rect? avoidRect)
    {
        InitializeComponent();

        _onClose = onClose;
        _onHoverChanged = onHoverChanged;
        _anchorRect = anchorRect;
        _avoidRect = avoidRect;

        var app = (App)System.Windows.Application.Current;
        CloseButton.ToolTip = app.Localization["Overlay.CloseTooltip"];

        UpdatePosition(anchorRect, avoidRect);
    }

    public void UpdatePosition(Rect anchorRect, Rect? avoidRect)
    {
        _anchorRect = anchorRect;
        _avoidRect = avoidRect;

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

        var chosen = FindCandidate(candidates, bounds, anchorRect, avoidRect, Width, Height, requireAvoidOutput: true)
                     ?? FindCandidate(candidates, bounds, anchorRect, avoidRect, Width, Height, requireAvoidOutput: false)
                     ?? ClampCandidate(candidates[0], bounds, Width, Height);

        Left = chosen.X;
        Top = chosen.Y;
    }

    private static System.Windows.Point? FindCandidate(
        System.Windows.Point[] candidates,
        Rectangle bounds,
        Rect captureRect,
        Rect? avoidRect,
        double width,
        double height,
        bool requireAvoidOutput)
    {
        foreach (var pt in candidates)
        {
            if (pt.X < bounds.Left || pt.Y < bounds.Top ||
                pt.X + width > bounds.Right || pt.Y + height > bounds.Bottom)
            {
                continue;
            }

            var rect = new System.Windows.Rect(pt.X, pt.Y, width, height);
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

    private static System.Windows.Point ClampCandidate(System.Windows.Point candidate, Rectangle bounds, double width, double height)
    {
        var clampedX = Math.Max(bounds.Left, Math.Min(candidate.X, bounds.Right - width));
        var clampedY = Math.Max(bounds.Top, Math.Min(candidate.Y, bounds.Bottom - height));
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
