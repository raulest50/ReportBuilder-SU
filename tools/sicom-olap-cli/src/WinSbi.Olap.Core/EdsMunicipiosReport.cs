using System.Globalization;
using System.Text;

namespace WinSbi.Olap.Core;

public static class EdsMunicipiosReport
{
    public const string CatalogName = MayoristasReport.CatalogName;
    public const string CubeName = MayoristasReport.CubeName;
    public const string AgentsCatalogName = "SBI-Agentes";
    public const string AgentsCubeName = "Agentes";

    public static EdsMunicipiosReportOptions CreateOptions(
        int year,
        int? quarter,
        int? semester,
        string? months,
        string product)
    {
        var period = ReportPeriods.Resolve(year, quarter, semester, months);

        return new EdsMunicipiosReportOptions(
            period.Year,
            period.Months,
            period.Label,
            period.CommandPart,
            MayoristasReport.ParseProduct(product));
    }

    public static EdsMunicipiosReportOptions CreateOptions(
        int year,
        int? quarter,
        string? months,
        string product)
    {
        return CreateOptions(year, quarter, null, months, product);
    }

    public static string DefaultOutputDirectory(EdsMunicipiosReportOptions options)
    {
        return Path.Combine("reports", CatalogName, "eds_municipios", options.PeriodLabel);
    }

    public static string BuildMdx(EdsMunicipiosReportOptions options)
    {
        var monthMembers = string.Join($",{Environment.NewLine}", options.Months.Select(static month =>
            $"    [MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[{month}]"));
        var productMembers = string.Join($",{Environment.NewLine}", ProductUniqueNames(options.Product).Select(static product =>
            $"    [PRODUCTO].[DESCRIPCION PRODUCTO].&[{product}]"));

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_products] AS {
{{productMembers}}
}
SET [rows] AS
    NONEMPTY(
        [COMPRADOR].[CODIGO DANE DEPARTAMENTO AGENTE COMPRADOR].[CODIGO DANE DEPARTAMENTO AGENTE COMPRADOR].Members
        * [COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].Members
        * [COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR].Members
        * [COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].Members
        * [COMPRADOR].[ZONA FRONTERA].[ZONA FRONTERA].Members
        * { [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}] }
        * [target_months]
        * [target_products],
        [Measures].[VOLUMEN ACEPTADO]
    )
