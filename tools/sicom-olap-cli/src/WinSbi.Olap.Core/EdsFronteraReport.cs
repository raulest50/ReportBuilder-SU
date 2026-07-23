using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WinSbi.Olap.Core;

public static class EdsFronteraReport
{
    public const string CatalogName = MayoristasReport.CatalogName;
    public const string CubeName = MayoristasReport.CubeName;
    public const string AgentsCatalogName = EdsMunicipiosReport.AgentsCatalogName;
    public const string AgentsCubeName = EdsMunicipiosReport.AgentsCubeName;
    public const int ManifestSchemaVersion = 1;
    public const int DefaultTopMunicipalities = 5;

    private static readonly string[] TargetProducts =
    [
        "BIODIESEL CON MEZCLA",
        "GASOLINA MOTOR CORRIENTE",
        "GASOLINA MOTOR EXTRA"
    ];

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    public static EdsFronteraReportOptions CreateOptions(
        int year,
        int? quarter,
        int? semester,
        bool annual,
        string? months)
    {
        var period = ReportPeriods.Resolve(year, quarter, semester, annual, months);
        return new EdsFronteraReportOptions(
            period.Year,
            period.Months,
            period.Label,
            period.CommandPart,
            DefaultTopMunicipalities);
    }

    public static string DefaultOutputDirectory(EdsFronteraReportOptions options)
    {
        return Path.Combine("reports", CatalogName, "eds_frontera", options.PeriodLabel);
    }

    public static string BuildVolumesMdx(EdsFronteraReportOptions options)
    {
        return $$"""
WITH
SET [target_months] AS {
{{MonthMembers(options)}}
}
SET [target_products] AS {
{{ProductMembers()}}
}
SET [target_years] AS {
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year - 1}}],
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}]
}
MEMBER [Measures].[VOLUMEN DESPACHADO PERIODO] AS
    SUM([target_months], [Measures].[VOLUMEN DESPACHADO])
SET [rows] AS
    NONEMPTY(
        [COMPRADOR].[ZONA FRONTERA].[ZONA FRONTERA].Members
        * [target_years]
        * [target_products],
        [Measures].[VOLUMEN DESPACHADO PERIODO]
    )
SELECT
    { [Measures].[VOLUMEN DESPACHADO PERIODO] } ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
WHERE ([COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ])
""";
    }

    public static string BuildActiveEdsMdx()
    {
        return $$"""
WITH
SET [rows] AS
    NONEMPTY(
        [AGENTE].[ZONA DE FRONTERA].[ZONA DE FRONTERA].Members,
        [Measures].[AGENTE CANTIDAD]
    )
SELECT
    { [Measures].[AGENTE CANTIDAD] } ON COLUMNS,
    [rows] ON ROWS
FROM [{{AgentsCubeName}}]
WHERE (
    [SUBTIPO AGENTE].[SUBTIPO AGENTE].&[ESTACION DE SERVICIO AUTOMOTRIZ],
    [AGENTE].[NUEVO ESTADO].&[ACTIVO]
)
""";
    }

    public static string BuildMunicipalitiesMdx(EdsFronteraReportOptions options)
    {
        return $$"""
WITH
SET [target_months] AS {
{{MonthMembers(options)}}
}
SET [target_products] AS {
{{ProductMembers()}}
}
MEMBER [Measures].[VOLUMEN DESPACHADO PERIODO] AS
    SUM([target_months] * [target_products], [Measures].[VOLUMEN DESPACHADO])
SET [rows] AS
    TOPCOUNT(
        NONEMPTY(
            [COMPRADOR].[CODIGO DANE DEPARTAMENTO AGENTE COMPRADOR].[CODIGO DANE DEPARTAMENTO AGENTE COMPRADOR].Members
            * [COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].Members
            * [COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR].Members
            * [COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].Members,
            [Measures].[VOLUMEN DESPACHADO PERIODO]
        ),
        {{options.TopMunicipalities}},
        [Measures].[VOLUMEN DESPACHADO PERIODO]
    )
SELECT
    { [Measures].[VOLUMEN DESPACHADO PERIODO] } ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
WHERE (
    [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}],
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ],
    [COMPRADOR].[ZONA FRONTERA].&[SI]
)
""";
    }

