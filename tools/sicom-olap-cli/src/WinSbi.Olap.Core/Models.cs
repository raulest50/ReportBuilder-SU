using System.Collections.ObjectModel;

namespace WinSbi.Olap.Core;

public sealed record SourceDefinition(string Name, string Url);

public sealed record SourceConnection(
    SourceDefinition Source,
    string User,
    string Password,
    string AuthMode,
    int TimeoutSeconds);

public sealed record LoadedSecrets(
    IReadOnlyDictionary<string, string> UserSecrets,
    IReadOnlyDictionary<string, string> EnvFile,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, string> Values,
    string? EnvFilePath);

public sealed record CatalogInfo(string Name, string Description, string DateModified);

public sealed record CubeInfo(string Name, string Type, string LastSchemaUpdate, string LastDataUpdate);

public sealed record CubeProfile(
    CatalogInfo Catalog,
    CubeInfo Cube,
    IReadOnlyList<DimensionInfo> Dimensions,
    IReadOnlyList<HierarchyInfo> Hierarchies,
    IReadOnlyList<LevelInfo> Levels,
    IReadOnlyList<MeasureInfo> Measures,
    IReadOnlyList<KpiInfo> Kpis,
    IReadOnlyList<MeasureGroupInfo> MeasureGroups,
    IReadOnlyList<MeasureGroupDimensionInfo> MeasureGroupDimensions,
    IReadOnlyList<MemberPropertyInfo> Properties,
    IReadOnlyList<FieldInfo> Fields);

public sealed record DimensionInfo(
    string Name,
    string UniqueName,
    string Caption,
    string Type,
    string DefaultHierarchy,
    IReadOnlyDictionary<string, string> Raw);

public sealed record HierarchyInfo(
    string DimensionUniqueName,
    string Name,
    string UniqueName,
    string Caption,
    string Origin,
    IReadOnlyDictionary<string, string> Raw);

public sealed record LevelInfo(
    string DimensionUniqueName,
    string HierarchyUniqueName,
    string Name,
    string UniqueName,
    string Number,
    string Type,
    string Cardinality,
    string DataType,
    IReadOnlyDictionary<string, string> Raw);

public sealed record MeasureInfo(
    string MeasureGroupName,
    string Name,
    string UniqueName,
    string Caption,
    string DataType,
    string Aggregator,
    string FormatString,
    IReadOnlyDictionary<string, string> Raw);

public sealed record KpiInfo(string Name, string Caption, IReadOnlyDictionary<string, string> Raw);

public sealed record MeasureGroupInfo(string Name, string Caption, string Cardinality, IReadOnlyDictionary<string, string> Raw);

public sealed record MeasureGroupDimensionInfo(
    string MeasureGroupName,
    string DimensionUniqueName,
    string Cardinality,
    IReadOnlyDictionary<string, string> Raw);

public sealed record MemberPropertyInfo(
    string DimensionUniqueName,
    string HierarchyUniqueName,
    string LevelUniqueName,
    string Name,
    string Caption,
    string Type,
    string DataType,
    IReadOnlyDictionary<string, string> Raw);

public sealed record FieldInfo(
    string Kind,
    string Name,
    string UniqueName,
    string DataType,
    string Parent,
    string Cardinality,
    string Details);

