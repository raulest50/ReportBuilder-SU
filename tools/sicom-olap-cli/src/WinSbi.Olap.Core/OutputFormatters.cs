using System.Text;
using System.Text.Json;

namespace WinSbi.Olap.Core;

public static class OutputFormatters
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    public static string ToCsv(MdxTable table)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", table.Headers.Select(EscapeCsv)));
        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(",", table.Headers.Select((_, index) => EscapeCsv(index < row.Count ? row[index] : ""))));
        }

        return builder.ToString();
    }

    public static string ToText(MdxTable table)
    {
        var builder = new StringBuilder();
        builder.Append(MdxTableBuilder.BuildTextTable(table.Headers, table.Rows));
        if (!string.IsNullOrWhiteSpace(table.Note))
        {
            builder.AppendLine();
            builder.AppendLine(table.Note);
        }

        return builder.ToString();
    }

    public static MdxTable TableFromCatalogs(IEnumerable<CatalogInfo> catalogs)
    {
        return new MdxTable(
            ["Name", "DateModified", "Description"],
            catalogs.Select(static catalog => new[] { catalog.Name, catalog.DateModified, catalog.Description })
                .Cast<IReadOnlyList<string>>()
                .ToList(),
            null);
    }

    public static MdxTable TableFromCubes(IEnumerable<CubeInfo> cubes)
    {
        return new MdxTable(
            ["Name", "Type", "LastSchemaUpdate", "LastDataUpdate"],
            cubes.Select(static cube => new[] { cube.Name, cube.Type, cube.LastSchemaUpdate, cube.LastDataUpdate })
                .Cast<IReadOnlyList<string>>()
                .ToList(),
            null);
    }

    public static MdxTable TableFromFields(IEnumerable<FieldInfo> fields)
    {
        return new MdxTable(
            ["Kind", "Name", "UniqueName", "DataType", "Parent", "Cardinality", "Details"],
            fields.Select(static field => new[]
                {
                    field.Kind,
                    field.Name,
                    field.UniqueName,
                    field.DataType,
                    field.Parent,
                    field.Cardinality,
                    field.Details
                })
                .Cast<IReadOnlyList<string>>()
                .ToList(),
            null);
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
