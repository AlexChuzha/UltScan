using System.Collections.Generic;

namespace UltScan;

public sealed class LocalizationCatalog
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Dictionary<string, string> Strings { get; init; } = new();
}
