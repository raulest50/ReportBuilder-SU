using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class EnvAndSecretsTests
{
    [Fact]
    public void EnvParserHandlesCommentsQuotesAndWhitespace()
    {
        var values = EnvFileParser.Parse(
            """
            # comment
            USER_CUBO_SICOM = "alice@example.com"
            PASSWORD_CUBO_SICOM='pass # still value'
            CUBO_LIQS_URL=https://bi.example/OLAP/msmdpump.dll # inline comment
            export CUBO_AGENTS_URL = "https://agents.example/OLAP/msmdpump.dll"
            """);

        Assert.Equal("alice@example.com", values["USER_CUBO_SICOM"]);
        Assert.Equal("pass # still value", values["PASSWORD_CUBO_SICOM"]);
        Assert.Equal("https://bi.example/OLAP/msmdpump.dll", values["CUBO_LIQS_URL"]);
        Assert.Equal("https://agents.example/OLAP/msmdpump.dll", values["CUBO_AGENTS_URL"]);
    }

    [Fact]
    public void SecretMergeUsesEnvironmentOverEnvFileOverUserSecrets()
    {
        var userSecrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USER_CUBO_SICOM"] = "user-secret",
            ["PASSWORD_CUBO_SICOM"] = "password-secret",
            ["CUBO_LIQS_URL"] = "https://secret"
        };
        var envFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PASSWORD_CUBO_SICOM"] = "password-env-file",
            ["CUBO_LIQS_URL"] = "https://env-file"
        };
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CUBO_LIQS_URL"] = "https://environment"
        };

        var merged = SecretStore.Merge(userSecrets, envFile, environment);

        Assert.Equal("user-secret", merged["USER_CUBO_SICOM"]);
        Assert.Equal("password-env-file", merged["PASSWORD_CUBO_SICOM"]);
        Assert.Equal("https://environment", merged["CUBO_LIQS_URL"]);
    }
}