    public static async Task<EdsFronteraReportResult> GenerateAsync(
        IXmlaClient client,
        EdsFronteraReportOptions options,
        string outputDirectory,
        string equivalentCommand,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var volumesResult = await client.ExecuteMdxAsync(
            CatalogName,
            BuildVolumesMdx(options),
            debugOptions: AddDebugSuffix(debugOptions, "-zfd-volumenes"),
            cancellationToken: cancellationToken);
        var volumeRows = NormalizeVolumeRows(MdxTableBuilder.BuildTable(volumesResult));

        var activeResult = await client.ExecuteMdxAsync(
            AgentsCatalogName,
            BuildActiveEdsMdx(),
            debugOptions: AddDebugSuffix(debugOptions, "-zfd-eds-activas"),
            cancellationToken: cancellationToken);
        var activeRows = NormalizeActiveRows(MdxTableBuilder.BuildTable(activeResult));
        var activeEdsQueriedAtUtc = DateTimeOffset.UtcNow;

        var municipalitiesResult = await client.ExecuteMdxAsync(
            CatalogName,
            BuildMunicipalitiesMdx(options),
            debugOptions: AddDebugSuffix(debugOptions, "-zfd-municipios"),
            cancellationToken: cancellationToken);
        var municipalityRows = NormalizeMunicipalityRows(
            MdxTableBuilder.BuildTable(municipalitiesResult),
            options.TopMunicipalities);

        ValidateCoverage(volumeRows, activeRows, options);
        var summaryTable = BuildSummaryTable(volumeRows, activeRows, options);
        var productsTable = BuildProductsTable(volumeRows, options);
        var municipalitiesTable = BuildMunicipalitiesTable(municipalityRows, volumeRows, options);

        Directory.CreateDirectory(outputDirectory);
        var summaryPath = Path.Combine(outputDirectory, "zfd-resumen.csv");
        var productsPath = Path.Combine(outputDirectory, "zfd-productos.csv");
        var municipalitiesPath = Path.Combine(outputDirectory, "zfd-municipios.csv");
        var manifestPath = Path.Combine(outputDirectory, "zfd-manifest.json");
        var manifest = new EdsFronteraManifest(
            ManifestSchemaVersion,
            "eds_frontera",
            "complete",
            CatalogName,
            CubeName,
            AgentsCatalogName,
            AgentsCubeName,
            options.Year,
            options.Months,
            options.PeriodLabel,
            "dispatched",
            TargetProducts,
            "ESTACION DE SERVICIO AUTOMOTRIZ",
            options.TopMunicipalities,
            summaryTable.Rows.Count,
            productsTable.Rows.Count,
            municipalitiesTable.Rows.Count,
            equivalentCommand,
            activeEdsQueriedAtUtc,
            DateTimeOffset.UtcNow);

        await WriteTextAtomicAsync(summaryPath, OutputFormatters.ToCsv(summaryTable), cancellationToken);
        await WriteTextAtomicAsync(productsPath, OutputFormatters.ToCsv(productsTable), cancellationToken);
        await WriteTextAtomicAsync(
            municipalitiesPath,
            OutputFormatters.ToCsv(municipalitiesTable),
            cancellationToken);
        await WriteTextAtomicAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions) + Environment.NewLine,
            cancellationToken);

        return new EdsFronteraReportResult(
            options,
            outputDirectory,
            summaryPath,
            productsPath,
            municipalitiesPath,
            manifestPath,
            summaryTable,
            productsTable,
            municipalitiesTable,
            equivalentCommand,
            activeEdsQueriedAtUtc);
    }

    public static IReadOnlyList<ZoneVolumeRow> NormalizeVolumeRows(MdxTable table)
    {
        return table.Rows
            .Select(row => new ZoneVolumeRow(
                BorderZoneFlag(Cell(row, 0)),
                ParseInt(Cell(row, 1), "ANO"),
                CleanText(Cell(row, 2)),
                ProductCanonical(Cell(row, 2)),
                ParseNonNegativeDecimal(Cell(row, 3), "VOLUMEN_DESPACHADO")))
            .Where(static row => row.Zone is "SI" or "NO")
            .ToList();
    }

    public static IReadOnlyList<ActiveEdsRow> NormalizeActiveRows(MdxTable table)
    {
        return table.Rows
            .Select(row => new ActiveEdsRow(
                BorderZoneFlag(Cell(row, 0)),
                ParseNonNegativeDecimal(Cell(row, 1), "EDS_ACTIVAS")))
            .Where(static row => row.Zone is "SI" or "NO")
            .ToList();
    }

    public static IReadOnlyList<MunicipalityVolumeRow> NormalizeMunicipalityRows(
        MdxTable table,
        int top)
    {
        return table.Rows
            .Select(row => new MunicipalityVolumeRow(
                0,
                CleanText(Cell(row, 0)),
                CleanText(Cell(row, 1)),
                CleanText(Cell(row, 2)),
                CleanText(Cell(row, 3)),
                ParseNonNegativeDecimal(Cell(row, 4), "VOLUMEN_DESPACHADO")))
            .Where(static row => !string.IsNullOrWhiteSpace(row.MunicipalityCode))
            .Where(static row => !IsAllMember(row.MunicipalityCode))
            .Where(static row => row.Dispatched > 0)
            .OrderByDescending(static row => row.Dispatched)
            .ThenBy(static row => row.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Municipality, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    public static MdxTable BuildSummaryTable(
        IReadOnlyList<ZoneVolumeRow> volumeRows,
        IReadOnlyList<ActiveEdsRow> activeRows,
        EdsFronteraReportOptions options)
    {
        var current = volumeRows.Where(row => row.Year == options.Year).ToList();
        var previous = volumeRows.Where(row => row.Year == options.Year - 1).ToList();
        var activeByZone = activeRows
            .GroupBy(static row => row.Zone, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Sum(row => row.ActiveEds),
                StringComparer.OrdinalIgnoreCase);
        var nationalCurrent = current.Sum(static row => row.Dispatched);
        var nationalPrevious = previous.Sum(static row => row.Dispatched);
        var nationalActive = activeByZone.Values.Sum();
        var rows = new List<IReadOnlyList<string>>();

        foreach (var zone in new[] { "SI", "NO" })
        {
            var currentVolume = current.Where(row => row.Zone == zone).Sum(static row => row.Dispatched);
            var previousVolume = previous.Where(row => row.Zone == zone).Sum(static row => row.Dispatched);
            activeByZone.TryGetValue(zone, out var activeEds);
            rows.Add(SummaryRow(
                zone,
                previousVolume,
                currentVolume,
                activeEds,
                nationalActive,
                nationalCurrent,
                options.Months.Count));
        }

        rows.Add(SummaryRow(
            "TOTAL NACIONAL",
            nationalPrevious,
            nationalCurrent,
            nationalActive,
            nationalActive,
            nationalCurrent,
            options.Months.Count));

        return new MdxTable(
            [
                "ZONA_FRONTERA",
                "VOLUMEN_DESPACHADO_ANIO_ANTERIOR",
                "VOLUMEN_DESPACHADO",
                "CAMBIO_ABSOLUTO_DESPACHADO",
                "VAR_INTERANUAL_DESPACHADO_PCT",
                "EDS_ACTIVAS",
                "PARTICIPACION_EDS_NACIONAL_PCT",
                "PARTICIPACION_VOLUMEN_NACIONAL_PCT",
                "GAL_MES_EDS_DESPACHADO"
            ],
            rows,
            null);
    }

    public static MdxTable BuildProductsTable(
        IReadOnlyList<ZoneVolumeRow> volumeRows,
        EdsFronteraReportOptions options)
    {
        var current = volumeRows.Where(row => row.Year == options.Year).ToList();
        var zoneTotals = current
            .GroupBy(static row => row.Zone, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Sum(row => row.Dispatched),
                StringComparer.OrdinalIgnoreCase);
        var rows = current
            .GroupBy(row => new ProductZoneKey(row.Zone, row.Product, row.ProductCanonical))
            .Select(group =>
            {
                var dispatched = group.Sum(row => row.Dispatched);
                zoneTotals.TryGetValue(group.Key.Zone, out var zoneTotal);
                return (IReadOnlyList<string>)[
                    group.Key.Zone,
                    group.Key.Product,
                    group.Key.ProductCanonical,
                    FormatDecimal(dispatched),
                    FormatPercentage(Divide(dispatched * 100, zoneTotal))
                ];
            })
            .OrderBy(row => row[0], StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row[2], StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MdxTable(
            [
                "ZONA_FRONTERA",
                "PRODUCTO",
                "PRODUCTO_CANONICO",
                "VOLUMEN_DESPACHADO",
                "PARTICIPACION_PRODUCTO_EN_ZONA_PCT"
            ],
            rows,
            null);
    }

    public static MdxTable BuildMunicipalitiesTable(
        IReadOnlyList<MunicipalityVolumeRow> municipalityRows,
        IReadOnlyList<ZoneVolumeRow> volumeRows,
        EdsFronteraReportOptions options)
    {
        var frontierTotal = volumeRows
            .Where(row => row.Year == options.Year && row.Zone == "SI")
            .Sum(static row => row.Dispatched);
        var rows = municipalityRows
            .OrderBy(static row => row.Rank)
            .Select(row => (IReadOnlyList<string>)[
                row.Rank.ToString(CultureInfo.InvariantCulture),
                row.DepartmentCode,
                row.Department,
                row.MunicipalityCode,
                row.Municipality,
                FormatDecimal(row.Dispatched),
                FormatPercentage(Divide(row.Dispatched * 100, frontierTotal))
            ])
            .ToList();

        return new MdxTable(
            [
                "RANK_MUNICIPIO",
                "CODIGO_DANE_DEPARTAMENTO",
                "DEPARTAMENTO",
                "CODIGO_DANE_MUNICIPIO",
                "MUNICIPIO",
                "VOLUMEN_DESPACHADO",
                "PARTICIPACION_VOLUMEN_FRONTERA_PCT"
            ],
            rows,
            null);
    }

    public static string BuildEquivalentCommand(EdsFronteraReportOptions options, string outputDirectory)
    {
        return string.Join(" ", [
            "dotnet run -- report eds_frontera",
            $"--year {options.Year}",
            options.PeriodCommandPart,
            $"--output-dir \"{outputDirectory}\""
        ]);
    }

    private static void ValidateCoverage(
        IReadOnlyList<ZoneVolumeRow> volumeRows,
        IReadOnlyList<ActiveEdsRow> activeRows,
        EdsFronteraReportOptions options)
    {
        foreach (var year in new[] { options.Year - 1, options.Year })
        {
            foreach (var zone in new[] { "SI", "NO" })
            {
                var products = volumeRows
                    .Where(row => row.Year == year && row.Zone == zone)
                    .Select(static row => row.ProductCanonical)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = new[] { "corriente", "diesel", "extra" }.Where(product => !products.Contains(product)).ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidDataException(
                        $"Missing ZFD products for {zone}/{year}: {string.Join(", ", missing)}.");
                }
            }
        }

        var activeZones = activeRows.Select(static row => row.Zone).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!activeZones.Contains("SI") || !activeZones.Contains("NO"))
        {
            throw new InvalidDataException("Active EDS must contain the SI and NO border zones.");
        }
    }

    private static IReadOnlyList<string> SummaryRow(
        string zone,
        decimal previousVolume,
        decimal currentVolume,
        decimal activeEds,
        decimal nationalActive,
        decimal nationalCurrent,
        int monthCount)
    {
        var change = currentVolume - previousVolume;
        var variation = previousVolume == 0 ? "" : FormatPercentage(change * 100 / previousVolume);
        var intensity = activeEds == 0 || monthCount == 0
            ? ""
            : FormatDecimal(currentVolume / monthCount / activeEds);
        return
        [
            zone,
            FormatDecimal(previousVolume),
            FormatDecimal(currentVolume),
            FormatDecimal(change),
            variation,
            FormatDecimal(activeEds),
            FormatPercentage(Divide(activeEds * 100, nationalActive)),
            FormatPercentage(Divide(currentVolume * 100, nationalCurrent)),
            intensity
        ];
    }

    private static string MonthMembers(EdsFronteraReportOptions options)
    {
        return string.Join($",{Environment.NewLine}", options.Months.Select(static month =>
            $"    [MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[{month}]"));
    }

    private static string ProductMembers()
    {
        return string.Join($",{Environment.NewLine}", TargetProducts.Select(static product =>
            $"    [PRODUCTO].[DESCRIPCION PRODUCTO].&[{product}]"));
    }

    private static string ProductCanonical(string product)
    {
        var normalized = NormalizeInvariant(product);
        if (normalized.Contains("CORRIENTE", StringComparison.OrdinalIgnoreCase)) return "corriente";
        if (normalized.Contains("EXTRA", StringComparison.OrdinalIgnoreCase)) return "extra";
        if (normalized.Contains("BIODIESEL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("DIESEL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ACPM", StringComparison.OrdinalIgnoreCase)) return "diesel";
        return product.Trim().ToLowerInvariant();
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

    private static int ParseInt(string value, string field)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"{field} contains a null or non-numeric value.");
        }
        return parsed;
    }

    private static decimal ParseNonNegativeDecimal(string value, string field)
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

    private static string CleanText(string value) => value.Replace('\u00A0', ' ').Trim();
    private static string Cell(IReadOnlyList<string> row, int index) => index < row.Count ? row[index] : "";
    private static decimal Divide(decimal numerator, decimal denominator) => denominator == 0 ? 0 : numerator / denominator;
    private static string FormatDecimal(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
    private static string FormatPercentage(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

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

    private static async Task WriteTextAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public sealed record ZoneVolumeRow(
        string Zone,
        int Year,
        string Product,
        string ProductCanonical,
        decimal Dispatched);

    public sealed record ActiveEdsRow(string Zone, decimal ActiveEds);

    public sealed record MunicipalityVolumeRow(
        int Rank,
        string DepartmentCode,
        string Department,
        string MunicipalityCode,
        string Municipality,
        decimal Dispatched);

    private sealed record ProductZoneKey(string Zone, string Product, string ProductCanonical);

    public sealed record EdsFronteraManifest(
        int SchemaVersion,
        string Report,
        string Status,
        string Catalog,
        string Cube,
        string AgentsCatalog,
        string AgentsCube,
        int Year,
        IReadOnlyList<int> Months,
        string PeriodLabel,
        string Measure,
        IReadOnlyList<string> Products,
        string BuyerScope,
        int TopMunicipalities,
        int SummaryRowCount,
        int ProductRowCount,
        int MunicipalityRowCount,
        string EquivalentCommand,
        DateTimeOffset ActiveEdsQueriedAtUtc,
        DateTimeOffset GeneratedAtUtc);
}
