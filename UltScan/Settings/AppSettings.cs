using System;
using System.IO;
using System.Text.Json;

namespace UltScan;

public sealed class AppSettings
{
    public HotKeyConfig HotKey { get; set; } = HotKeyPresets.Default.ToConfig();
    public string LocaleId { get; set; } = string.Empty;
    public bool AutoStart { get; set; }
    public bool ExperimentalMode { get; set; }
    public TranslationSettings Translation { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public WordHoverSettings WordHover { get; set; } = new();
    public bool ExperimentalImagePreprocessing { get; set; }
    public bool ExperimentalTranslationLogging { get; set; }

    public static AppSettings Default => new();

    public static string SettingsPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "UltScan", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return Default;
            }

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? Default;
        }
        catch
        {
            return Default;
        }
    }

    public void Save()
    {
        var path = SettingsPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }
}

public sealed class OverlaySettings
{
    public OverlayOrientation Orientation { get; set; } = OverlayOrientation.Right;
    public double Opacity { get; set; } = 0.6;
    public bool CtrlResizeEnabled { get; set; } = true;
    public bool AutoHideWhenNoText { get; set; }
    public int AutoHideNoTextHideDelayMs { get; set; } = 450;
    public int AutoHideNoTextShowDelayMs { get; set; } = 120;
    public int AutoHideNoTextEmptyFrames { get; set; } = 2;
    public bool AutoHideNoTextShowDormantFrame { get; set; } = true;
}

public sealed class WordHoverSettings
{
    public bool Enabled { get; set; }
    public bool RequireModifier { get; set; } = true;
    public ModifierKeys ModifierKey { get; set; } = ModifierKeys.Alt;
    public int PollIntervalMs { get; set; } = 160;
    public int InitialCaptureWidth { get; set; } = 200;
    public int InitialCaptureHeight { get; set; } = 100;
    public int MaxCaptureWidth { get; set; } = 440;
    public int MaxCaptureHeight { get; set; } = 220;
    public bool ShowTranslationAlternatives { get; set; }
    public bool EnableTts { get; set; } = true;
    public int AutoHideDelayMs { get; set; } = 600;
    public bool PinByDefault { get; set; }
}

public enum OverlayOrientation
{
    Right,
    Bottom,
    Left,
    Top
}

public sealed class TranslationSettings
{
    public bool Enabled { get; set; } = true;
    public bool TranslatedBold { get; set; }
    public string TranslatedTextColor { get; set; } = "#4CD964";
    public string CaptionTextColor { get; set; } = "#C8FFD4";
    public bool AlternateColorSchemeEnabled { get; set; }
    public string AlternateTranslatedTextColor { get; set; } = "#64B5F6";
    public string AlternateCaptionTextColor { get; set; } = "#CFE8FF";
    public string OverlayFontFamily { get; set; } = string.Empty;
    public double OverlayFontSize { get; set; } = 16;
    public TranslationMode Mode { get; set; } = TranslationMode.Standard;
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = string.Empty;
    public int StabilizationMs { get; set; } = 700;
    public double MinChangeRatio { get; set; } = 0.25;
    public int PollIntervalMs { get; set; } = 500;
    public string ProjectId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ServiceAccountJsonPath { get; set; } = string.Empty;
    public string Provider { get; set; } = TranslationService.ProviderWeb;
}

public enum TranslationMode
{
    Standard,
    VisualNovel
}

public sealed class HotKeyConfig
{
    public string Id { get; set; } = HotKeyPresets.Default.Id;
    public ModifierKeys Modifiers { get; set; } = HotKeyPresets.Default.Modifiers;
    public uint VirtualKey { get; set; } = HotKeyPresets.Default.VirtualKey;
}


