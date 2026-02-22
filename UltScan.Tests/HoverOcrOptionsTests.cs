using Xunit;

namespace UltScan.Tests;

public sealed class HoverOcrOptionsTests
{
    [Fact]
    public void FromSettings_Null_UsesDefaults()
    {
        var options = HoverOcrOptions.FromSettings(settings: null!);

        Assert.Equal(200, options.InitialCaptureWidth);
        Assert.Equal(100, options.InitialCaptureHeight);
        Assert.Equal(440, options.MaxCaptureWidth);
        Assert.Equal(220, options.MaxCaptureHeight);
    }

    [Fact]
    public void FromSettings_ClampsInvalidValues()
    {
        var settings = new WordHoverSettings
        {
            InitialCaptureWidth = 10,
            InitialCaptureHeight = 10,
            MaxCaptureWidth = 5000,
            MaxCaptureHeight = 5000
        };

        var options = HoverOcrOptions.FromSettings(settings);

        Assert.Equal(120, options.InitialCaptureWidth);
        Assert.Equal(60, options.InitialCaptureHeight);
        Assert.Equal(1000, options.MaxCaptureWidth);
        Assert.Equal(600, options.MaxCaptureHeight);
    }
}
