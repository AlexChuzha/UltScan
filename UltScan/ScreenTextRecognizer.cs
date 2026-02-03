using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using System.Collections.Generic;

namespace UltScan;

public static class ScreenTextRecognizer
{
    public static async Task<string> RecognizeTextAsync(Rect rect, Visual visual)
    {
        var layout = await RecognizeLayoutAsync(rect, visual);
        return layout.Text;
    }

    public static async Task<OcrLayoutResult> RecognizeLayoutAsync(Rect rect, Visual visual)
    {
        var width = (int)Math.Round(rect.Width);
        var height = (int)Math.Round(rect.Height);
        if (width <= 0 || height <= 0)
        {
            return OcrLayoutResult.Empty;
        }

        var dpi = VisualTreeHelper.GetDpi(visual);
        var scaledRect = new Rectangle(
            (int)Math.Round(rect.X * dpi.DpiScaleX),
            (int)Math.Round(rect.Y * dpi.DpiScaleY),
            (int)Math.Round(rect.Width * dpi.DpiScaleX),
            (int)Math.Round(rect.Height * dpi.DpiScaleY));
        if (scaledRect.Width <= 0 || scaledRect.Height <= 0)
        {
            return OcrLayoutResult.Empty;
        }

        using var bitmap = new Bitmap(scaledRect.Width,
                                      scaledRect.Height,
                                      System.Drawing.Imaging.PixelFormat.Format32bppArgb);

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
            return OcrLayoutResult.Empty;
        }

        var result = await engine.RecognizeAsync(softwareBitmap);
        if (result == null)
        {
            return OcrLayoutResult.Empty;
        }

        var scaleX = rect.Width / softwareBitmap.PixelWidth;
        var scaleY = rect.Height / softwareBitmap.PixelHeight;

        var lines = new List<OcrLineLayout>();
        foreach (Windows.Media.Ocr.OcrLine line in result.Lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            var parts = new List<string>(line.Words.Count);
            foreach (Windows.Media.Ocr.OcrWord word in line.Words)
            {
                parts.Add(word.Text);
                Windows.Foundation.Rect wordRect = word.BoundingRect;
                minX = Math.Min(minX, wordRect.X);
                minY = Math.Min(minY, wordRect.Y);
                maxX = Math.Max(maxX, wordRect.X + wordRect.Width);
                maxY = Math.Max(maxY, wordRect.Y + wordRect.Height);
            }

            var mappedRect = new System.Windows.Rect(
                rect.X + (minX * scaleX),
                rect.Y + (minY * scaleY),
                (maxX - minX) * scaleX,
                (maxY - minY) * scaleY);

            var textColor = EstimateLineColor(bitmap, (int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
            var text = string.Join(" ", parts);
            lines.Add(new OcrLineLayout(text, mappedRect, textColor));
        }

        return new OcrLayoutResult(result.Text ?? string.Empty, lines);
    }

    private static System.Windows.Media.Color? EstimateLineColor(Bitmap bitmap, int x, int y, int width, int height)
    {
        if (width <= 2 || height <= 2)
        {
            return null;
        }

        var rect = new Rectangle(
            Math.Max(0, x),
            Math.Max(0, y),
            Math.Min(width, bitmap.Width - x),
            Math.Min(height, bitmap.Height - y));

        if (rect.Width <= 2 || rect.Height <= 2)
        {
            return null;
        }

        var step = Math.Max(1, Math.Min(rect.Width, rect.Height) / 12);
        var borderLums = new List<double>();

        for (int ix = rect.Left; ix < rect.Right; ix += step)
        {
            borderLums.Add(GetLuminance(bitmap.GetPixel(ix, rect.Top)));
            borderLums.Add(GetLuminance(bitmap.GetPixel(ix, rect.Bottom - 1)));
        }

        for (int iy = rect.Top; iy < rect.Bottom; iy += step)
        {
            borderLums.Add(GetLuminance(bitmap.GetPixel(rect.Left, iy)));
            borderLums.Add(GetLuminance(bitmap.GetPixel(rect.Right - 1, iy)));
        }

        borderLums.Sort();
        var bgLum = borderLums.Count > 0 ? borderLums[borderLums.Count / 2] : 0;

        double r = 0;
        double g = 0;
        double b = 0;
        int count = 0;

        for (int iy = rect.Top; iy < rect.Bottom; iy += step)
        {
            for (int ix = rect.Left; ix < rect.Right; ix += step)
            {
                var pixel = bitmap.GetPixel(ix, iy);
                var lum = GetLuminance(pixel);
                var contrast = Math.Abs(lum - bgLum);
                if (contrast < 35)
                {
                    continue;
                }

                var sat = GetSaturation(pixel);
                if (sat < 0.08 && contrast < 70)
                {
                    continue;
                }

                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
                count++;
            }
        }

        if (count < 6)
        {
            return null;
        }

        var avg = System.Windows.Media.Color.FromArgb(
            255,
            (byte)Math.Clamp(Math.Round(r / count), 0, 255),
            (byte)Math.Clamp(Math.Round(g / count), 0, 255),
            (byte)Math.Clamp(Math.Round(b / count), 0, 255));

        return avg;
    }

    private static double GetLuminance(System.Drawing.Color color)
    {
        return (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
    }

    private static double GetSaturation(System.Drawing.Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max <= 0)
        {
            return 0;
        }

        return (max - min) / max;
    }
}

public sealed class OcrLayoutResult
{
    public static readonly OcrLayoutResult Empty = new(string.Empty, Array.Empty<OcrLineLayout>());

    public OcrLayoutResult(string text, IReadOnlyList<OcrLineLayout> lines)
    {
        Text = text;
        Lines = lines;
    }

    public string Text { get; }
    public IReadOnlyList<OcrLineLayout> Lines { get; }
}

public sealed record OcrLineLayout(string Text, Rect Bounds, System.Windows.Media.Color? TextColor);
