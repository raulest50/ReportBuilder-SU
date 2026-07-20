namespace WinSbi.Olap.Core;

public sealed class OlapMetadataService
{
    public async Task<IReadOnlyList<CatalogInfo>> ListCatalogsAsync(
        IXmlaClient client,
        string? prefix = null,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var result = await client.DiscoverAsync(
            "DBSCHEMA_CATALOGS",
            restrictions: new Dictionary<string, string>(),
            properties: new Dictionary<string, string>(),
            debugOptions,
            cancellationToken);

        return result.Rows
            .Select(static row => new CatalogInfo(
                RowValue.Get(row, "CATALOG_NAME"),
                RowValue.Get(row, "DESCRIPTION"),
                RowValue.Get(row, "DATE_MODIFIED")))
            .Where(static row => !string.IsNullOrWhiteSpace(row.Name))
            .Where(row => prefix is null || row.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<CubeInfo>> ListCubesAsync(
        IXmlaClient client,
        CatalogInfo catalog,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await DiscoverRowsAsync(
            client,
            "MDSCHEMA_CUBES",
            new Dictionary<string, string> { ["CATALOG_NAME"] = catalog.Name },
            CatalogProperties(catalog.Name),
            debugOptions,
            cancellationToken);

        return rows
            .Select(static row => new CubeInfo(
                RowValue.Get(row, "CUBE_NAME"),
                RowValue.Get(row, "CUBE_TYPE"),
                RowValue.Get(row, "LAST_SCHEMA_UPDATE"),
                RowValue.Get(row, "LAST_DATA_UPDATE")))
            .Where(static row => !string.IsNullOrWhiteSpace(row.Name))
            .OrderBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CubeProfile> GetCubeProfileAsync(
        IXmlaClient client,
        CatalogInfo catalog,
        CubeInfo cube,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var catalogProperties = CatalogProperties(catalog.Name);
        var cubeRestrictions = new Dictionary<string, string>
        {
            ["CATALOG_NAME"] = catalog.Name,
            ["CUBE_NAME"] = cube.Name
        };

        var dimensions = MapDimensions(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_DIMENSIONS", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_DIMENSIONS"), cancellationToken));
        var hierarchies = MapHierarchies(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_HIERARCHIES", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_HIERARCHIES"), cancellationToken));
        var levels = MapLevels(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_LEVELS", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_LEVELS"), cancellationToken));
        var measures = MapMeasures(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_MEASURES", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_MEASURES"), cancellationToken));
        var kpis = MapKpis(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_KPIS", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_KPIS"), cancellationToken));
        var measureGroups = MapMeasureGroups(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_MEASUREGROUPS", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_MEASUREGROUPS"), cancellationToken));
        var measureGroupDimensions = MapMeasureGroupDimensions(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_MEASUREGROUP_DIMENSIONS", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_MEASUREGROUP_DIMENSIONS"), cancellationToken));
        var properties = MapProperties(await DiscoverRowsOrEmptyAsync(client, "MDSCHEMA_PROPERTIES", cubeRestrictions, catalogProperties, SuffixDebug(debugOptions, "MDSCHEMA_PROPERTIES"), cancellationToken));
        var fields = BuildFields(measures, levels, properties);

        return new CubeProfile(
            catalog,
            cube,
            dimensions,
            hierarchies,
            levels,
            measures,
            kpis,
            measureGroups,
            measureGroupDimensions,
            properties,
            fields);
    }

    public static Dictionary<string, string> CatalogProperties(string catalogName)
    {
        return new Dictionary<string, string> { ["Catalog"] = catalogName };
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> DiscoverRowsAsync(
        IXmlaClient client,
        string requestType,
        IReadOnlyDictionary<string, string> restrictions,
        IReadOnlyDictionary<string, string> properties,
        XmlaDebugOptions? debugOptions,
        CancellationToken cancellationToken)
    {
        return (await client.DiscoverAsync(requestType, restrictions, properties, debugOptions, cancellationToken)).Rows;
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> DiscoverRowsOrEmptyAsync(
        IXmlaClient client,
        string requestType,
        IReadOnlyDictionary<string, string> restrictions,
        IReadOnlyDictionary<string, string> properties,
        XmlaDebugOptions? debugOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DiscoverRowsAsync(client, requestType, restrictions, properties, debugOptions, cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<DimensionInfo> MapDimensions(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new DimensionInfo(
                RowValue.Get(row, "DIMENSION_NAME"),
                RowValue.Get(row, "DIMENSION_UNIQUE_NAME"),
                RowValue.Get(row, "DIMENSION_CAPTION"),
                RowValue.Get(row, "DIMENSION_TYPE"),
                RowValue.Get(row, "DEFAULT_HIERARCHY"),
                RowValue.Copy(row)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.UniqueName) || !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(static item => FirstNonEmpty(item.Caption, item.Name, item.UniqueName), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<HierarchyInfo> MapHierarchies(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new HierarchyInfo(
                RowValue.Get(row, "DIMENSION_UNIQUE_NAME"),
                RowValue.Get(row, "HIERARCHY_NAME"),
                RowValue.Get(row, "HIERARCHY_UNIQUE_NAME"),
                RowValue.Get(row, "HIERARCHY_CAPTION"),
                RowValue.Get(row, "HIERARCHY_ORIGIN"),
                RowValue.Copy(row)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.UniqueName) || !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(static item => item.DimensionUniqueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => FirstNonEmpty(item.Caption, item.Name, item.UniqueName), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LevelInfo> MapLevels(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new LevelInfo(
                RowValue.Get(row, "DIMENSION_UNIQUE_NAME"),
                RowValue.Get(row, "HIERARCHY_UNIQUE_NAME"),
                RowValue.Get(row, "LEVEL_NAME"),
                RowValue.Get(row, "LEVEL_UNIQUE_NAME"),
                RowValue.Get(row, "LEVEL_NUMBER"),
                RowValue.Get(row, "LEVEL_TYPE"),
                RowValue.Get(row, "LEVEL_CARDINALITY"),
                FirstNonEmpty(RowValue.Get(row, "LEVEL_DBTYPE"), RowValue.Get(row, "DATA_TYPE")),
                RowValue.Copy(row)))
            .Where(static item => !string.Equals(item.Name, "(All)", StringComparison.OrdinalIgnoreCase))
            .Where(static item => !string.IsNullOrWhiteSpace(item.UniqueName) || !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(static item => item.DimensionUniqueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.HierarchyUniqueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => TryParseInt(item.Number) ?? int.MaxValue)
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<MeasureInfo> MapMeasures(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new MeasureInfo(
                RowValue.Get(row, "MEASUREGROUP_NAME"),
                RowValue.Get(row, "MEASURE_NAME"),
                RowValue.Get(row, "MEASURE_UNIQUE_NAME"),
                RowValue.Get(row, "MEASURE_CAPTION"),
                RowValue.Get(row, "DATA_TYPE"),
                RowValue.Get(row, "MEASURE_AGGREGATOR"),
                RowValue.Get(row, "DEFAULT_FORMAT_STRING"),
                RowValue.Copy(row)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.UniqueName) || !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(static item => item.MeasureGroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => FirstNonEmpty(item.Caption, item.Name, item.UniqueName), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<KpiInfo> MapKpis(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new KpiInfo(
                RowValue.Get(row, "KPI_NAME"),
                RowValue.Get(row, "KPI_CAPTION"),
                RowValue.Copy(row)))
            .OrderBy(static item => FirstNonEmpty(item.Caption, item.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<MeasureGroupInfo> MapMeasureGroups(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new MeasureGroupInfo(
                RowValue.Get(row, "MEASUREGROUP_NAME"),
                RowValue.Get(row, "MEASUREGROUP_CAPTION"),
                FirstNonEmpty(RowValue.Get(row, "MEASURE_GROUP_CARDINALITY"), RowValue.Get(row, "MEASUREGROUP_CARDINALITY")),
                RowValue.Copy(row)))
            .OrderBy(static item => FirstNonEmpty(item.Caption, item.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<MeasureGroupDimensionInfo> MapMeasureGroupDimensions(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new MeasureGroupDimensionInfo(
                RowValue.Get(row, "MEASUREGROUP_NAME"),
                RowValue.Get(row, "DIMENSION_UNIQUE_NAME"),
                RowValue.Get(row, "DIMENSION_CARDINALITY"),
                RowValue.Copy(row)))
            .OrderBy(static item => item.MeasureGroupName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.DimensionUniqueName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<MemberPropertyInfo> MapProperties(IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        return rows
            .Select(static row => new MemberPropertyInfo(
                RowValue.Get(row, "DIMENSION_UNIQUE_NAME"),
                RowValue.Get(row, "HIERARCHY_UNIQUE_NAME"),
                RowValue.Get(row, "LEVEL_UNIQUE_NAME"),
                RowValue.Get(row, "PROPERTY_NAME"),
                RowValue.Get(row, "PROPERTY_CAPTION"),
                RowValue.Get(row, "PROPERTY_TYPE"),
                FirstNonEmpty(RowValue.Get(row, "PROPERTY_CONTENT_TYPE"), RowValue.Get(row, "DATA_TYPE"), RowValue.Get(row, "PROPERTY_DBTYPE")),
                RowValue.Copy(row)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Caption))
            .OrderBy(static item => item.DimensionUniqueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.LevelUniqueName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => FirstNonEmpty(item.Caption, item.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<FieldInfo> BuildFields(
        IReadOnlyList<MeasureInfo> measures,
        IReadOnlyList<LevelInfo> levels,
        IReadOnlyList<MemberPropertyInfo> properties)
    {
        var fields = new List<FieldInfo>();

        fields.AddRange(measures.Select(static item => new FieldInfo(
            "Measure",
            FirstNonEmpty(item.Caption, item.Name, item.UniqueName),
            item.UniqueName,
            item.DataType,
            item.MeasureGroupName,
            "",
            JoinNonEmpty(item.Aggregator, item.FormatString))));

        fields.AddRange(levels.Select(static item => new FieldInfo(
            "Level",
            item.Name,
            item.UniqueName,
            item.DataType,
            FirstNonEmpty(item.HierarchyUniqueName, item.DimensionUniqueName),
            item.Cardinality,
            item.Type)));

        fields.AddRange(properties.Select(static item => new FieldInfo(
            "Property",
            FirstNonEmpty(item.Caption, item.Name),
            item.Name,
            item.DataType,
            FirstNonEmpty(item.LevelUniqueName, item.HierarchyUniqueName, item.DimensionUniqueName),
            "",
            item.Type)));

        return fields
            .OrderBy(static item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Parent, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static XmlaDebugOptions? SuffixDebug(XmlaDebugOptions? debugOptions, string suffix)
    {
        if (debugOptions is null)
        {
            return null;
        }

        return new XmlaDebugOptions(
            SuffixPath(debugOptions.RequestOutputPath, suffix),
            SuffixPath(debugOptions.ResponseOutputPath, suffix));
    }

    private static string? SuffixPath(string? path, string suffix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory ?? "", $"{fileName}-{suffix}{extension}");
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

    private static string JoinNonEmpty(params string?[] values)
    {
        return string.Join(" / ", values.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static int? TryParseInt(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
