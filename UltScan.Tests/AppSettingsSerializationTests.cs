using System.Text.Json;
using Xunit;

namespace UltScan.Tests;

public sealed class AppSettingsSerializationTests
{
    [Fact]
    public void AppSettings_RoundTrip_PreservesCriticalValues()
    {
        var source = new AppSettings
        {
            LocaleId = "ru",
            AutoStart = true,
            ExperimentalMode = true,
            ExperimentalImagePreprocessing = true,
            ExperimentalTranslationLogging = true,
            HotKey = new HotKeyConfig
            {
                Id = "custom",
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                VirtualKey = 0x4F
            },
            Overlay = new OverlaySettings
            {
                Orientation = OverlayOrientation.Bottom,
                Opacity = 0.72,
                CtrlResizeEnabled = false,
                AutoHideWhenNoText = true,
                AutoHideNoTextHideDelayMs = 600,
                AutoHideNoTextShowDelayMs = 150,
                AutoHideNoTextEmptyFrames = 3,
                AutoHideNoTextShowDormantFrame = false
            },
            Translation = new TranslationSettings
            {
                Enabled = true,
                Mode = TranslationMode.VisualNovel,
                SourceLanguage = "en",
                TargetLanguage = "ru",
                PollIntervalMs = 450,
                Provider = TranslationService.ProviderApi,
                ApiKey = "test-key"
            }
        };

        var json = JsonSerializer.Serialize(source);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("ru", restored!.LocaleId);
        Assert.True(restored.AutoStart);
        Assert.True(restored.ExperimentalMode);
        Assert.True(restored.ExperimentalImagePreprocessing);
        Assert.True(restored.ExperimentalTranslationLogging);
        Assert.Equal("custom", restored.HotKey.Id);
        Assert.Equal(OverlayOrientation.Bottom, restored.Overlay.Orientation);
        Assert.Equal(0.72, restored.Overlay.Opacity, 3);
        Assert.Equal(TranslationMode.VisualNovel, restored.Translation.Mode);
        Assert.Equal("en", restored.Translation.SourceLanguage);
        Assert.Equal("ru", restored.Translation.TargetLanguage);
        Assert.Equal(TranslationService.ProviderApi, restored.Translation.Provider);
    }

    [Fact]
    public void AppSettings_Deserialize_WithMissingSections_UsesPropertyDefaults()
    {
        const string json = """
        {
          "LocaleId": "en",
          "Translation": {
            "TargetLanguage": "ja"
          }
        }
        """;

        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("en", restored!.LocaleId);
        Assert.NotNull(restored.HotKey);
        Assert.NotNull(restored.Overlay);
        Assert.NotNull(restored.Translation);
        Assert.Equal("ja", restored.Translation.TargetLanguage);
        Assert.Equal("auto", restored.Translation.SourceLanguage);
        Assert.Equal(TranslationService.ProviderWeb, restored.Translation.Provider);
    }
}
