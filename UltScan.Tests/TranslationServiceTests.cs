using Xunit;

namespace UltScan.Tests;

public sealed class TranslationServiceTests
{
    [Fact]
    public async Task ProbeTranslationAsync_NullSettings_ReturnsFail()
    {
        var result = await TranslationService.ProbeTranslationAsync(
            settings: null!,
            text: "Hello",
            sourceLanguage: "en");

        Assert.False(result.Success);
        Assert.Equal("SETTINGS_MISSING", result.ErrorCode);
    }

    [Fact]
    public async Task ProbeTranslationAsync_MissingTargetLanguage_ReturnsFail()
    {
        var settings = new TranslationSettings
        {
            Provider = TranslationService.ProviderWeb,
            TargetLanguage = ""
        };

        var result = await TranslationService.ProbeTranslationAsync(settings, "Hello", "en");

        Assert.False(result.Success);
        Assert.Equal("TARGET_LANGUAGE_MISSING", result.ErrorCode);
    }

    [Fact]
    public async Task ProbeTranslationAsync_ApiProviderWithoutKey_ReturnsFail()
    {
        var settings = new TranslationSettings
        {
            Provider = TranslationService.ProviderApi,
            SourceLanguage = "en",
            TargetLanguage = "ru",
            ApiKey = ""
        };

        var result = await TranslationService.ProbeTranslationAsync(settings, "Hello", "en");

        Assert.False(result.Success);
        Assert.Equal("API_KEY_MISSING", result.ErrorCode);
    }

    [Fact]
    public void GetConfiguredApiKey_PrefersOverride()
    {
        const string overrideValue = "override-key";
        var actual = TranslationService.GetConfiguredApiKey(overrideValue);
        Assert.Equal(overrideValue, actual);
    }

    [Fact]
    public void GetConfiguredServiceAccountPath_PrefersOverride()
    {
        const string overridePath = @"C:\temp\service-account.json";
        var actual = TranslationService.GetConfiguredServiceAccountPath(overridePath);
        Assert.Equal(overridePath, actual);
    }
}