public sealed record DiscoverResult(IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed record MdxQueryResult(
    string? Scalar,
    IReadOnlyList<MdxMember> Axis1Members,
    IReadOnlyList<MdxAxis> Axes,
    IReadOnlyList<MdxCell> Cells,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed record MdxAxis(string Name, int Ordinal, IReadOnlyList<MdxTuple> Tuples);

public sealed record MdxTuple(IReadOnlyList<MdxMember> Members);

public sealed record MdxMember(string Caption, string UniqueName, string LevelName, string HierarchyName);

public sealed record MdxCell(int Ordinal, string Value, string FormattedValue);

public sealed record MdxTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, string? Note);

public sealed record EndpointProbeResult(
    string Url,
    int StatusCode,
    string ReasonPhrase,
    string WwwAuthenticate,
    string Server,
    bool IsHttpSuccess);

public sealed record DiagnoseResult(
    SourceDefinition Source,
    string AuthMode,
    EndpointProbeResult? Probe,
    bool DiscoverSucceeded,
    int CatalogCount,
    string? Error);

public sealed record XmlaDebugOptions(string? RequestOutputPath, string? ResponseOutputPath);

public enum MayoristasMeasureSelection
{
    Accepted,
    Dispatched,
    Both
}

public enum MayoristasProductSelection
{
    All,
    Corriente,
    Extra,
    Diesel
}

public enum EdsBuyerScope
{
    Eds,
    EdsFluvial,
    EdsFluvialIndustrial
}

public sealed record MayoristasReportOptions(
    int Year,
    IReadOnlyList<int> Months,
    string PeriodLabel,
    string PeriodCommandPart,
    MayoristasMeasureSelection Measure,
    MayoristasProductSelection Product);

public sealed record MayoristasReportResult(
    MayoristasReportOptions Options,
    string OutputDirectory,
    string DetailCsvPath,
    string ProviderSummaryCsvPath,
    string? ProductSummaryCsvPath,
    MdxTable DetailTable,
    MdxTable ProviderSummaryTable,
    MdxTable? ProductSummaryTable,
    string EquivalentCommand);

public sealed record MayoristasHistoricoYearPart(int Year, IReadOnlyList<int> Months);

public sealed record MayoristasHistoricoReportOptions(
    int StartYear,
    int StartMonth,
    int EndYear,
    int EndMonth,
    IReadOnlyList<MayoristasHistoricoYearPart> YearParts,
    string PeriodLabel,
    MayoristasMeasureSelection Measure,
    MayoristasProductSelection Product,
    EdsBuyerScope BuyerScope,
    bool Resume);

public sealed record MayoristasHistoricoPartManifest(
    int Year,
    IReadOnlyList<int> Months,
    string Path,
    int RowCount);

public sealed record MayoristasHistoricoManifest(
    int SchemaVersion,
    string Report,
    string Status,
    string Catalog,
    string Cube,
    string Measure,
    string Product,
    string BuyerScope,
    string ProviderType,
    string StartPeriod,
    string EndPeriod,
    string GeneratedAtUtc,
    string EquivalentCommand,
    IReadOnlyList<MayoristasHistoricoPartManifest> Parts,
    int DetailRowCount,
    int MonthlyRowCount);

public sealed record MayoristasHistoricoReportResult(
    MayoristasHistoricoReportOptions Options,
    string OutputDirectory,
    string DetailCsvPath,
    string MonthlyCsvPath,
    string ManifestJsonPath,
    IReadOnlyList<string> PartCsvPaths,
    MdxTable DetailTable,
    MdxTable MonthlyTable,
    string EquivalentCommand);

public sealed record EdsMunicipiosReportOptions(
    int Year,
    IReadOnlyList<int> Months,
    string PeriodLabel,
    string PeriodCommandPart,
    MayoristasProductSelection Product);

public sealed record EdsMunicipiosReportResult(
    EdsMunicipiosReportOptions Options,
    string OutputDirectory,
    string DetailCsvPath,
    string AverageCsvPath,
    string ActiveEdsCsvPath,
    MdxTable DetailTable,
    MdxTable AverageTable,
    MdxTable ActiveEdsTable,
    string EquivalentCommand);

public sealed record EdsInsightsReportOptions(
    int Year,
    IReadOnlyList<int> Months,
    string PeriodLabel,
    string PeriodCommandPart,
    MayoristasProductSelection Product,
    int TopCities,
    int TopEds);

public sealed record EdsInsightsReportResult(
    EdsInsightsReportOptions Options,
    string OutputDirectory,
    string NationalFlagsCsvPath,
    string TopCitiesCsvPath,
    string TopCityFlagsCsvPath,
    string TopEdsCsvPath,
    string BorderSummaryCsvPath,
    string BorderProductsCsvPath,
    MdxTable NationalFlagsTable,
    MdxTable TopCitiesTable,
    MdxTable TopCityFlagsTable,
    MdxTable TopEdsTable,
    MdxTable BorderSummaryTable,
    MdxTable BorderProductsTable,
    string EquivalentCommand);

public sealed record EdsTopReportOptions(
    int Year,
    IReadOnlyList<int> Months,
    string PeriodLabel,
    string PeriodCommandPart,
    MayoristasMeasureSelection Measure,
    MayoristasProductSelection Product,
    int Top);

public sealed record EdsTopReportResult(
    EdsTopReportOptions Options,
    string OutputDirectory,
    string CsvPath,
    string ManifestPath,
    MdxTable Table,
    string EquivalentCommand);

public sealed record EdsPercentilesWindowMonth(int Year, int Month);

public sealed record EdsPercentilesReportOptions(
    int EndYear,
    int EndMonth,
    IReadOnlyList<EdsPercentilesWindowMonth> WindowMonths,
    string PeriodLabel,
    MayoristasProductSelection Product,
    EdsBuyerScope BuyerScope);

public sealed record EdsPercentilesReportResult(
    EdsPercentilesReportOptions Options,
    string OutputDirectory,
    string BaseCsvPath,
    string StationsCsvPath,
    string SummaryCsvPath,
    MdxTable BaseTable,
    MdxTable StationsTable,
    MdxTable SummaryTable,
    string EquivalentCommand);

public interface IXmlaClient
{
    Task<DiscoverResult> DiscoverAsync(
        string requestType,
        IReadOnlyDictionary<string, string> restrictions,
        IReadOnlyDictionary<string, string> properties,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default);

    Task<MdxQueryResult> ExecuteMdxAsync(
        string catalogName,
        string statement,
        string axisFormat = "ClusterFormat",
        string? content = null,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default);
}

public static class RowValue
{
    public static string Get(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value.Trim() : "";
    }

    public static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string> row)
    {
        return new ReadOnlyDictionary<string, string>(
            row.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase));
    }
}
