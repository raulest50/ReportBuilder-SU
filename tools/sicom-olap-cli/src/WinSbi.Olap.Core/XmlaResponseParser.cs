using System.Xml.Linq;

namespace WinSbi.Olap.Core;

public static class XmlaResponseParser
{
    public static DiscoverResult ParseDiscoverResponse(string xml)
    {
        var document = XDocument.Parse(xml);
        ThrowIfFault(document);
        return new DiscoverResult(ParseRowsetRows(document));
    }

    public static MdxQueryResult ParseMdxResponse(string xml)
    {
        var document = XDocument.Parse(xml);
        ThrowIfFault(document);

        var cellError = document.Descendants().FirstOrDefault(static element => element.Name.LocalName == "Error");
        if (cellError is not null)
        {
            var errorText = cellError.Descendants()
                .FirstOrDefault(static element => element.Name.LocalName is "Description" or "ErrorDescription")
                ?.Value;
            throw new InvalidOperationException($"XMLA cell error: {FirstNonEmpty(errorText, cellError.Value)}");
        }

        var axes = document.Descendants()
            .Where(static element => element.Name.LocalName == "Axis")
            .Select(ParseMdxAxis)
            .OrderBy(static axis => axis.Ordinal)
            .ThenBy(static axis => axis.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cells = document.Descendants()
            .Where(static element => element.Name.LocalName == "Cell")
            .Select(static (cell, index) => new MdxCell(
                ParseCellOrdinal(cell, index),
                cell.Elements().FirstOrDefault(static element => element.Name.LocalName == "Value")?.Value ?? "",
                cell.Elements().FirstOrDefault(static element => element.Name.LocalName is "FmtValue" or "FormattedValue")?.Value ?? ""))
            .OrderBy(static cell => cell.Ordinal)
            .ToList();

        var scalar = cells
            .Select(static cell => FirstNonEmpty(cell.Value, cell.FormattedValue))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

        var axis1Members = axes
            .FirstOrDefault(static axis => axis.Ordinal == 1 || string.Equals(axis.Name, "Axis1", StringComparison.OrdinalIgnoreCase))
            ?.Tuples
            .SelectMany(static tuple => tuple.Members)
            .ToList() ?? [];

        var rows = axes.Count == 0 && cells.Count == 0 ? ParseRowsetRows(document) : [];
        return new MdxQueryResult(scalar, axis1Members, axes, cells, rows);
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseRowsetRows(XDocument document)
    {
        return document.Descendants()
            .Where(static element => element.Name.LocalName == "row")
            .Select(static row => row.Elements()
                .GroupBy(static element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Last().Value,
                    StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlyDictionary<string, string>>()
            .ToList();
    }

    private static MdxAxis ParseMdxAxis(XElement axis)
    {
        var name = axis.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "name")?.Value ?? "";
        var tuples = axis.Descendants()
            .Where(static element => element.Name.LocalName == "Tuple")
            .Select(static tuple => new MdxTuple(tuple.Elements()
                .Where(static element => element.Name.LocalName == "Member")
                .Select(static member => ParseMdxMember(member, null))
                .ToList()))
            .ToList();

        if (tuples.Count == 0)
        {
            tuples.AddRange(ParseClusterFormatTuples(axis));
        }

        return new MdxAxis(name, ParseAxisOrdinal(name), tuples);
    }

    private static IReadOnlyList<MdxTuple> ParseClusterFormatTuples(XElement axis)
    {
        var crossProducts = axis.Elements()
            .Where(static element => element.Name.LocalName == "CrossProduct")
            .ToList();

        if (crossProducts.Count == 0)
        {
            crossProducts = axis.Descendants()
                .Where(static element => element.Name.LocalName == "CrossProduct")
                .ToList();
        }

        var tuples = new List<MdxTuple>();
        foreach (var crossProduct in crossProducts)
        {
            var memberGroups = crossProduct.Elements()
                .Where(static element => element.Name.LocalName == "Members")
                .Select(static group =>
                {
                    var hierarchy = group.Attributes()
                        .FirstOrDefault(static attribute => attribute.Name.LocalName == "Hierarchy")
                        ?.Value;
                    return group.Elements()
                        .Where(static element => element.Name.LocalName == "Member")
                        .Select(member => ParseMdxMember(member, hierarchy))
                        .ToList();
                })
                .Where(static group => group.Count > 0)
                .ToList();

            foreach (var members in ExpandMemberGroups(memberGroups))
            {
                tuples.Add(new MdxTuple(members));
            }
        }

        if (tuples.Count == 0)
        {
            var members = axis.Elements()
                .Where(static element => element.Name.LocalName == "Member")
                .Select(static member => ParseMdxMember(member, null))
                .ToList();
            tuples.AddRange(members.Select(static member => new MdxTuple([member])));
        }

        return tuples;
    }

    private static IReadOnlyList<IReadOnlyList<MdxMember>> ExpandMemberGroups(IReadOnlyList<IReadOnlyList<MdxMember>> memberGroups)
    {
        if (memberGroups.Count == 0)
        {
            return [];
        }

        var tuples = new List<IReadOnlyList<MdxMember>> { Array.Empty<MdxMember>() };
        foreach (var group in memberGroups)
        {
            var expanded = new List<IReadOnlyList<MdxMember>>();
            foreach (var prefix in tuples)
            {
                foreach (var member in group)
                {
                    expanded.Add(prefix.Concat([member]).ToList());
                }
            }

            tuples = expanded;
        }

        return tuples;
    }

    private static MdxMember ParseMdxMember(XElement member, string? hierarchyFallback)
    {
        return new MdxMember(
            member.Elements().FirstOrDefault(static element => element.Name.LocalName == "Caption")?.Value ?? "",
            member.Elements().FirstOrDefault(static element => element.Name.LocalName == "UName")?.Value ?? "",
            member.Elements().FirstOrDefault(static element => element.Name.LocalName == "LName")?.Value ?? "",
            member.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "Hierarchy")?.Value ??
            hierarchyFallback ??
            member.Parent?.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "Hierarchy")?.Value ??
            "");
    }

    private static void ThrowIfFault(XDocument document)
    {
        var fault = document.Descendants().FirstOrDefault(static element => element.Name.LocalName == "Fault");
        if (fault is null)
        {
            return;
        }

        var faultText = fault.Descendants()
            .FirstOrDefault(static element => element.Name.LocalName is "faultstring" or "Description" or "Text")
            ?.Value;

        throw new InvalidOperationException($"XMLA SOAP fault: {FirstNonEmpty(faultText, fault.Value)}");
    }

    private static int ParseAxisOrdinal(string axisName)
    {
        const string prefix = "Axis";
        return axisName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(axisName[prefix.Length..], out var ordinal)
            ? ordinal
            : int.MaxValue;
    }

    private static int ParseCellOrdinal(XElement cell, int fallbackOrdinal)
    {
        return int.TryParse(
            cell.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "CellOrdinal")?.Value,
            out var ordinal)
            ? ordinal
            : fallbackOrdinal;
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
