using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace UltScan;

public static class ScreenTextRecognizer
{
    public static async Task<string> RecognizeTextAsync(Rect rect, Visual visual)
    {
        var width = (int)Math.Round(rect.Width);
        var height = (int)Math.Round(rect.Height);
        if (width <= 0 || height <= 0)
        {
            return string.Empty;
        }

        var dpi = VisualTreeHelper.GetDpi(visual);
        var scaledRect = new Rectangle(
            (int)Math.Round(rect.X * dpi.DpiScaleX),
            (int)Math.Round(rect.Y * dpi.DpiScaleY),
            (int)Math.Round(rect.Width * dpi.DpiScaleX),
            (int)Math.Round(rect.Height * dpi.DpiScaleY));
        if (scaledRect.Width <= 0 || scaledRect.Height <= 0)
        {
            return string.Empty;
        }

        using var bitmap = new Bitmap(scaledRect.Width, scaledRect.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(scaledRect.Left, scaledRect.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        }

        using var stream = new InMemoryRandomAccessStream();
        bitmap.Save(stream.AsStreamForWrite(), ImageFormat.Bmp);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            return string.Empty;
        }

        var result = await engine.RecognizeAsync(softwareBitmap);
        return result?.Text ?? string.Empty;
    }
}
