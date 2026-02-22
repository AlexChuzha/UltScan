using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace UltScan;

public static class HoverWordRecognizer
{
    public static async Task<HoverWordHitResult?> TryRecognizeWordAtCursorAsync(
        Point cursorPoint,
        Visual visual,
        HoverOcrOptions? options = null)
    {
        options ??= new HoverOcrOptions();
        var attempts = BuildCaptureRects(cursorPoint, options);

        foreach (var rect in attempts)
        {
            var words = await ScreenTextRecognizer.RecognizeWordsAsync(rect, visual).ConfigureAwait(false);
            if (words.Count == 0)
            {
                continue;
            }

            var word = PickWordUnderPoint(words, cursorPoint);
            if (word == null)
            {
                continue;
            }

            var padded = Inflate(word.Bounds, options.WordPaddingFactor);
            return new HoverWordHitResult(word.Text, word.Bounds, padded, rect);
        }

        return null;
    }

    private static IReadOnlyList<Rect> BuildCaptureRects(Point center, HoverOcrOptions options)
    {
        var results = new List<Rect>();
        var width = Math.Max(1, options.InitialCaptureWidth);
        var height = Math.Max(1, options.InitialCaptureHeight);
        var maxWidth = Math.Max(width, options.MaxCaptureWidth);
        var maxHeight = Math.Max(height, options.MaxCaptureHeight);
        var growth = options.GrowthFactor <= 1.0 ? 1.3 : options.GrowthFactor;

        while (true)
        {
            results.Add(RectFromCenter(center, width, height));

            if (width >= maxWidth && height >= maxHeight)
            {
                break;
            }

            var nextWidth = Math.Min(maxWidth, (int)Math.Ceiling(width * growth));
            var nextHeight = Math.Min(maxHeight, (int)Math.Ceiling(height * growth));
            if (nextWidth == width && nextHeight == height)
            {
                break;
            }

            width = nextWidth;
            height = nextHeight;
        }

        return results;
    }

    private static Rect RectFromCenter(Point center, double width, double height)
    {
        var left = center.X - (width / 2.0);
        var top = center.Y - (height / 2.0);
        return new Rect(left, top, width, height);
    }

    private static OcrWordLayout? PickWordUnderPoint(IReadOnlyList<OcrWordLayout> words, Point point)
    {
        return words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .Where(w => w.Bounds.Contains(point))
            .OrderBy(w => w.Bounds.Width * w.Bounds.Height)
            .FirstOrDefault();
    }

    private static Rect Inflate(Rect source, double factor)
    {
        var safeFactor = Math.Clamp(factor, 0, 1);
        var dx = source.Width * safeFactor;
        var dy = source.Height * safeFactor;
        var expanded = source;
        expanded.Inflate(dx, dy);
        return expanded;
    }
}

public sealed record HoverWordHitResult(
    string WordText,
    Rect WordBounds,
    Rect PaddedWordBounds,
    Rect CaptureRect);
