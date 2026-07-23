using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WinSbi.Olap.Core;

public static class EdsTopReport
{
    public const string CatalogName = MayoristasReport.CatalogName;
    public const string CubeName = MayoristasReport.CubeName;
    public const int ManifestSchemaVersion = 1;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    public static EdsTopReportOptions CreateOptions(
        int year,
        int? quarter,
        int? semester,
        bool annual,
        string? months,
        string measure,
        string product,
        int top)
    {
        var period = ReportPeriods.Resolve(year, quarter, semester, annual, months);
        var parsedMeasure = MayoristasReport.ParseMeasure(measure);
        if (parsedMeasure == MayoristasMeasureSelection.Both)
        {
            throw new ArgumentException("--measure for report eds_top must be accepted or dispatched.");
        }

        if (top < 1)
        {
            throw new ArgumentException("--top must be greater than zero.");
        }

        return new EdsTopReportOptions(
            period.Year,
            period.Months,
            period.Label,
            period.CommandPart,
            parsedMeasure,
            MayoristasReport.ParseProduct(product),
            top);
    }

    public static EdsTopReportOptions CreateOptions(
        int year,
        int? quarter,
        int? semester,
        string? months,
        string measure,
        string product,
        int top)
    {
        return CreateOptions(year, quarter, semester, annual: false, months, measure, product, top);
    }

    public static string DefaultOutputDirectory(EdsTopReportOptions options)
    {
        return Path.Combine("reports", CatalogName, "eds_top", options.PeriodLabel);
    }

    public static string BuildTopMdx(EdsTopReportOptions options)
    {
        var sharedOptions = new EdsInsightsReportOptions(
            options.Year,
            options.Months,
            options.PeriodLabel,
            options.PeriodCommandPart,
            options.Product,
            TopCities: 1,
            TopEds: options.Top);
        return EdsInsightsReport.BuildTopEdsMdx(sharedOptions, options.Measure);
    }

