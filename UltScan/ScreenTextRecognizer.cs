using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        var preprocess = IsPreprocessingEnabled();
        Bitmap? preprocessed = null;

        try
        {
            if (!preprocess)
            {
                return await RecognizeFromBitmapAsync(bitmap, rect);
            }

            preprocessed = PreprocessBitmap(bitmap);
            var rawTask = RecognizeFromBitmapAsync(bitmap, rect);
            var preTask = RecognizeFromBitmapAsync(preprocessed, rect);
            await Task.WhenAll(rawTask, preTask);

            var raw = await rawTask;
            var filtered = await preTask;

            var rawScore = raw.QualityScore;
            var filteredScore = filtered.QualityScore;

            LogOcrComparison(raw.Text, rawScore, filtered.Text, filteredScore, filteredScore > rawScore ? "preprocessed" : "raw");
            return filteredScore > rawScore ? filtered : raw;
        }
        finally
        {
            if (preprocess && preprocessed != null)
            {
                preprocessed.Dispose();
            }
        }
    }

    private static bool IsPreprocessingEnabled()
    {
        if (System.Windows.Application.Current is App app)
        {
            return app.Settings.ExperimentalImagePreprocessing;
        }

        return false;
    }

    private static Bitmap PreprocessBitmap(Bitmap source)
    {
        const int scale = 2;
        const double contrast = 1.6;
        const byte threshold = 170;

        var scaled = new Bitmap(source.Width * scale, source.Height * scale, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
        }

        var rect = new Rectangle(0, 0, scaled.Width, scaled.Height);
        var data = scaled.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var stride = data.Stride;
        var byteCount = Math.Abs(stride) * scaled.Height;
        var buffer = new byte[byteCount];

        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, byteCount);

        for (int y = 0; y < scaled.Height; y++)
        {
            var rowOffset = y * stride;
            for (int x = 0; x < scaled.Width; x++)
            {
                var idx = rowOffset + (x * 3);
                var b = buffer[idx];
                var g = buffer[idx + 1];
                var r = buffer[idx + 2];

                var gray = (0.299 * r) + (0.587 * g) + (0.114 * b);
                var norm = (gray / 255.0) - 0.5;
                var boosted = Math.Clamp((norm * contrast) + 0.5, 0, 1);
                var level = (byte)Math.Round(boosted * 255);

                var bw = level >= threshold ? (byte)255 : (byte)0;
                var inv = (byte)(255 - bw);

                buffer[idx] = inv;
                buffer[idx + 1] = inv;
                buffer[idx + 2] = inv;
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, byteCount);
        scaled.UnlockBits(data);
        return scaled;
    }

    private static async Task<OcrLayoutResult> RecognizeFromBitmapAsync(Bitmap bitmap, Rect rect)
    {
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

            var text = string.Join(" ", parts);
            lines.Add(new OcrLineLayout(text, mappedRect));
        }

        var recognizedText = result.Text ?? string.Empty;
        return new OcrLayoutResult(recognizedText, lines, ScoreText(recognizedText));
    }

    private static int ScoreText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var good = 0;
        var bad = 0;
        var words = 0;
        var inWord = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (inWord)
                {
                    words++;
                    inWord = false;
                }
                continue;
            }

            inWord = true;

            if (char.IsLetterOrDigit(ch))
            {
                good += 2;
            }
            else if (IsBasicPunctuation(ch))
            {
                good += 1;
            }
            else
            {
                bad += 2;
            }
        }

        if (inWord)
        {
            words++;
        }

        var lengthBonus = Math.Min(text.Length, 200);
        return good + (words * 3) + lengthBonus - bad;
    }

    private static bool IsBasicPunctuation(char ch)
    {
        return ch == '.' || ch == ',' || ch == '!' || ch == '?' ||
               ch == '\'' || ch == '"' || ch == ':' || ch == ';' ||
               ch == '-' || ch == '(' || ch == ')';
    }

    private static void LogOcrComparison(string rawText, int rawScore, string preText, int preScore, string chosen)
    {
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Errors");
            Directory.CreateDirectory(root);

            File.WriteAllText(Path.Combine(root, "ocr_raw.txt"),
                $"score={rawScore}{Environment.NewLine}{rawText}");
            File.WriteAllText(Path.Combine(root, "ocr_pre.txt"),
                $"score={preScore}{Environment.NewLine}{preText}");
            File.WriteAllText(Path.Combine(root, "ocr_chosen.txt"),
                $"chosen={chosen}{Environment.NewLine}raw={rawScore} pre={preScore}");
        }
        catch
        {
        }
    }
}

public sealed class OcrLayoutResult
{
    public static readonly OcrLayoutResult Empty = new(string.Empty, Array.Empty<OcrLineLayout>(), 0);

    public OcrLayoutResult(string text, IReadOnlyList<OcrLineLayout> lines, int qualityScore)
    {
        Text = text;
        Lines = lines;
        QualityScore = qualityScore;
    }

    public string Text { get; }
    public IReadOnlyList<OcrLineLayout> Lines { get; }
    public int QualityScore { get; }
}

public sealed record OcrLineLayout(string Text, Rect Bounds);
