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
using Windows.Globalization;
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
        Bitmap? preBinary = null;
        Bitmap? preSoft = null;

        try
        {
            var engines = GetOcrEngines();
            if (engines.Count == 0)
            {
                return OcrLayoutResult.Empty;
            }

            var raw = await RecognizeBestAsync(bitmap, rect, engines);

            var shouldTryPreprocess = preprocess ||
                                      raw.QualityScore < 80 ||
                                      raw.Text.Length < 20 ||
                                      raw.Lines.Count < 2;
            if (!shouldTryPreprocess)
            {
                return raw;
            }

            preBinary = PreprocessBitmapBinary(bitmap);
            preSoft = PreprocessBitmapSoft(bitmap);

            var bin = await RecognizeBestAsync(preBinary, rect, engines);
            var soft = await RecognizeBestAsync(preSoft, rect, engines);

            var best = PickBest(raw, bin, soft);
            LogOcrCandidates(best, raw, bin, soft);
            return best;
        }
        finally
        {
            preBinary?.Dispose();
            preSoft?.Dispose();
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

    private static Bitmap PreprocessBitmapBinary(Bitmap source)
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

    private static Bitmap PreprocessBitmapSoft(Bitmap source)
    {
        const int scale = 2;
        const double contrast = 1.35;
        const double gamma = 0.9;

        var scaled = new Bitmap(source.Width * scale, source.Height * scale, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
        }

        var rect = new Rectangle(0, 0, scaled.Width, scaled.Height);
        var data = scaled.LockBits(rect, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
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
                var gammaAdjusted = Math.Clamp(Math.Pow(boosted, gamma), 0, 1);
                var level = (byte)Math.Round(gammaAdjusted * 255);

                buffer[idx] = level;
                buffer[idx + 1] = level;
                buffer[idx + 2] = level;
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, byteCount);
        scaled.UnlockBits(data);
        return scaled;
    }

    private static List<OcrEngine> GetOcrEngines()
    {
        var engines = new List<OcrEngine>();
        var preferredSourceLanguage = GetPreferredOcrLanguageFromSettings();

        if (!string.IsNullOrWhiteSpace(preferredSourceLanguage))
        {
            TryAddLanguageEngine(engines, preferredSourceLanguage);
        }

        var userEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (userEngine != null && !ContainsLanguage(engines, userEngine.RecognizerLanguage.LanguageTag))
        {
            engines.Add(userEngine);
        }

        TryAddLanguageEngine(engines, "en");

        return engines;
    }

    private static string? GetPreferredOcrLanguageFromSettings()
    {
        if (System.Windows.Application.Current is not App app)
        {
            return null;
        }

        var sourceLanguage = app.Settings?.Translation?.SourceLanguage;
        if (string.IsNullOrWhiteSpace(sourceLanguage) ||
            string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return sourceLanguage.Trim();
    }

    private static void TryAddLanguageEngine(List<OcrEngine> engines, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        if (TryCreateEngine(code, out var engine) &&
            engine != null &&
            !ContainsLanguage(engines, engine.RecognizerLanguage.LanguageTag))
        {
            engines.Add(engine);
            return;
        }

        var dash = code.IndexOf('-');
        if (dash <= 0)
        {
            return;
        }

        var neutral = code.Substring(0, dash);
        if (TryCreateEngine(neutral, out engine) &&
            engine != null &&
            !ContainsLanguage(engines, engine.RecognizerLanguage.LanguageTag))
        {
            engines.Add(engine);
        }
    }

    private static bool TryCreateEngine(string code, out OcrEngine? engine)
    {
        engine = null;
        try
        {
            var language = new Language(code);
            if (!OcrEngine.IsLanguageSupported(language))
            {
                return false;
            }

            engine = OcrEngine.TryCreateFromLanguage(language);
            return engine != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsLanguage(List<OcrEngine> engines, string tag)
    {
        foreach (var existing in engines)
        {
            if (string.Equals(existing.RecognizerLanguage.LanguageTag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<OcrLayoutResult> RecognizeBestAsync(Bitmap bitmap, Rect rect, List<OcrEngine> engines)
    {
        OcrLayoutResult? best = null;
        foreach (var engine in engines)
        {
            var current = await RecognizeFromBitmapAsync(bitmap, rect, engine);
            best = best == null ? current : PickBest(best, current);
        }

        return best ?? new OcrLayoutResult(string.Empty, Array.Empty<OcrLineLayout>(), 0);
    }

    private static OcrLayoutResult PickBest(OcrLayoutResult a, OcrLayoutResult b)
    {
        if (a.QualityScore != b.QualityScore)
        {
            return a.QualityScore > b.QualityScore ? a : b;
        }

        return a.Text.Length >= b.Text.Length ? a : b;
    }

    private static OcrLayoutResult PickBest(params OcrLayoutResult[] results)
    {
        var best = results.Length > 0 ? results[0] : OcrLayoutResult.Empty;
        for (int i = 1; i < results.Length; i++)
        {
            best = PickBest(best, results[i]);
        }

        return best;
    }

    private static async Task<OcrLayoutResult> RecognizeFromBitmapAsync(Bitmap bitmap, Rect rect, OcrEngine engine)
    {
        using var stream = new InMemoryRandomAccessStream();
        bitmap.Save(stream.AsStreamForWrite(), ImageFormat.Bmp);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);

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

    private static void LogOcrCandidates(OcrLayoutResult chosen, OcrLayoutResult raw, OcrLayoutResult bin, OcrLayoutResult soft)
    {
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Errors");
            Directory.CreateDirectory(root);

            File.WriteAllText(Path.Combine(root, "ocr_raw.txt"),
                $"score={raw.QualityScore}{Environment.NewLine}{raw.Text}");
            File.WriteAllText(Path.Combine(root, "ocr_bin.txt"),
                $"score={bin.QualityScore}{Environment.NewLine}{bin.Text}");
            File.WriteAllText(Path.Combine(root, "ocr_soft.txt"),
                $"score={soft.QualityScore}{Environment.NewLine}{soft.Text}");
            File.WriteAllText(Path.Combine(root, "ocr_chosen.txt"),
                $"chosen_score={chosen.QualityScore}{Environment.NewLine}{chosen.Text}");
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
