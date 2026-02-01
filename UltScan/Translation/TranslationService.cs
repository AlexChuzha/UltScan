using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UltScan;

public static class TranslationService
{
    private static readonly HttpClient Client = new();

    public static string ApiKeyEnvName => "ULTSCAN_GOOGLE_TRANSLATE_API_KEY";

    public static async Task<string?> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string projectId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(targetLanguage) || string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var url = $"https://translation.googleapis.com/v3/projects/{Uri.EscapeDataString(projectId)}/locations/global:translateText?key={Uri.EscapeDataString(apiKey)}";

        var request = new TranslateRequest
        {
            Contents = new[] { text },
            MimeType = "text/plain",
            SourceLanguageCode = string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase) ? null : sourceLanguage,
            TargetLanguageCode = targetLanguage
        };

        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync(url, content).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TranslateResponse>(responseJson, JsonOptions.Default);
        return result?.Translations != null && result.Translations.Length > 0
            ? result.Translations[0].TranslatedText
            : null;
    }

    private sealed class TranslateRequest
    {
        public string[] Contents { get; set; } = Array.Empty<string>();
        public string? SourceLanguageCode { get; set; }
        public string TargetLanguageCode { get; set; } = string.Empty;
        public string? MimeType { get; set; }
    }

    private sealed class TranslateResponse
    {
        public TranslationItem[]? Translations { get; set; }
    }

    private sealed class TranslationItem
    {
        public string TranslatedText { get; set; } = string.Empty;
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
