using System.Text;

namespace WinSbi.Olap.Core;

public static class EnvFileParser
{
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(content);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            ParseLine(line, values);
        }

        return values;
    }

    private static void ParseLine(string line, Dictionary<string, string> values)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return;
        }

        if (trimmed.StartsWith("export ", StringComparison.Ordinal))
        {
            trimmed = trimmed["export ".Length..].TrimStart();
        }

        var equalsIndex = FindUnquoted(trimmed, '=');
        if (equalsIndex <= 0)
        {
            return;
        }

        var key = trimmed[..equalsIndex].Trim();
        if (!IsValidKey(key))
        {
            return;
        }

        var rawValue = trimmed[(equalsIndex + 1)..].Trim();
        values[key] = ParseValue(rawValue);
    }

    private static string ParseValue(string rawValue)
    {
        if (rawValue.Length == 0)
        {
            return "";
        }

        if (rawValue[0] == '"')
        {
            var quoted = ReadQuoted(rawValue, '"');
            return UnescapeDoubleQuoted(quoted);
        }

        if (rawValue[0] == '\'')
        {
            return ReadQuoted(rawValue, '\'');
        }

        return StripInlineComment(rawValue).Trim();
    }

    private static string ReadQuoted(string value, char quote)
    {
        var builder = new StringBuilder();
        var escaped = false;
        for (var index = 1; index < value.Length; index++)
        {
            var current = value[index];
            if (escaped)
            {
                builder.Append('\\');
                builder.Append(current);
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current == quote)
            {
                return builder.ToString();
            }

            builder.Append(current);
        }

        if (escaped)
        {
            builder.Append('\\');
        }

        return builder.ToString();
    }

    private static string UnescapeDoubleQuoted(string value)
    {
        var builder = new StringBuilder(value.Length);
        var escaped = false;
        foreach (var current in value)
        {
            if (!escaped)
            {
                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                builder.Append(current);
                continue;
            }

            builder.Append(current switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                _ => current
            });
            escaped = false;
        }

        if (escaped)
        {
            builder.Append('\\');
        }

        return builder.ToString();
    }

    private static string StripInlineComment(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '#' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index];
            }
        }

        return value;
    }

    private static int FindUnquoted(string value, char target)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
                continue;
            }

            if (current == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsValidKey(string key)
    {
        if (key.Length == 0 || !(char.IsLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }

        return key.All(static c => char.IsLetterOrDigit(c) || c == '_');
    }
}
