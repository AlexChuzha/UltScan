using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace UltScan;

public static class TranslationConnectionDiagnostics
{
    public static async Task<TranslationConnectionDiagnosticResult> RunAsync(TranslationSettings settings)
    {
        var checks = new List<TranslationConnectionCheck>();
        var provider = string.IsNullOrWhiteSpace(settings.Provider)
            ? TranslationService.ProviderWeb
            : settings.Provider;

        checks.Add(new TranslationConnectionCheck(
            "provider",
            ok: true,
            messageKey: "Diag.ProviderSelected"));

        if (string.IsNullOrWhiteSpace(settings.TargetLanguage))
        {
            checks.Add(new TranslationConnectionCheck(
                "target_language",
                ok: false,
                messageKey: "Diag.TargetLanguageMissing"));
            return new TranslationConnectionDiagnosticResult(provider, checks);
        }

        checks.Add(new TranslationConnectionCheck(
            "target_language",
            ok: true,
            messageKey: "Diag.TargetLanguageOk"));

        if (string.Equals(provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase))
        {
            var key = TranslationService.GetConfiguredApiKey(settings.ApiKey);
            var hasKey = !string.IsNullOrWhiteSpace(key);
            checks.Add(new TranslationConnectionCheck(
                "api_key",
                ok: hasKey,
                messageKey: hasKey ? "Diag.ApiKeyOk" : "Diag.ApiKeyMissing"));
            if (!hasKey)
            {
                return new TranslationConnectionDiagnosticResult(provider, checks);
            }
        }
        else if (string.Equals(provider, TranslationService.ProviderApiV3OAuth, StringComparison.OrdinalIgnoreCase))
        {
            var hasProjectId = !string.IsNullOrWhiteSpace(settings.ProjectId);
            checks.Add(new TranslationConnectionCheck(
                "project_id",
                ok: hasProjectId,
                messageKey: hasProjectId ? "Diag.ProjectIdOk" : "Diag.ProjectIdMissing"));
            if (!hasProjectId)
            {
                return new TranslationConnectionDiagnosticResult(provider, checks);
            }

            var credentialsPath = TranslationService.GetConfiguredServiceAccountPath(settings.ServiceAccountJsonPath);
            var hasPath = !string.IsNullOrWhiteSpace(credentialsPath);
            checks.Add(new TranslationConnectionCheck(
                "service_account_path",
                ok: hasPath,
                messageKey: hasPath ? "Diag.ServiceAccountPathOk" : "Diag.ServiceAccountPathMissing",
                details: hasPath ? credentialsPath : null));
            if (!hasPath)
            {
                return new TranslationConnectionDiagnosticResult(provider, checks);
            }

            var exists = File.Exists(credentialsPath);
            checks.Add(new TranslationConnectionCheck(
                "service_account_exists",
                ok: exists,
                messageKey: exists ? "Diag.ServiceAccountFileOk" : "Diag.ServiceAccountFileMissing",
                details: credentialsPath));
            if (!exists)
            {
                return new TranslationConnectionDiagnosticResult(provider, checks);
            }

            var validCredentials = await ValidateServiceAccountJsonAsync(credentialsPath!).ConfigureAwait(false);
            checks.Add(new TranslationConnectionCheck(
                "service_account_json",
                ok: validCredentials,
                messageKey: validCredentials ? "Diag.ServiceAccountJsonOk" : "Diag.ServiceAccountJsonInvalid",
                details: credentialsPath));
            if (!validCredentials)
            {
                return new TranslationConnectionDiagnosticResult(provider, checks);
            }
        }

        var probe = await TranslationService.ProbeTranslationAsync(settings, text: "Hello", sourceLanguage: "en").ConfigureAwait(false);
        if (probe.Success)
        {
            checks.Add(new TranslationConnectionCheck(
                "test_call",
                ok: true,
                messageKey: "Diag.TestCallOk",
                details: probe.TranslatedText));
        }
        else
        {
            var details = string.IsNullOrWhiteSpace(probe.ErrorCode)
                ? probe.ErrorMessage
                : $"{probe.ErrorCode}: {probe.ErrorMessage}";
            if (probe.HttpStatusCode.HasValue)
            {
                details = $"HTTP {probe.HttpStatusCode.Value}. {details}";
            }

            checks.Add(new TranslationConnectionCheck(
                "test_call",
                ok: false,
                messageKey: "Diag.TestCallFailed",
                details: details));
        }

        return new TranslationConnectionDiagnosticResult(provider, checks);
    }

    private static async Task<bool> ValidateServiceAccountJsonAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var credentials = JsonSerializer.Deserialize<ServiceAccountSchema>(json);
            return credentials != null &&
                   !string.IsNullOrWhiteSpace(credentials.ClientEmail) &&
                   !string.IsNullOrWhiteSpace(credentials.PrivateKey);
        }
        catch
        {
            return false;
        }
    }

    private sealed class ServiceAccountSchema
    {
        [JsonPropertyName("client_email")]
        public string? ClientEmail { get; set; }

        [JsonPropertyName("private_key")]
        public string? PrivateKey { get; set; }
    }
}

public sealed class TranslationConnectionDiagnosticResult
{
    public TranslationConnectionDiagnosticResult(string provider, IReadOnlyList<TranslationConnectionCheck> checks)
    {
        Provider = provider;
        Checks = checks;
    }

    public string Provider { get; }
    public IReadOnlyList<TranslationConnectionCheck> Checks { get; }
    public bool IsSuccess => Checks.Count > 0 && Checks.All(c => c.Ok);
}

public sealed class TranslationConnectionCheck
{
    public TranslationConnectionCheck(string id, bool ok, string messageKey, string? details = null)
    {
        Id = id;
        Ok = ok;
        MessageKey = messageKey;
        Details = details;
    }

    public string Id { get; }
    public bool Ok { get; }
    public string MessageKey { get; }
    public string? Details { get; }
}
