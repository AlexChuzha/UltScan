using System.Windows;
using Xunit;

namespace UltScan.Tests;

public sealed class OcrTextSegmenterTests
{
    [Fact]
    public void BuildSegments_EmptyInput_ReturnsEmpty()
    {
        var segments = OcrTextSegmenter.BuildSegments(Array.Empty<OcrLineLayout>());
        Assert.Empty(segments);
    }

    [Fact]
    public void BuildSegments_ParagraphLines_AreGroupedAndComposed()
    {
        var lines = new[]
        {
            new OcrLineLayout("Hello", new Rect(0, 0, 100, 20)),
            new OcrLineLayout("world", new Rect(0, 22, 120, 20)),
            new OcrLineLayout("Next paragraph line", new Rect(0, 90, 240, 20))
        };

        var segments = OcrTextSegmenter.BuildSegments(lines);
        var text = OcrTextSegmenter.ComposeText(segments);

        Assert.Equal(2, segments.Count);
        Assert.Contains("Hello world", text);
        Assert.Contains("Next paragraph line", text);
    }

    [Fact]
    public void ComposeText_MergesHyphenatedWords()
    {
        var lines = new[]
        {
            new OcrLineLayout("inter-", new Rect(0, 0, 90, 20)),
            new OcrLineLayout("national", new Rect(0, 22, 110, 20))
        };

        var segments = OcrTextSegmenter.BuildSegments(lines);
        var text = OcrTextSegmenter.ComposeText(segments);

        Assert.Equal("international", text);
    }
}
