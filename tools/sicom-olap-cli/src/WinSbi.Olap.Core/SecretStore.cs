using System.Text.Json;
using System.Xml.Linq;

namespace WinSbi.Olap.Core;

public static class SecretStore
{
    public static readonly string[] KnownKeys =
    [
        "USER_CUBO_SICOM",
        "PASSWORD_CUBO_SICOM",
        "CUBO_LIQS_URL",
        "CUBO_AGENTS_URL"
    ];

    public static LoadedSecrets Load(string searchDirectory)
    {
        var userSecrets = LoadUserSecrets(searchDirectory);
        var envFilePath = FindEnvFile(searchDirectory);
        var envFile = envFilePath is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(
                EnvFileParser.Parse(File.ReadAllText(envFilePath)),
                StringComparer.OrdinalIgnoreCase);
        var environment = KnownKeys
            .Select(static key => new KeyValuePair<string, string?>(key, Environment.GetEnvironmentVariable(key)))
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
        var values = Merge(userSecrets, envFile, environment);

        return new LoadedSecrets(userSecrets, envFile, environment, values, envFilePath);
    }

    public static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> userSecrets,
        IReadOnlyDictionary<string, string> envFile,
        IReadOnlyDictionary<string, string> environment)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CopyInto(values, userSecrets);
        CopyInto(values, envFile);
        CopyInto(values, environment);
        return values;
    }

    public static string? FindEnvFile(string searchDirectory)
    {
        var candidates = new List<string>();
        AddWalkCandidates(candidates, Path.GetFullPath(searchDirectory));
        AddWalkCandidates(candidates, AppContext.BaseDirectory);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void AddWalkCandidates(List<string> candidates, string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            candidates.Add(Path.Combine(directory.FullName, ".env"));
            candidates.Add(Path.Combine(directory.FullName, "Cli-Tools", ".env"));
            directory = directory.Parent;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadUserSecrets(string searchDirectory)
    {
        var userSecretsId = FindUserSecretsId(searchDirectory);
        if (userSecretsId is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var secretsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".microsoft",
            "usersecrets",
            userSecretsId,
            "secrets.json");

        if (!File.Exists(secretsPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(secretsPath);
        using var json = JsonDocument.Parse(stream);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenJson(json.RootElement, prefix: null, values);
        return values;
    }

    private static string? FindUserSecretsId(string searchDirectory)
    {
        var root = Path.GetFullPath(searchDirectory);
        foreach (var projectFile in EnumerateProjectFiles(root))
        {
            try
            {
                var document = XDocument.Load(projectFile);
                var userSecretsId = document.Descendants()
                    .FirstOrDefault(static element => element.Name.LocalName == "UserSecretsId")
                    ?.Value
                    .Trim();
                if (!string.IsNullOrWhiteSpace(userSecretsId))
                {
                    return userSecretsId;
                }
            }
            catch
            {
                // User-secrets are optional. Ignore malformed or inaccessible project files.
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string searchDirectory)
    {
        var directory = new DirectoryInfo(searchDirectory);
        while (directory is not null)
        {
            foreach (var file in Directory.EnumerateFiles(directory.FullName, "*.csproj"))
            {
                yield return file;
            }

            var srcDirectory = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(srcDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(srcDirectory, "*.csproj", SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }

            directory = directory.Parent;
        }
    }

    private static void FlattenJson(JsonElement element, string? prefix, Dictionary<string, string> target)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            if (prefix is not null)
            {
                target[prefix] = element.ToString();
            }

            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            var key = prefix is null ? property.Name : $"{prefix}:{property.Name}";
            FlattenJson(property.Value, key, target);
        }
    }

    private static void CopyInto(IDictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                target[pair.Key] = pair.Value;
            }
        }
    }
}
