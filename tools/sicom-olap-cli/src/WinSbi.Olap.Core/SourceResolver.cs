namespace WinSbi.Olap.Core;

public sealed class SourceResolver
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public SourceResolver(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    public IReadOnlyList<SourceDefinition> ListKnownSources()
    {
        var sources = new List<SourceDefinition>();
        AddIfConfigured(sources, "agents", "CUBO_AGENTS_URL");
        AddIfConfigured(sources, "liqs", "CUBO_LIQS_URL");
        return sources.OrderBy(static source => source.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public SourceConnection ResolveConnection(
        string source,
        string? customUrl,
        string? user,
        string? password,
        string authMode,
        int timeoutSeconds)
    {
        return new SourceConnection(
            ResolveSource(source, customUrl),
            string.IsNullOrWhiteSpace(user) ? GetRequired("USER_CUBO_SICOM") : user,
            string.IsNullOrWhiteSpace(password) ? GetRequired("PASSWORD_CUBO_SICOM") : password,
            NormalizeAuthMode(authMode),
            Math.Max(1, timeoutSeconds));
    }

    public SourceDefinition ResolveSource(string source, string? customUrl)
    {
        if (!string.IsNullOrWhiteSpace(customUrl))
        {
            return new SourceDefinition("custom", customUrl);
        }

        return source.ToLowerInvariant() switch
        {
            "agents" => new SourceDefinition("agents", GetRequired("CUBO_AGENTS_URL")),
            "liqs" => new SourceDefinition("liqs", GetRequired("CUBO_LIQS_URL")),
            _ => throw new InvalidOperationException($"Unknown source '{source}'. Use 'liqs', 'agents', or pass --url.")
        };
    }

    private void AddIfConfigured(List<SourceDefinition> sources, string name, string secretKey)
    {
        if (_values.TryGetValue(secretKey, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            sources.Add(new SourceDefinition(name, value));
        }
    }

    private string GetRequired(string key)
    {
        if (_values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required secret or environment variable '{key}'.");
    }

    private static string NormalizeAuthMode(string authMode)
    {
        var normalized = string.IsNullOrWhiteSpace(authMode) ? "basic" : authMode.Trim().ToLowerInvariant();
        return normalized is "basic" or "challenge"
            ? normalized
            : throw new ArgumentException("Auth mode must be 'basic' or 'challenge'.", nameof(authMode));
    }
}
