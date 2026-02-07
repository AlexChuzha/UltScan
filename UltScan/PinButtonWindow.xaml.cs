using System;
using System.Drawing;
using Forms = System.Windows.Forms;
using System.Windows;

namespace UltScan;

public partial class PinButtonWindow : Window
{
    private static readonly System.Windows.Media.Color PanelBaseColor =
        System.Windows.Media.Color.FromArgb(0x99, 0x44, 0x44, 0x44);
    private readonly Action _onClose;
    private readonly Action _onManualTranslate;
    private readonly Action _onCopyTranslation;
    private readonly Action<double, double> _onDragDelta;
    private readonly Action<bool> _onTransparencyChanged;
    private readonly Action<bool> _onHoverChanged;
    private Rect? _avoidRect;
    private Rect _anchorRect;

    public PinButtonWindow(
        Rect anchorRect,
        Action onClose,
        Action onManualTranslate,
        Action onCopyTranslation,
        Action<double, double> onDragDelta,
        Action<bool> onTransparencyChanged,
        Action<bool> onHoverChanged,
        Rect? avoidRect)
    {
        InitializeComponent();
        ShowActivated = false;
        Focusable = false;

        _onClose = onClose;
        _onManualTranslate = onManualTranslate;
        _onCopyTranslation = onCopyTranslation;
        _onDragDelta = onDragDelta;
        _onTransparencyChanged = onTransparencyChanged;
        _onHoverChanged = onHoverChanged;
        _anchorRect = anchorRect;
        _avoidRect = avoidRect;

        var app = (App)System.Windows.Application.Current;
        CloseButton.ToolTip = app.Localization["Overlay.CloseTooltip"];
        ManualTranslateButton.ToolTip = app.Localization["Overlay.ManualTranslateTooltip"];
        CopyTranslationButton.ToolTip = app.Localization["Overlay.CopyTooltip"];
        DragThumb.ToolTip = app.Localization["Overlay.DragTooltip"];
        TransparencyCheckBox.Content = app.Localization["Overlay.TransparencyLabel"];
        TransparencyCheckBox.IsChecked = true;
        UpdatePanelAppearance(app.Settings.Overlay.Opacity, transparencyEnabled: true);

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
        System.Windows.Point? attached = null;
        if (avoidRect.HasValue)
        {
            var output = avoidRect.Value;
            var x = output.Left + 6;
            var y = output.Top - Height;
            if (y < bounds.Top)
            {
                y = output.Bottom;
                if (y + Height > bounds.Bottom)
                {
                    y = Math.Max(bounds.Top, output.Top - Height);
                }
            }

            var clamped = ClampCandidate(new System.Windows.Point(x, y), bounds, Width, Height);
            Left = clamped.X;
            Top = clamped.Y;
            UpdatePanelCorners(output);
            return;
        }

        if (attached.HasValue &&
            attached.Value.X >= bounds.Left &&
            attached.Value.Y >= bounds.Top &&
            attached.Value.X + Width <= bounds.Right &&
            attached.Value.Y + Height <= bounds.Bottom)
        {
            Left = attached.Value.X;
            Top = attached.Value.Y;
            return;
        }

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
        if (avoidRect.HasValue)
        {
            UpdatePanelCorners(avoidRect.Value);
        }
        else
        {
            PanelRoot.CornerRadius = new CornerRadius(8);
        }
    }

    private void UpdatePanelCorners(Rect outputRect)
    {
        if (Top + Height <= outputRect.Top)
        {
            PanelRoot.CornerRadius = new CornerRadius(8, 8, 0, 0);
            return;
        }

        if (Top >= outputRect.Bottom)
        {
            PanelRoot.CornerRadius = new CornerRadius(0, 0, 8, 8);
            return;
        }

        PanelRoot.CornerRadius = new CornerRadius(8);
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

    private void ManualTranslateButton_Click(object sender, RoutedEventArgs e)
    {
        _onManualTranslate();
    }

    private void CopyTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        _onCopyTranslation();
    }

    private void DragThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        _onDragDelta(e.HorizontalChange, e.VerticalChange);
    }

    private void TransparencyCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdatePanelAppearance(GetOverlayOpacity(), transparencyEnabled: true);
        _onTransparencyChanged(true);
    }

    private void TransparencyCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdatePanelAppearance(GetOverlayOpacity(), transparencyEnabled: false);
        _onTransparencyChanged(false);
    }

    private void Panel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _onHoverChanged(true);
    }

    private void Panel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _onHoverChanged(false);
    }

    private static double GetOverlayOpacity()
    {
        if (System.Windows.Application.Current is App app)
        {
            return app.Settings.Overlay.Opacity;
        }

        return 1.0;
    }

    private void UpdatePanelAppearance(double overlayOpacity, bool transparencyEnabled)
    {
        var clamped = Math.Max(0.1, Math.Min(1.0, overlayOpacity));
        var alpha = (byte)Math.Round((transparencyEnabled ? clamped : 1.0) * 255);
        PanelRoot.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(alpha, PanelBaseColor.R, PanelBaseColor.G, PanelBaseColor.B));
    }
}