SELECT
    { [Measures].[VOLUMEN ACEPTADO] } ON COLUMNS,
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
        [UBICACION INSTALACION].[CODIGO DANE DEPARTAMENTO INSTALACION].[CODIGO DANE DEPARTAMENTO INSTALACION].Members
        * [UBICACION INSTALACION].[DEPARTAMENTO INSTALACION].[DEPARTAMENTO INSTALACION].Members
        * [UBICACION INSTALACION].[CODIGO DANE MUNICIPIO INSTALACION].[CODIGO DANE MUNICIPIO INSTALACION].Members
        * [UBICACION INSTALACION].[MUNICIPIO INSTALACION].[MUNICIPIO INSTALACION].Members
        * [AGENTE].[ZONA DE FRONTERA].[ZONA DE FRONTERA].Members,
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

    public static async Task<EdsMunicipiosReportResult> GenerateAsync(
        IXmlaClient client,
        EdsMunicipiosReportOptions options,
        string outputDirectory,
        string equivalentCommand,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var detailMdx = BuildMdx(options);
        var detailResult = await client.ExecuteMdxAsync(
            CatalogName,
            detailMdx,
            debugOptions: debugOptions,
            cancellationToken: cancellationToken);

        var activeEdsMdx = BuildActiveEdsMdx();
        var activeEdsResult = await client.ExecuteMdxAsync(
            AgentsCatalogName,
            activeEdsMdx,
            debugOptions: BuildActiveEdsDebugOptions(debugOptions),
            cancellationToken: cancellationToken);

        var detailTable = NormalizeDetailTable(MdxTableBuilder.BuildTable(detailResult), options);
        var averageTable = BuildAverageSummary(detailTable, options);
        var activeEdsTable = NormalizeActiveEdsTable(MdxTableBuilder.BuildTable(activeEdsResult));

        Directory.CreateDirectory(outputDirectory);
        var detailPath = Path.Combine(outputDirectory, "eds-municipios-detalle.csv");
        var averagePath = Path.Combine(outputDirectory, "eds-municipios-promedio.csv");
        var activeEdsPath = Path.Combine(outputDirectory, "eds-municipios-eds-activas.csv");

        await File.WriteAllTextAsync(detailPath, OutputFormatters.ToCsv(detailTable), cancellationToken);
        await File.WriteAllTextAsync(averagePath, OutputFormatters.ToCsv(averageTable), cancellationToken);
        await File.WriteAllTextAsync(activeEdsPath, OutputFormatters.ToCsv(activeEdsTable), cancellationToken);

        return new EdsMunicipiosReportResult(
            options,
            outputDirectory,
            detailPath,
            averagePath,
            activeEdsPath,
            detailTable,
            averageTable,
            activeEdsTable,
            equivalentCommand);
    }

    public static MdxTable NormalizeDetailTable(MdxTable table, EdsMunicipiosReportOptions options)
    {
        var headers = DetailHeaders();
        var rows = table.Rows
            .Select(row =>
            {
                var departmentCode = Cell(row, 0);
                var department = Cell(row, 1);
                var municipalityCode = Cell(row, 2);
                var municipality = Cell(row, 3);
                var borderZone = Cell(row, 4);
                var year = Cell(row, 5);
                var month = Cell(row, 6);
                var product = Cell(row, 7);
                var volume = Cell(row, 8);
                var monthNumber = ParseInt(month);
                var yearNumber = ParseInt(year);

                return new[]
                {
                    departmentCode,
                    department,
                    municipalityCode,
                    municipality,
                    borderZone,
                    BorderZoneFlag(borderZone),
                    year,
                    month,
                    monthNumber is null ? "" : QuarterFromMonth(monthNumber.Value).ToString(CultureInfo.InvariantCulture),
                    BuildPeriod(yearNumber, monthNumber),
                    product,
                    ProductCanonical(product),
                    volume
                };
            })
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(headers, rows, table.Note);
    }

    public static MdxTable BuildAverageSummary(MdxTable detailTable, EdsMunicipiosReportOptions options)
    {
        var indexes = new AverageIndexes(
            HeaderIndex(detailTable, "CODIGO_DANE_DEPARTAMENTO"),
            HeaderIndex(detailTable, "DEPARTAMENTO"),
            HeaderIndex(detailTable, "CODIGO_DANE_MUNICIPIO"),
            HeaderIndex(detailTable, "MUNICIPIO"),
            HeaderIndex(detailTable, "ZONA_FRONTERA"),
            HeaderIndex(detailTable, "ES_ZONA_FRONTERA"),
            HeaderIndex(detailTable, "PRODUCTO"),
            HeaderIndex(detailTable, "PRODUCTO_CANONICO"),
            HeaderIndex(detailTable, "VOLUMEN_ACEPTADO"));

        var summaries = detailTable.Rows
            .GroupBy(row => new AverageKey(
                Cell(row, indexes.DepartmentCode),
                Cell(row, indexes.Department),
                Cell(row, indexes.MunicipalityCode),
                Cell(row, indexes.Municipality),
                Cell(row, indexes.BorderZone),
                Cell(row, indexes.BorderZoneFlag),
                Cell(row, indexes.Product),
                Cell(row, indexes.ProductCanonical)))
            .Select(group =>
            {
                var total = group.Sum(row => ParseDecimal(Cell(row, indexes.Volume)));
                var average = options.Months.Count == 0 ? 0 : total / options.Months.Count;
                return new AverageRow(group.Key, total, average);
            })
            .OrderBy(row => row.Key.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Key.Municipality, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Key.ProductCanonical, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = summaries
            .Select(row => new[]
            {
                row.Key.DepartmentCode,
                row.Key.Department,
                row.Key.MunicipalityCode,
                row.Key.Municipality,
                row.Key.BorderZone,
                row.Key.BorderZoneFlag,
                row.Key.Product,
                row.Key.ProductCanonical,
                options.Months.Count.ToString(CultureInfo.InvariantCulture),
                FormatDecimal(row.Total),
                FormatDecimal(row.Average)
            })
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(
            [
                "CODIGO_DANE_DEPARTAMENTO",
                "DEPARTAMENTO",
                "CODIGO_DANE_MUNICIPIO",
                "MUNICIPIO",
                "ZONA_FRONTERA",
                "ES_ZONA_FRONTERA",
                "PRODUCTO",
                "PRODUCTO_CANONICO",
                "MESES_PERIODO",
                "VOLUMEN_ACEPTADO_TOTAL",
                "PROMEDIO_GAL_MES"
            ],
            rows,
            null);
    }

    public static MdxTable NormalizeActiveEdsTable(MdxTable activeEdsTable)
    {
        var grouped = activeEdsTable.Rows
            .GroupBy(row => BuildMunicipalityKey(
                departmentCode: Cell(row, 0),
                department: Cell(row, 1),
                municipalityCode: Cell(row, 2),
                municipality: Cell(row, 3)))
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group =>
            {
                var first = group.First();
                var borderZones = group.Select(row => Cell(row, 4)).Where(static value => !string.IsNullOrWhiteSpace(value)).ToList();
                var borderZone = MergeBorderZones(borderZones);
                var activeEds = group.Sum(row => ParseDecimal(Cell(row, 5)));
                return new[]
                {
                    Cell(first, 0),
                    Cell(first, 1),
                    Cell(first, 2),
                    Cell(first, 3),
                    borderZone,
                    BorderZoneFlag(borderZone),
                    FormatDecimal(activeEds)
                };
            })
            .OrderBy(row => row[1], StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row[3], StringComparer.OrdinalIgnoreCase)
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(
            [
                "CODIGO_DANE_DEPARTAMENTO",
                "DEPARTAMENTO",
                "CODIGO_DANE_MUNICIPIO",
                "MUNICIPIO",
                "ZONA_FRONTERA",
                "ES_ZONA_FRONTERA",
                "EDS_AUTOMOTRIZ_ACTIVAS"
            ],
            grouped,
            activeEdsTable.Note);
    }

    public static string BuildEquivalentCommand(EdsMunicipiosReportOptions options, string outputDirectory)
    {
        return string.Join(" ", [
            "dotnet run -- report eds_municipios",
            $"--year {options.Year}",
            options.PeriodCommandPart,
            $"--product {ProductArgument(options.Product)}",
            $"--output-dir \"{outputDirectory}\""
        ]);
    }

    private static IReadOnlyList<string> DetailHeaders()
    {
        return [
            "CODIGO_DANE_DEPARTAMENTO",
            "DEPARTAMENTO",
            "CODIGO_DANE_MUNICIPIO",
            "MUNICIPIO",
            "ZONA_FRONTERA",
            "ES_ZONA_FRONTERA",
            "ANO",
            "MES",
            "TRIMESTRE",
            "PERIODO",
            "PRODUCTO",
            "PRODUCTO_CANONICO",
            "VOLUMEN_ACEPTADO"
        ];
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

    private static bool IsQuarterPeriod(EdsMunicipiosReportOptions options, out int quarter)
    {
        var months = options.Months;
        if (months.SequenceEqual([1, 2, 3])) { quarter = 1; return true; }
        if (months.SequenceEqual([4, 5, 6])) { quarter = 2; return true; }
        if (months.SequenceEqual([7, 8, 9])) { quarter = 3; return true; }
        if (months.SequenceEqual([10, 11, 12])) { quarter = 4; return true; }
        quarter = 0;
        return false;
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

    private static string ProductCanonical(string product)
    {
        var normalized = NormalizeInvariant(product);
        if (normalized.Contains("CORRIENTE", StringComparison.OrdinalIgnoreCase))
        {
            return "corriente";
        }

        if (normalized.Contains("EXTRA", StringComparison.OrdinalIgnoreCase))
        {
            return "extra";
        }

        if (normalized.Contains("BIODIESEL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("DIESEL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ACPM", StringComparison.OrdinalIgnoreCase))
        {
            return "diesel";
        }

        return product.Trim().ToLowerInvariant();
    }

    private static string BorderZoneFlag(string borderZone)
    {
        if (string.IsNullOrWhiteSpace(borderZone))
        {
            return "";
        }

        var normalized = NormalizeInvariant(borderZone);
        if (normalized is "SI" or "S" or "1" or "TRUE")
        {
            return "SI";
        }

        if (normalized is "NO" or "N" or "0" or "FALSE")
        {
            return "NO";
        }

        if (normalized.Contains("NO", StringComparison.OrdinalIgnoreCase))
        {
            return "NO";
        }

        if (normalized.Contains("SI", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("FRONTERA", StringComparison.OrdinalIgnoreCase))
        {
            return "SI";
        }

        return borderZone.Trim();
    }

    private static string BuildPeriod(int? year, int? month)
    {
        return year is null || month is null
            ? ""
            : $"{year.Value:0000}-{month.Value:00}";
    }

    private static int QuarterFromMonth(int month)
    {
        return ((month - 1) / 3) + 1;
    }

    private static int? ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static XmlaDebugOptions? BuildActiveEdsDebugOptions(XmlaDebugOptions? debugOptions)
    {
        if (debugOptions is null)
        {
            return null;
        }

        return new XmlaDebugOptions(
            AddFileNameSuffix(debugOptions.RequestOutputPath, "-eds-activas"),
            AddFileNameSuffix(debugOptions.ResponseOutputPath, "-eds-activas"));
    }

    private static string? AddFileNameSuffix(string? path, string suffix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(directory)
            ? $"{fileName}{suffix}{extension}"
            : Path.Combine(directory, $"{fileName}{suffix}{extension}");
    }

    private static int HeaderIndex(MdxTable table, string header)
    {
        var index = table.Headers
            .Select((value, itemIndex) => new { value, itemIndex })
            .FirstOrDefault(item => string.Equals(item.value, header, StringComparison.OrdinalIgnoreCase))
            ?.itemIndex;
        return index ?? throw new ArgumentException($"Table does not contain header '{header}'.");
    }

    private static string Cell(IReadOnlyList<string> row, int index)
    {
        return index < row.Count ? row[index] : "";
    }

    private static string BuildMunicipalityKey(
        string departmentCode,
        string department,
        string municipalityCode,
        string municipality)
    {
        var primary = NormalizeKey(municipalityCode);
        if (string.IsNullOrWhiteSpace(primary))
        {
            primary = $"{NormalizeKey(department)}|{NormalizeKey(municipality)}";
        }
        else
        {
            primary = $"{NormalizeKey(departmentCode)}|{primary}";
        }

        return primary;
    }

    private static string MergeBorderZones(IReadOnlyList<string> borderZones)
    {
        if (borderZones.Count == 0)
        {
            return "";
        }

        var flags = borderZones.Select(BorderZoneFlag).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (flags.Any(static flag => string.Equals(flag, "SI", StringComparison.OrdinalIgnoreCase)))
        {
            return "SI";
        }

        if (flags.Any(static flag => string.Equals(flag, "NO", StringComparison.OrdinalIgnoreCase)))
        {
            return "NO";
        }

        return borderZones[0];
    }

    private static string NormalizeKey(string value)
    {
        return NormalizeInvariant(value).Trim();
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

    private sealed record AverageIndexes(
        int DepartmentCode,
        int Department,
        int MunicipalityCode,
        int Municipality,
        int BorderZone,
        int BorderZoneFlag,
        int Product,
        int ProductCanonical,
        int Volume);

    private sealed record AverageKey(
        string DepartmentCode,
        string Department,
        string MunicipalityCode,
        string Municipality,
        string BorderZone,
        string BorderZoneFlag,
        string Product,
        string ProductCanonical);

    private sealed record AverageRow(AverageKey Key, decimal Total, decimal Average);
}
