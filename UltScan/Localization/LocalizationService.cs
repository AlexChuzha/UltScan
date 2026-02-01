using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UltScan;

public sealed class LocalizationService
{
    private readonly Dictionary<string, LocalizationCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private LocalizationCatalog? _fallback;
    private LocalizationCatalog? _current;

    public event EventHandler? LocaleChanged;

    public IReadOnlyList<LocaleOption> Locales { get; private set; } = Array.Empty<LocaleOption>();

    public string CurrentLocaleId => _current?.Id ?? _fallback?.Id ?? "en";

    public static LocalizationService LoadFromDisk()
    {
        var service = new LocalizationService();
        var root = Path.Combine(AppContext.BaseDirectory, "Locales");
        if (!Directory.Exists(root))
        {
            return service;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var catalog = JsonSerializer.Deserialize<LocalizationCatalog>(json, options);
                if (catalog == null || string.IsNullOrWhiteSpace(catalog.Id))
                {
                    continue;
                }

                service._catalogs[catalog.Id] = catalog;
            }
            catch
            {
                // ignore broken localization file
            }
        }

        service.Initialize();
        return service;
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (_current != null && _current.Strings.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_fallback != null && _fallback.Strings.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    public string GetBestLocaleId(string? preferredId)
    {
        if (_catalogs.Count == 0)
        {
            return "en";
        }

        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            if (_catalogs.ContainsKey(preferredId))
            {
                return preferredId;
            }

            var neutral = preferredId.Split('-')[0];
            if (_catalogs.ContainsKey(neutral))
            {
                return neutral;
            }
        }

        var osCulture = CultureInfo.CurrentUICulture.Name;
        if (_catalogs.ContainsKey(osCulture))
        {
            return osCulture;
        }

        var osNeutral = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (_catalogs.ContainsKey(osNeutral))
        {
            return osNeutral;
        }

        if (_catalogs.ContainsKey("en"))
        {
            return "en";
        }

        return _catalogs.Keys.First();
    }

    public void SetLocale(string? localeId)
    {
        var best = GetBestLocaleId(localeId);
        if (!_catalogs.TryGetValue(best, out var catalog))
        {
            return;
        }

        if (_current != null && string.Equals(_current.Id, catalog.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _current = catalog;
        LocaleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Initialize()
    {
        _fallback = _catalogs.TryGetValue("en", out var en) ? en : _catalogs.Values.FirstOrDefault();
        Locales = _catalogs.Values
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new LocaleOption(c.Id, c.Name))
            .ToList();
    }
}

public sealed record LocaleOption(string Id, string Name);
