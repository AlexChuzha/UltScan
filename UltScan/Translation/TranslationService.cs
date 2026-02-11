using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace UltScan;

public static class TranslationService
{
    private static readonly HttpClient Client = new();

    public static string ApiKeyEnvName => "ULTSCAN_GOOGLE_TRANSLATE_API_KEY";
    public const string ProviderApi = "api";
    public const string ProviderWeb = "web";
    public const string ProviderWebSiteExperimental = "web_site_experimental";

    public static async Task<string?> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string projectId,
        string? apiKeyOverride,
        string provider)
    {
        if (string.Equals(provider, ProviderWebSiteExperimental, StringComparison.OrdinalIgnoreCase))
        {
            return await TranslateViaWebsiteExperimentalAsync(text, sourceLanguage, targetLanguage).ConfigureAwait(false);
        }

        if (string.Equals(provider, ProviderWeb, StringComparison.OrdinalIgnoreCase))
        {
            return await TranslateViaWebAsync(text, sourceLanguage, targetLanguage).ConfigureAwait(false);
        }

        return await TranslateViaApiAsync(text, sourceLanguage, targetLanguage, projectId, apiKeyOverride).ConfigureAwait(false);
    }

    private static async Task<string?> TranslateViaWebsiteExperimentalAsync(string text, string sourceLanguage, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            return null;
        }

        var sl = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;
        var tl = NormalizeLanguageCode(targetLanguage);

        var viaWebapp = await TranslateViaWebAppSingleAsync(text, sl, tl).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(viaWebapp))
        {
            return viaWebapp;
        }

        return await TranslateViaWebAsync(text, sl, tl).ConfigureAwait(false);
    }

    private static async Task<string?> TranslateViaApiAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string projectId,
        string? apiKeyOverride)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(targetLanguage) || string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var apiKey = !string.IsNullOrWhiteSpace(apiKeyOverride)
            ? apiKeyOverride
            : Environment.GetEnvironmentVariable(ApiKeyEnvName);
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

    private static async Task<string?> TranslateViaWebAsync(string text, string sourceLanguage, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            return null;
        }

        var sl = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage;
        var tl = NormalizeLanguageCode(targetLanguage);
        var url = $"https://translate.google.com/m?sl={Uri.EscapeDataString(sl)}&tl={Uri.EscapeDataString(tl)}&q={Uri.EscapeDataString(text)}";

        var html = await Client.GetStringAsync(url).ConfigureAwait(false);
        var match = Regex.Match(html, "class=\"result-container\">(?<t>.*?)</div>", RegexOptions.Singleline);
        if (!match.Success)
        {
            return await TranslateViaGtxAsync(text, sl, tl).ConfigureAwait(false);
        }

        var raw = match.Groups["t"].Value;
        var decoded = WebUtility.HtmlDecode(raw);
        return decoded;
    }

    private static async Task<string?> TranslateViaGtxAsync(string text, string sourceLanguage, string targetLanguage)
    {
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(sourceLanguage)}&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(text)}";
        var json = await Client.GetStringAsync(url).ConfigureAwait(false);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var segments = doc.RootElement[0];
            if (segments.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                {
                    continue;
                }

                var piece = segment[0];
                if (piece.ValueKind == JsonValueKind.String)
                {
                    sb.Append(piece.GetString());
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TranslateViaWebAppSingleAsync(string text, string sourceLanguage, string targetLanguage)
    {
        var url = $"https://translate.google.com/translate_a/single?client=webapp&sl={Uri.EscapeDataString(sourceLanguage)}&tl={Uri.EscapeDataString(targetLanguage)}&hl={Uri.EscapeDataString(targetLanguage)}&dt=t&dj=1&source=input&q={Uri.EscapeDataString(text)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

        using var response = await Client.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("sentences", out var sentences) ||
                sentences.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var sentence in sentences.EnumerateArray())
            {
                if (sentence.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!sentence.TryGetProperty("trans", out var trans) || trans.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                sb.Append(trans.GetString());
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeLanguageCode(string code)
    {
        if (string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        return code;
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
