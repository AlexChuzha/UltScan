using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace UltScan;

public static class TranslationService
{
    private static readonly HttpClient Client = new();
    private static readonly object OAuthSync = new();
    private static string? _cachedOAuthAccessToken;
    private static DateTime _cachedOAuthAccessTokenExpiresUtc = DateTime.MinValue;

    public static string ApiKeyEnvName => "ULTSCAN_GOOGLE_TRANSLATE_API_KEY";
    public static string GoogleApplicationCredentialsEnvName => "GOOGLE_APPLICATION_CREDENTIALS";
    public const string ProviderApi = "api";
    public const string ProviderApiV3OAuth = "api_v3_oauth";
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

        if (string.Equals(provider, ProviderApiV3OAuth, StringComparison.OrdinalIgnoreCase))
        {
            return await TranslateViaApiV3OAuthAsync(text, sourceLanguage, targetLanguage, projectId).ConfigureAwait(false);
        }

        return await TranslateViaApiAsync(text, sourceLanguage, targetLanguage, projectId, apiKeyOverride).ConfigureAwait(false);
    }

    public static async Task<TranslationProbeResult> ProbeTranslationAsync(
        TranslationSettings settings,
        string text,
        string sourceLanguage)
    {
        if (settings == null)
        {
            return TranslationProbeResult.Fail("SETTINGS_MISSING", "Translation settings are missing.");
        }

        var provider = string.IsNullOrWhiteSpace(settings.Provider)
            ? ProviderWeb
            : settings.Provider;
        var targetLanguage = settings.TargetLanguage;
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            return TranslationProbeResult.Fail("TARGET_LANGUAGE_MISSING", "Target language is not configured.");
        }

        if (string.Equals(provider, ProviderApi, StringComparison.OrdinalIgnoreCase))
        {
            return await ProbeViaApiV2Async(
                text,
                sourceLanguage,
                targetLanguage,
                settings.ApiKey).ConfigureAwait(false);
        }

        if (string.Equals(provider, ProviderApiV3OAuth, StringComparison.OrdinalIgnoreCase))
        {
            return await ProbeViaApiV3OAuthAsync(
                text,
                sourceLanguage,
                targetLanguage,
                settings.ProjectId,
                settings.ServiceAccountJsonPath).ConfigureAwait(false);
        }

        try
        {
            var translated = await TranslateAsync(
                text,
                sourceLanguage,
                targetLanguage,
                settings.ProjectId,
                settings.ApiKey,
                provider).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(translated))
            {
                return TranslationProbeResult.Fail("EMPTY_TRANSLATION", "Translation request returned empty result.");
            }

            return TranslationProbeResult.Ok(translated);
        }
        catch (Exception ex)
        {
            return TranslationProbeResult.Fail("EXCEPTION", ex.Message);
        }
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

    private static async Task<string?> TranslateViaApiV3OAuthAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string projectId)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(targetLanguage) || string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var token = await GetOAuthAccessTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var normalizedTargetLanguage = NormalizeLanguageCode(targetLanguage);
        var normalizedSourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim();
        var url = $"https://translation.googleapis.com/v3/projects/{Uri.EscapeDataString(projectId)}/locations/global:translateText";
        var request = new TranslateV3Request
        {
            Contents = new[] { text },
            MimeType = "text/plain",
            SourceLanguageCode = string.Equals(normalizedSourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : normalizedSourceLanguage,
            TargetLanguageCode = normalizedTargetLanguage
        };

        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await Client.SendAsync(message).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TranslateV3Response>(responseJson, JsonOptions.Default);
        return result?.Translations != null && result.Translations.Length > 0
            ? result.Translations[0].TranslatedText
            : null;
    }

    private static async Task<TranslationProbeResult> ProbeViaApiV3OAuthAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string projectId,
        string? serviceAccountJsonPath)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return TranslationProbeResult.Fail("PROJECT_ID_MISSING", "Project ID is not configured.");
        }

        var tokenResult = await GetOAuthAccessTokenResultAsync(serviceAccountJsonPath).ConfigureAwait(false);
        if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.AccessToken))
        {
            return TranslationProbeResult.Fail("OAUTH_TOKEN_ERROR", tokenResult.ErrorMessage ?? "OAuth token acquisition failed.");
        }

        var normalizedTargetLanguage = NormalizeLanguageCode(targetLanguage);
        var normalizedSourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim();
        var url = $"https://translation.googleapis.com/v3/projects/{Uri.EscapeDataString(projectId)}/locations/global:translateText";
        var request = new TranslateV3Request
        {
            Contents = new[] { text },
            MimeType = "text/plain",
            SourceLanguageCode = string.Equals(normalizedSourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : normalizedSourceLanguage,
            TargetLanguageCode = normalizedTargetLanguage
        };

        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

        using var response = await Client.SendAsync(message).ConfigureAwait(false);
        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = BuildGoogleApiError(responseJson, response.StatusCode);
            return TranslationProbeResult.Fail(error.Code, error.Message, (int)response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<TranslateV3Response>(responseJson, JsonOptions.Default);
        var translated = parsed?.Translations != null && parsed.Translations.Length > 0
            ? parsed.Translations[0].TranslatedText
            : null;
        if (string.IsNullOrWhiteSpace(translated))
        {
            return TranslationProbeResult.Fail("EMPTY_TRANSLATION", "Translation request returned empty result.", (int)response.StatusCode);
        }

        return TranslationProbeResult.Ok(translated, (int)response.StatusCode);
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

        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            return null;
        }

        var apiKey = GetConfiguredApiKey(apiKeyOverride);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var normalizedTargetLanguage = NormalizeLanguageCode(targetLanguage);
        var normalizedSourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim();
        var url = $"https://translation.googleapis.com/language/translate/v2?key={Uri.EscapeDataString(apiKey)}";

        var request = new TranslateV2Request
        {
            Q = text,
            Target = normalizedTargetLanguage,
            Source = string.Equals(normalizedSourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : normalizedSourceLanguage,
            Format = "text"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Client.PostAsync(url, content).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<TranslateV2ResponseEnvelope>(responseJson, JsonOptions.Default);
        var translated = result?.Data?.Translations != null && result.Data.Translations.Length > 0
            ? result.Data.Translations[0].TranslatedText
            : null;
        return translated == null ? null : WebUtility.HtmlDecode(translated);
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

    private static async Task<TranslationProbeResult> ProbeViaApiV2Async(
        string text,
        string sourceLanguage,
        string targetLanguage,
        string? apiKeyOverride)
    {
        var apiKey = GetConfiguredApiKey(apiKeyOverride);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TranslationProbeResult.Fail("API_KEY_MISSING", "API key is not configured.");
        }

        var normalizedTargetLanguage = NormalizeLanguageCode(targetLanguage);
        var normalizedSourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "auto" : sourceLanguage.Trim();
        var url = $"https://translation.googleapis.com/language/translate/v2?key={Uri.EscapeDataString(apiKey)}";
        var request = new TranslateV2Request
        {
            Q = text,
            Target = normalizedTargetLanguage,
            Source = string.Equals(normalizedSourceLanguage, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : normalizedSourceLanguage,
            Format = "text"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions.Default);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(url, content).ConfigureAwait(false);
        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = BuildGoogleApiError(responseJson, response.StatusCode);
            return TranslationProbeResult.Fail(error.Code, error.Message, (int)response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<TranslateV2ResponseEnvelope>(responseJson, JsonOptions.Default);
        var translated = parsed?.Data?.Translations != null && parsed.Data.Translations.Length > 0
            ? parsed.Data.Translations[0].TranslatedText
            : null;
        if (string.IsNullOrWhiteSpace(translated))
        {
            return TranslationProbeResult.Fail("EMPTY_TRANSLATION", "Translation request returned empty result.", (int)response.StatusCode);
        }

        return TranslationProbeResult.Ok(WebUtility.HtmlDecode(translated), (int)response.StatusCode);
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

    private static async Task<string?> GetOAuthAccessTokenAsync()
    {
        lock (OAuthSync)
        {
            if (!string.IsNullOrWhiteSpace(_cachedOAuthAccessToken) &&
                DateTime.UtcNow < _cachedOAuthAccessTokenExpiresUtc.AddSeconds(-60))
            {
                return _cachedOAuthAccessToken;
            }
        }

        var settingsPath = GetServiceAccountPathFromCurrentSettings();
        var credentialsPath = GetConfiguredServiceAccountPath(settingsPath);
        if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath))
        {
            return null;
        }

        ServiceAccountCredentials? credentials;
        try
        {
            var json = await File.ReadAllTextAsync(credentialsPath).ConfigureAwait(false);
            credentials = JsonSerializer.Deserialize<ServiceAccountCredentials>(json, JsonOptions.Default);
        }
        catch
        {
            return null;
        }

        if (credentials == null ||
            string.IsNullOrWhiteSpace(credentials.ClientEmail) ||
            string.IsNullOrWhiteSpace(credentials.PrivateKey))
        {
            return null;
        }

        var tokenUri = string.IsNullOrWhiteSpace(credentials.TokenUri)
            ? "https://oauth2.googleapis.com/token"
            : credentials.TokenUri;

        string jwt;
        try
        {
            jwt = BuildServiceAccountJwt(credentials.ClientEmail, credentials.PrivateKey, tokenUri);
        }
        catch
        {
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await Client.PostAsync(tokenUri, content).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        OAuthTokenResponse? token;
        try
        {
            token = JsonSerializer.Deserialize<OAuthTokenResponse>(responseJson, JsonOptions.Default);
        }
        catch
        {
            return null;
        }

        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return null;
        }

        var expiresIn = token.ExpiresIn > 0 ? token.ExpiresIn : 3600;
        lock (OAuthSync)
        {
            _cachedOAuthAccessToken = token.AccessToken;
            _cachedOAuthAccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            return _cachedOAuthAccessToken;
        }
    }

    private static string BuildServiceAccountJwt(string clientEmail, string privateKeyPem, string audience)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var headerJson = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";
        var claimsJson =
            "{\"iss\":\"" + JsonEncodedText.Encode(clientEmail).ToString() + "\"," +
            "\"scope\":\"https://www.googleapis.com/auth/cloud-translation\"," +
            "\"aud\":\"" + JsonEncodedText.Encode(audience).ToString() + "\"," +
            "\"iat\":" + now.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
            "\"exp\":" + (now + 3600).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(claimsJson));
        var unsignedToken = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem.AsSpan());
        var signatureBytes = rsa.SignData(
            Encoding.UTF8.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{unsignedToken}.{Base64UrlEncode(signatureBytes)}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string? GetConfiguredApiKey(string? apiKeyOverride)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
        {
            return apiKeyOverride.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnvName);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv.Trim();
    }

    public static string? GetConfiguredServiceAccountPath(string? serviceAccountJsonPath)
    {
        if (!string.IsNullOrWhiteSpace(serviceAccountJsonPath))
        {
            return serviceAccountJsonPath.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable(GoogleApplicationCredentialsEnvName);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv.Trim();
    }

    private static async Task<AccessTokenResult> GetOAuthAccessTokenResultAsync(string? serviceAccountJsonPath)
    {
        lock (OAuthSync)
        {
            if (!string.IsNullOrWhiteSpace(_cachedOAuthAccessToken) &&
                DateTime.UtcNow < _cachedOAuthAccessTokenExpiresUtc.AddSeconds(-60))
            {
                return AccessTokenResult.Ok(_cachedOAuthAccessToken);
            }
        }

        var credentialsPath = GetConfiguredServiceAccountPath(serviceAccountJsonPath);
        if (string.IsNullOrWhiteSpace(credentialsPath))
        {
            return AccessTokenResult.Fail("Service account JSON path is not configured.");
        }

        if (!File.Exists(credentialsPath))
        {
            return AccessTokenResult.Fail($"Service account JSON file not found: {credentialsPath}");
        }

        ServiceAccountCredentials? credentials;
        try
        {
            var json = await File.ReadAllTextAsync(credentialsPath).ConfigureAwait(false);
            credentials = JsonSerializer.Deserialize<ServiceAccountCredentials>(json, JsonOptions.Default);
        }
        catch (Exception ex)
        {
            return AccessTokenResult.Fail($"Failed to read service account JSON: {ex.Message}");
        }

        if (credentials == null ||
            string.IsNullOrWhiteSpace(credentials.ClientEmail) ||
            string.IsNullOrWhiteSpace(credentials.PrivateKey))
        {
            return AccessTokenResult.Fail("Service account JSON does not contain required fields: client_email/private_key.");
        }

        var tokenUri = string.IsNullOrWhiteSpace(credentials.TokenUri)
            ? "https://oauth2.googleapis.com/token"
            : credentials.TokenUri;

        string jwt;
        try
        {
            jwt = BuildServiceAccountJwt(credentials.ClientEmail, credentials.PrivateKey, tokenUri);
        }
        catch (Exception ex)
        {
            return AccessTokenResult.Fail($"Failed to build JWT assertion: {ex.Message}");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await Client.PostAsync(tokenUri, content).ConfigureAwait(false);
        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = BuildGoogleApiError(responseJson, response.StatusCode);
            return AccessTokenResult.Fail($"OAuth token request failed. HTTP {(int)response.StatusCode}: {error.Code} {error.Message}");
        }

        OAuthTokenResponse? token;
        try
        {
            token = JsonSerializer.Deserialize<OAuthTokenResponse>(responseJson, JsonOptions.Default);
        }
        catch (Exception ex)
        {
            return AccessTokenResult.Fail($"Failed to parse OAuth token response: {ex.Message}");
        }

        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return AccessTokenResult.Fail("OAuth token response does not contain access_token.");
        }

        var expiresIn = token.ExpiresIn > 0 ? token.ExpiresIn : 3600;
        lock (OAuthSync)
        {
            _cachedOAuthAccessToken = token.AccessToken;
            _cachedOAuthAccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            return AccessTokenResult.Ok(_cachedOAuthAccessToken);
        }
    }

    private static string? GetServiceAccountPathFromCurrentSettings()
    {
        if (System.Windows.Application.Current is not App app)
        {
            return null;
        }

        return app.Settings?.Translation?.ServiceAccountJsonPath;
    }

    private static (string Code, string Message) BuildGoogleApiError(string responseJson, System.Net.HttpStatusCode statusCode)
    {
        var fallbackCode = $"HTTP_{(int)statusCode}";
        var fallbackMessage = $"Request failed with status {(int)statusCode}.";
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return (fallbackCode, fallbackMessage);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                return (fallbackCode, fallbackMessage + " Raw: " + responseJson);
            }

            string code = fallbackCode;
            string message = fallbackMessage;

            if (errorElement.TryGetProperty("status", out var statusElement) &&
                statusElement.ValueKind == JsonValueKind.String)
            {
                code = statusElement.GetString() ?? fallbackCode;
            }
            else if (errorElement.TryGetProperty("code", out var codeElement) &&
                     codeElement.ValueKind == JsonValueKind.Number)
            {
                code = "HTTP_" + codeElement.GetInt32().ToString();
            }

            if (errorElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString() ?? fallbackMessage;
            }

            return (code, message);
        }
        catch
        {
            return (fallbackCode, fallbackMessage + " Raw: " + responseJson);
        }
    }

    private sealed class TranslateV2Request
    {
        public string Q { get; set; } = string.Empty;
        public string? Source { get; set; }
        public string Target { get; set; } = string.Empty;
        public string Format { get; set; } = "text";
    }

    private sealed class TranslateV2ResponseEnvelope
    {
        public TranslateV2Data? Data { get; set; }
    }

    private sealed class TranslateV2Data
    {
        public TranslationItem[]? Translations { get; set; }
    }

    private sealed class TranslateV3Request
    {
        public string[] Contents { get; set; } = Array.Empty<string>();
        public string? SourceLanguageCode { get; set; }
        public string TargetLanguageCode { get; set; } = string.Empty;
        public string MimeType { get; set; } = "text/plain";
    }

    private sealed class TranslateV3Response
    {
        public TranslationItem[]? Translations { get; set; }
    }

    private sealed class TranslationItem
    {
        public string TranslatedText { get; set; } = string.Empty;
    }

    private sealed class ServiceAccountCredentials
    {
        [JsonPropertyName("client_email")]
        public string? ClientEmail { get; set; }

        [JsonPropertyName("private_key")]
        public string? PrivateKey { get; set; }

        [JsonPropertyName("token_uri")]
        public string? TokenUri { get; set; }
    }

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class AccessTokenResult
    {
        private AccessTokenResult(bool success, string? accessToken, string? errorMessage)
        {
            Success = success;
            AccessToken = accessToken;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public string? AccessToken { get; }
        public string? ErrorMessage { get; }

        public static AccessTokenResult Ok(string accessToken) => new(true, accessToken, null);
        public static AccessTokenResult Fail(string message) => new(false, null, message);
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}

public sealed class TranslationProbeResult
{
    private TranslationProbeResult(bool success, string? translatedText, string? errorCode, string? errorMessage, int? httpStatusCode)
    {
        Success = success;
        TranslatedText = translatedText;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        HttpStatusCode = httpStatusCode;
    }

    public bool Success { get; }
    public string? TranslatedText { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public int? HttpStatusCode { get; }

    public static TranslationProbeResult Ok(string translatedText, int? httpStatusCode = null)
        => new(true, translatedText, null, null, httpStatusCode);

    public static TranslationProbeResult Fail(string errorCode, string errorMessage, int? httpStatusCode = null)
        => new(false, null, errorCode, errorMessage, httpStatusCode);
}
