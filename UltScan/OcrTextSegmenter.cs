using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace UltScan;

public enum OcrSegmentKind
{
    Header,
    Paragraph
}

public sealed record OcrSegment(
    OcrSegmentKind Kind,
    string Text,
    Rect Bounds,
    IReadOnlyList<OcrLineLayout> Lines);

public static class OcrTextSegmenter
{
    public static IReadOnlyList<OcrSegment> BuildSegments(IReadOnlyList<OcrLineLayout> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return Array.Empty<OcrSegment>();
        }

        var ordered = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .OrderBy(l => l.Bounds.Y)
            .ThenBy(l => l.Bounds.X)
            .ToList();
        if (ordered.Count == 0)
        {
            return Array.Empty<OcrSegment>();
        }

        var medianHeight = Median(ordered.Select(l => Math.Max(1, l.Bounds.Height)));
        var medianWidth = Median(ordered.Select(l => Math.Max(1, l.Bounds.Width)));

        var gaps = new List<double>();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var gap = ordered[i + 1].Bounds.Y - (ordered[i].Bounds.Y + ordered[i].Bounds.Height);
            if (gap >= 0)
            {
                gaps.Add(gap);
            }
        }

        var medianGap = gaps.Count > 0 ? Median(gaps) : Math.Max(4, medianHeight * 0.35);
        var sameGroupThreshold = Math.Max(medianHeight * 0.55, medianGap * 1.35);

        var groups = BuildGroups(ordered, sameGroupThreshold);
        var segments = new List<OcrSegment>(groups.Count);

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var text = JoinLines(group.Lines);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var next = i + 1 < groups.Count ? groups[i + 1] : null;
            var isHeader = IsHeaderGroup(group, next, medianWidth);
            segments.Add(new OcrSegment(
                isHeader ? OcrSegmentKind.Header : OcrSegmentKind.Paragraph,
                text,
                group.Bounds,
                group.Lines));
        }

        return segments;
    }

    public static string ComposeText(IReadOnlyList<OcrSegment> segments)
    {
        if (segments == null || segments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            segments
                .Select(s => s.Text?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static List<LineGroup> BuildGroups(List<OcrLineLayout> ordered, double sameGroupThreshold)
    {
        var groups = new List<LineGroup>();
        var current = new List<OcrLineLayout> { ordered[0] };

        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var next = ordered[i];
            var gap = next.Bounds.Y - (prev.Bounds.Y + prev.Bounds.Height);

            if (gap <= sameGroupThreshold)
            {
                current.Add(next);
                continue;
            }

            groups.Add(LineGroup.FromLines(current));
            current = new List<OcrLineLayout> { next };
        }

        groups.Add(LineGroup.FromLines(current));
        return groups;
    }

    private static bool IsHeaderGroup(LineGroup group, LineGroup? nextGroup, double medianWidth)
    {
        if (group.Lines.Count == 0 || group.Lines.Count > 2)
        {
            return false;
        }

        var text = JoinLines(group.Lines);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var len = trimmed.Length;
        var width = group.Bounds.Width;

        if (len > 24 || words > 4)
        {
            return false;
        }

        var last = trimmed[^1];
        if (last == '.' || last == '!' || last == '?' ||
            last == ',' || last == ';' || last == ':')
        {
            return false;
        }

        var isNarrow = width <= medianWidth * 0.72;
        if (!isNarrow)
        {
            return false;
        }

        if (nextGroup == null)
        {
            return false;
        }

        var nextText = JoinLines(nextGroup.Lines);
        return nextText.Length >= 30;
    }

    private static string JoinLines(IReadOnlyList<OcrLineLayout> lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            var current = lines[i].Text.Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (sb.Length == 0)
            {
                sb.Append(current);
                continue;
            }

            if (sb[^1] == '-')
            {
                sb.Length--;
                sb.Append(current);
            }
            else
            {
                sb.Append(' ');
                sb.Append(current);
            }
        }

        return NormalizeSpaces(sb.ToString());
    }

    private static string NormalizeSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var wasSpace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!wasSpace)
                {
                    sb.Append(' ');
                    wasSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                wasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values
            .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
            .OrderBy(v => v)
            .ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return sorted[middle];
        }

        return (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    private sealed class LineGroup
    {
        private LineGroup(IReadOnlyList<OcrLineLayout> lines, Rect bounds)
        {
            Lines = lines;
            Bounds = bounds;
        }

        public IReadOnlyList<OcrLineLayout> Lines { get; }
        public Rect Bounds { get; }

        public static LineGroup FromLines(IReadOnlyList<OcrLineLayout> lines)
        {
            var minX = lines.Min(l => l.Bounds.X);
            var minY = lines.Min(l => l.Bounds.Y);
            var maxX = lines.Max(l => l.Bounds.X + l.Bounds.Width);
            var maxY = lines.Max(l => l.Bounds.Y + l.Bounds.Height);
            return new LineGroup(lines.ToList(), new Rect(minX, minY, maxX - minX, maxY - minY));
        }
    }
}
