using System;
using System.IO;
using System.Text.Json;

namespace UltScan;

public sealed class AppSettings
{
    public HotKeyConfig HotKey { get; set; } = HotKeyPresets.Default.ToConfig();

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

public sealed class HotKeyConfig
{
    public string Id { get; set; } = HotKeyPresets.Default.Id;
    public ModifierKeys Modifiers { get; set; } = HotKeyPresets.Default.Modifiers;
    public uint VirtualKey { get; set; } = HotKeyPresets.Default.VirtualKey;
}
