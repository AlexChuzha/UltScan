using System;

namespace UltScan;

public sealed class HoverOcrOptions
{
    public int InitialCaptureWidth { get; init; } = 200;
    public int InitialCaptureHeight { get; init; } = 100;
    public int MaxCaptureWidth { get; init; } = 440;
    public int MaxCaptureHeight { get; init; } = 220;
    public double GrowthFactor { get; init; } = 1.3;
    public double WordPaddingFactor { get; init; } = 0.2;

    public static HoverOcrOptions FromSettings(WordHoverSettings settings)
    {
        if (settings == null)
        {
            return new HoverOcrOptions();
        }

        return new HoverOcrOptions
        {
            InitialCaptureWidth = Math.Clamp(settings.InitialCaptureWidth, 120, 600),
            InitialCaptureHeight = Math.Clamp(settings.InitialCaptureHeight, 60, 400),
            MaxCaptureWidth = Math.Clamp(settings.MaxCaptureWidth, 200, 1000),
            MaxCaptureHeight = Math.Clamp(settings.MaxCaptureHeight, 100, 600)
        };
    }
}
