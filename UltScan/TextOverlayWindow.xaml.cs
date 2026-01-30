using System.Windows;

namespace UltScan;

public partial class TextOverlayWindow : Window
{
    public TextOverlayWindow(Rect rect)
    {
        InitializeComponent();

        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;

        Loaded += (_, __) =>
        {
            Editor.Focus();
            Editor.CaretIndex = Editor.Text.Length;
        };
    }
}
