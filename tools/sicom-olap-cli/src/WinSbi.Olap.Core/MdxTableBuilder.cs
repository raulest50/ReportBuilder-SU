using System.Text;

namespace WinSbi.Olap.Core;

public static class MdxTableBuilder
{
    public static MdxTable BuildTable(MdxQueryResult result)
    {
        if (result.Rows.Count > 0)
        {
            var rowsetHeaders = result.Rows
                .SelectMany(static row => row.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static header => header, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rows = result.Rows
                .Select(row => rowsetHeaders.Select(header => RowValue.Get(row, header)).ToList())
                .Cast<IReadOnlyList<string>>()
                .ToList();
            return new MdxTable(rowsetHeaders, rows, null);
        }

        var axis0 = FindAxis(result, 0);
        var axis1 = FindAxis(result, 1);

        if (axis0 is null && axis1 is null)
        {
            return new MdxTable(["Value"], [[result.Scalar ?? ""]], null);
        }

        if (axis1 is null || axis1.Tuples.Count == 0)
        {
            var columns = axis0?.Tuples ?? [];
            if (columns.Count == 0)
            {
                return new MdxTable(["Value"], [[result.Scalar ?? ""]], null);
            }

            var rows = columns
                .Select((tuple, index) => new[]
                {
                    FormatTuple(tuple),
                    FindCellValue(result.Cells, index)
                })
                .Cast<IReadOnlyList<string>>()
                .ToList();
            return new MdxTable(["Column", "Value"], rows, BuildExtraAxisNote(result));
        }

        var columnTuples = axis0?.Tuples ?? [];
        var columnCount = Math.Max(1, columnTuples.Count);
        var rowMemberCount = Math.Max(1, axis1.Tuples.Max(static tuple => tuple.Members.Count));
        var matrixHeaders = BuildRowHeaders(axis1, rowMemberCount)
            .Concat(columnTuples.Count == 0 ? ["Value"] : columnTuples.Select(FormatTuple))
            .ToList();

        var tableRows = new List<IReadOnlyList<string>>();
        for (var rowIndex = 0; rowIndex < axis1.Tuples.Count; rowIndex++)
        {
            var tuple = axis1.Tuples[rowIndex];
            var cells = new List<string>();
            for (var memberIndex = 0; memberIndex < rowMemberCount; memberIndex++)
            {
                cells.Add(memberIndex < tuple.Members.Count ? FormatMember(tuple.Members[memberIndex]) : "");
            }

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                cells.Add(FindCellValue(result.Cells, columnIndex + rowIndex * columnCount));
            }

            tableRows.Add(cells);
        }

        return new MdxTable(matrixHeaders, tableRows, BuildExtraAxisNote(result));
    }

    public static string BuildTextTable(IEnumerable<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var tableRows = rows.ToList();
        var headerList = headers.ToList();
        var widths = headerList.Select(static header => header.Length).ToArray();

        foreach (var row in tableRows)
        {
            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] = Math.Max(widths[index], SafeCell(row, index).Length);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine(FormatRow(headerList, widths));
        builder.AppendLine(string.Join("  ", widths.Select(static width => new string('-', width))));
        foreach (var row in tableRows)
        {
            builder.AppendLine(FormatRow(row, widths));
        }

        return builder.ToString();
    }

    private static MdxAxis? FindAxis(MdxQueryResult result, int ordinal)
    {
        return result.Axes.FirstOrDefault(axis => axis.Ordinal == ordinal) ??
               result.Axes.FirstOrDefault(axis => string.Equals(axis.Name, $"Axis{ordinal}", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildRowHeaders(MdxAxis axis, int rowMemberCount)
    {
        var headers = new List<string>();
        for (var index = 0; index < rowMemberCount; index++)
        {
            var header = axis.Tuples
                .Select(tuple => index < tuple.Members.Count
                    ? FirstNonEmpty(tuple.Members[index].LevelName, tuple.Members[index].HierarchyName)
                    : "")
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            headers.Add(string.IsNullOrWhiteSpace(header) ? $"Row {index + 1}" : header);
        }

        return headers;
    }

    private static string BuildExtraAxisNote(MdxQueryResult result)
    {
        var extraAxes = result.Axes
            .Where(static axis => axis.Ordinal > 1)
            .Select(static axis => $"{axis.Name} ({axis.Tuples.Count} tuples)")
            .ToList();
        return extraAxes.Count == 0
            ? ""
            : $"Note: axes beyond ROWS/COLUMNS are present and are not expanded in table/csv output: {string.Join(", ", extraAxes)}. Use JSON to inspect the full parsed response.";
    }

    private static string FormatTuple(MdxTuple tuple)
    {
        return tuple.Members.Count == 0 ? "Value" : string.Join(" / ", tuple.Members.Select(FormatMember));
    }

    private static string FormatMember(MdxMember member)
    {
        return FirstNonEmpty(member.Caption, member.UniqueName, member.LevelName, member.HierarchyName);
    }

    private static string FindCellValue(IReadOnlyList<MdxCell> cells, int ordinal)
    {
        var cell = cells.FirstOrDefault(item => item.Ordinal == ordinal);
        return cell is null ? "" : FirstNonEmpty(cell.FormattedValue, cell.Value);
    }

    private static string FormatRow(IReadOnlyList<string> row, IReadOnlyList<int> widths)
    {
        return string.Join("  ", widths.Select((width, index) => SafeCell(row, index).PadRight(width)));
    }

    private static string SafeCell(IReadOnlyList<string> row, int index)
    {
        return index < row.Count ? row[index].ReplaceLineEndings(" ").Trim() : "";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}