    public static string BuildNationalMdx(EdsTopReportOptions options)
    {
        var monthMembers = MonthMembers(options);
        var productMembers = ProductMembers(options.Product);
        var sourceMeasure = MeasureUniqueName(options.Measure);

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_products] AS {
{{productMembers}}
}
MEMBER [Measures].[VOLUMEN RANKING PERIODO] AS
    SUM([target_months] * [target_products], {{sourceMeasure}})
SET [rows] AS
    NONEMPTY(
        [COMPRADOR].[ZONA FRONTERA].[ZONA FRONTERA].Members,
        [Measures].[VOLUMEN RANKING PERIODO]
    )
SELECT
    { [Measures].[VOLUMEN RANKING PERIODO] } ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
WHERE (
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}],
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]
)
""";
    }

    public static async Task<EdsTopReportResult> GenerateAsync(
        IXmlaClient client,
        EdsTopReportOptions options,
        string outputDirectory,
        string equivalentCommand,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var topResult = await client.ExecuteMdxAsync(
            CatalogName,
            BuildTopMdx(options),
            debugOptions: AddDebugSuffix(debugOptions, "-eds-top"),
            cancellationToken: cancellationToken);
        var topRows = NormalizeTopRows(MdxTableBuilder.BuildTable(topResult), options);
        if (topRows.Count != options.Top)
        {
            throw new InvalidDataException(
                $"The cube returned {topRows.Count} valid EDS rows; {options.Top} were required.");
        }

        var nationalResult = await client.ExecuteMdxAsync(
            CatalogName,
            BuildNationalMdx(options),
            debugOptions: AddDebugSuffix(debugOptions, "-eds-top-national"),
            cancellationToken: cancellationToken);
        var nationalVolume = NormalizeNationalVolume(MdxTableBuilder.BuildTable(nationalResult));

        var table = BuildOutputTable(topRows, nationalVolume, options);

        Directory.CreateDirectory(outputDirectory);
        var csvPath = Path.Combine(outputDirectory, "eds-top-volumen.csv");
        var manifestPath = Path.Combine(outputDirectory, "eds-top-manifest.json");
        var generatedAt = DateTimeOffset.UtcNow;
        var manifest = new EdsTopManifest(
            ManifestSchemaVersion,
            "eds_top",
            "complete",
            CatalogName,
            CubeName,
            options.Year,
            options.Months,
            options.PeriodLabel,
            MeasureArgument(options.Measure),
            ProductArgument(options.Product),
            ProductUniqueNames(options.Product),
            "ESTACION DE SERVICIO AUTOMOTRIZ",
            options.Top,
            table.Rows.Count,
            nationalVolume,
            equivalentCommand,
            generatedAt);

        await WriteTextAtomicAsync(csvPath, OutputFormatters.ToCsv(table), cancellationToken);
        await WriteTextAtomicAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions) + Environment.NewLine,
            cancellationToken);

        return new EdsTopReportResult(options, outputDirectory, csvPath, manifestPath, table, equivalentCommand);
    }

    public static IReadOnlyList<TopEdsRow> NormalizeTopRows(MdxTable table, EdsTopReportOptions options)
    {
        return table.Rows
            .Select(row => new TopEdsRow(
                0,
                CleanText(Cell(row, 0)),
                CleanText(Cell(row, 1)),
                CleanText(Cell(row, 2)),
                CleanText(Cell(row, 3)),
                BorderZoneFlag(Cell(row, 4)),
                ParseVolume(Cell(row, 5), "VOLUMEN_ACEPTADO"),
                ParseVolume(Cell(row, 6), "VOLUMEN_DESPACHADO")))
            .Where(static row => !string.IsNullOrWhiteSpace(row.Code) && !IsAllMember(row.Code))
            .Where(row => MeasureValue(row, options.Measure) > 0)
            .OrderByDescending(row => MeasureValue(row, options.Measure))
            .ThenBy(static row => row.Code, StringComparer.OrdinalIgnoreCase)
            .Take(options.Top)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    public static decimal NormalizeNationalVolume(MdxTable table)
    {
        var total = table.Rows
            .Select(row => new { Zone = BorderZoneFlag(Cell(row, 0)), Volume = ParseVolume(Cell(row, 1), "VOLUMEN_NACIONAL") })
            .Where(static row => row.Zone is "SI" or "NO")
            .Sum(static row => row.Volume);
        return total > 0
            ? total
            : throw new InvalidDataException("The national EDS volume must be greater than zero.");
    }

    public static MdxTable BuildOutputTable(
        IReadOnlyList<TopEdsRow> topRows,
        decimal nationalVolume,
        EdsTopReportOptions options)
    {
        if (nationalVolume <= 0)
        {
            throw new InvalidDataException("The national EDS volume must be greater than zero.");
        }

        var outputRows = new List<IReadOnlyList<string>>(topRows.Count);
        foreach (var row in topRows.OrderBy(static row => row.Rank))
        {
            var rankingVolume = MeasureValue(row, options.Measure);
            outputRows.Add([
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.Code,
                row.Name,
                row.Municipality,
                row.Department,
                row.BorderZone,
                FormatDecimal(row.Accepted),
                FormatDecimal(row.Dispatched),
                MeasureArgument(options.Measure),
                FormatDecimal(rankingVolume),
                FormatPercentage(Divide(rankingVolume * 100, nationalVolume))
            ]);
        }

        return new MdxTable(
            [
                "RANK_EDS",
                "CODIGO_SICOM_COMPRADOR",
                "NOMBRE_COMERCIAL_COMPRADOR",
                "MUNICIPIO",
                "DEPARTAMENTO",
                "ZONA_FRONTERA",
                "VOLUMEN_ACEPTADO",
                "VOLUMEN_DESPACHADO",
                "MEDIDA_RANKING",
                "VOLUMEN_RANKING",
                "PARTICIPACION_NACIONAL_PCT"
            ],
            outputRows,
            null);
    }

    public static string BuildEquivalentCommand(EdsTopReportOptions options, string outputDirectory)
    {
        return string.Join(" ", [
            "dotnet run -- report eds_top",
            $"--year {options.Year}",
            options.PeriodCommandPart,
            $"--measure {MeasureArgument(options.Measure)}",
            $"--product {ProductArgument(options.Product)}",
            $"--top {options.Top}",
            $"--output-dir \"{outputDirectory}\""
        ]);
    }

    private static decimal MeasureValue(TopEdsRow row, MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => row.Accepted,
            MayoristasMeasureSelection.Dispatched => row.Dispatched,
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
        };
    }

    private static string MeasureUniqueName(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => "[Measures].[VOLUMEN ACEPTADO]",
            MayoristasMeasureSelection.Dispatched => "[Measures].[VOLUMEN DESPACHADO]",
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
        };
    }

    private static string MeasureArgument(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => "accepted",
            MayoristasMeasureSelection.Dispatched => "dispatched",
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
        };
    }

    private static string ProductArgument(MayoristasProductSelection product)
    {
        return product switch
        {
            MayoristasProductSelection.All => "all",
            MayoristasProductSelection.Corriente => "corriente",
            MayoristasProductSelection.Extra => "extra",
            MayoristasProductSelection.Diesel => "diesel",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null)
        };
    }

    private static IReadOnlyList<string> ProductUniqueNames(MayoristasProductSelection product)
    {
        return product switch
        {
            MayoristasProductSelection.All => [
                "BIODIESEL CON MEZCLA",
                "GASOLINA MOTOR CORRIENTE",
                "GASOLINA MOTOR EXTRA"
            ],
            MayoristasProductSelection.Corriente => ["GASOLINA MOTOR CORRIENTE"],
            MayoristasProductSelection.Extra => ["GASOLINA MOTOR EXTRA"],
            MayoristasProductSelection.Diesel => ["BIODIESEL CON MEZCLA"],
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null)
        };
    }

    private static string MonthMembers(EdsTopReportOptions options)
    {
        return string.Join($",{Environment.NewLine}", options.Months.Select(static month =>
            $"    [MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[{month}]"));
    }

    private static string ProductMembers(MayoristasProductSelection product)
    {
        return string.Join($",{Environment.NewLine}", ProductUniqueNames(product).Select(static productName =>
            $"    [PRODUCTO].[DESCRIPCION PRODUCTO].&[{productName}]"));
    }

    private static decimal ParseVolume(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"{field} contains a null or non-numeric value.");
        }

        if (parsed < 0)
        {
            throw new InvalidDataException($"{field} contains a negative value.");
        }

        return parsed;
    }

    private static string BorderZoneFlag(string value)
    {
        var normalized = NormalizeInvariant(value);
        if (normalized is "SI" or "S" or "1" or "TRUE") return "SI";
        if (normalized is "NO" or "N" or "0" or "FALSE") return "NO";
        if (normalized.Contains("NO", StringComparison.OrdinalIgnoreCase)) return "NO";
        if (normalized.Contains("SI", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("FRONTERA", StringComparison.OrdinalIgnoreCase)) return "SI";
        return "";
    }

    private static bool IsAllMember(string value)
    {
        return NormalizeInvariant(value) is "ALL" or "(ALL)" or "TODOS";
    }

    private static string NormalizeInvariant(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string CleanText(string value) => value.Replace('\u00A0', ' ').Trim();
    private static string Cell(IReadOnlyList<string> row, int index) => index < row.Count ? row[index] : "";
    private static decimal Divide(decimal numerator, decimal denominator) => denominator == 0 ? 0 : numerator / denominator;
    private static string FormatDecimal(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
    private static string FormatPercentage(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static XmlaDebugOptions? AddDebugSuffix(XmlaDebugOptions? debugOptions, string suffix)
    {
        return debugOptions is null
            ? null
            : new XmlaDebugOptions(
                AddFileNameSuffix(debugOptions.RequestOutputPath, suffix),
                AddFileNameSuffix(debugOptions.ResponseOutputPath, suffix));
    }

    private static string? AddFileNameSuffix(string? path, string suffix)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}{suffix}{extension}"
            : Path.Combine(directory, $"{fileName}{suffix}{extension}");
    }

    private static async Task WriteTextAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public sealed record TopEdsRow(
        int Rank,
        string Code,
        string Name,
        string Municipality,
        string Department,
        string BorderZone,
        decimal Accepted,
        decimal Dispatched);

    public sealed record EdsTopManifest(
        int SchemaVersion,
        string Report,
        string Status,
        string Catalog,
        string Cube,
        int Year,
        IReadOnlyList<int> Months,
        string PeriodLabel,
        string Measure,
        string Product,
        IReadOnlyList<string> Products,
        string BuyerScope,
        int Top,
        int RowCount,
        decimal NationalVolume,
        string EquivalentCommand,
        DateTimeOffset GeneratedAtUtc);
}
