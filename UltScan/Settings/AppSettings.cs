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
    public bool ExperimentalImagePreprocessing { get; set; }

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
    public TranslationMode Mode { get; set; } = TranslationMode.Standard;
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = string.Empty;
    public int StabilizationMs { get; set; } = 700;
    public double MinChangeRatio { get; set; } = 0.25;
    public int PollIntervalMs { get; set; } = 500;
    public string ProjectId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
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
