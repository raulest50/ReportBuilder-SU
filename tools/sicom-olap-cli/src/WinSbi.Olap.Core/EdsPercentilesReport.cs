using System.Globalization;
using System.Text;

namespace WinSbi.Olap.Core;

public static class EdsPercentilesReport
{
    public const string CatalogName = MayoristasReport.CatalogName;
    public const string CubeName = MayoristasReport.CubeName;
    public const int WindowMonthCount = 12;

    private static readonly IReadOnlyList<string> CanonicalProductOrder = ["corriente", "extra", "diesel"];

    public static EdsPercentilesReportOptions CreateOptions(int endYear, int endMonth, string product, string buyerScope = "eds")
    {
        if (endYear < 2000 || endYear > 2100)
        {
            throw new ArgumentException("--end-year must be a four-digit year.");
        }

        if (endMonth < 1 || endMonth > 12)
        {
            throw new ArgumentException("--end-month must be between 1 and 12.");
        }

        var windowMonths = BuildWindowMonths(endYear, endMonth);
        return new EdsPercentilesReportOptions(
            endYear,
            endMonth,
            windowMonths,
            $"{endYear}-{endMonth:00}",
            MayoristasReport.ParseProduct(product),
            ParseBuyerScope(buyerScope));
    }

    public static IReadOnlyList<EdsPercentilesWindowMonth> BuildWindowMonths(int endYear, int endMonth)
    {
        if (endMonth < 1 || endMonth > 12)
        {
            throw new ArgumentException("--end-month must be between 1 and 12.");
        }

        var end = new DateOnly(endYear, endMonth, 1);
        return Enumerable.Range(0, WindowMonthCount)
            .Select(offset => end.AddMonths(offset - (WindowMonthCount - 1)))
            .Select(date => new EdsPercentilesWindowMonth(date.Year, date.Month))
            .ToList();
    }

    public static string DefaultOutputDirectory(EdsPercentilesReportOptions options)
    {
        return Path.Combine("reports", CatalogName, "eds_percentiles", options.PeriodLabel);
    }

    public static string BuildMdx(EdsPercentilesReportOptions options)
    {
        var periodTuples = string.Join($",{Environment.NewLine}", options.WindowMonths.Select(static period =>
            $"    ([MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{period.Year}], [MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[{period.Month}])"));
        var productMembers = string.Join($",{Environment.NewLine}", ProductUniqueNames(options.Product).Select(static product =>
            $"    [PRODUCTO].[DESCRIPCION PRODUCTO].&[{product}]"));
        var buyerSubtypeMembers = string.Join($",{Environment.NewLine}", BuyerSubtypeUniqueNames(options.BuyerScope).Select(static subtype =>
            $"    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[{subtype}]"));

        return $$"""
WITH
SET [target_periods] AS {
{{periodTuples}}
}
SET [target_products] AS {
{{productMembers}}
}
SET [target_buyer_subtypes] AS {
{{buyerSubtypeMembers}}
}
SET [rows] AS
    NONEMPTY(
        [COMPRADOR].[CODIGO SICOM COMPRADOR].[CODIGO SICOM COMPRADOR].Members
        * [COMPRADOR].[NOMBRE COMERCIAL COMPRADOR].[NOMBRE COMERCIAL COMPRADOR].Members
        * [COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR].Members
        * [COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].[MUNICIPIO AGENTE COMPRADOR].Members
        * [COMPRADOR].[ZONA FRONTERA].[ZONA FRONTERA].Members
        * [target_buyer_subtypes]
        * [target_periods]
        * [target_products],
        [Measures].[VOLUMEN ACEPTADO]
    )
SELECT
    {
        [Measures].[VOLUMEN ACEPTADO],
        [Measures].[VOLUMEN DESPACHADO]
    } ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
""";
    }

    public static async Task<EdsPercentilesReportResult> GenerateAsync(
        IXmlaClient client,
        EdsPercentilesReportOptions options,
        string outputDirectory,
        string equivalentCommand,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var result = await client.ExecuteMdxAsync(
            CatalogName,
            BuildMdx(options),
            debugOptions: debugOptions,
            cancellationToken: cancellationToken);

        var baseTable = NormalizeBaseTable(MdxTableBuilder.BuildTable(result));
        var stationsTable = BuildStationsTable(baseTable);
        var summaryTable = BuildSummaryTable(stationsTable);

        Directory.CreateDirectory(outputDirectory);
        var basePath = Path.Combine(outputDirectory, "eds-percentiles-base-12m.csv");
        var stationsPath = Path.Combine(outputDirectory, "eds-percentiles-estaciones.csv");
        var summaryPath = Path.Combine(outputDirectory, "eds-percentiles-resumen.csv");

        await File.WriteAllTextAsync(basePath, OutputFormatters.ToCsv(baseTable), cancellationToken);
        await File.WriteAllTextAsync(stationsPath, OutputFormatters.ToCsv(stationsTable), cancellationToken);
        await File.WriteAllTextAsync(summaryPath, OutputFormatters.ToCsv(summaryTable), cancellationToken);

        return new EdsPercentilesReportResult(
            options,
            outputDirectory,
            basePath,
            stationsPath,
            summaryPath,
            baseTable,
            stationsTable,
            summaryTable,
            equivalentCommand);
    }

    public static MdxTable NormalizeBaseTable(MdxTable table)
    {
        var rows = table.Rows
            .Select(row =>
            {
                var code = Cell(row, 0);
                var name = Cell(row, 1);
                var department = Cell(row, 2);
                var municipality = Cell(row, 3);
                var borderFlag = BorderZoneFlag(Cell(row, 4));
                var buyerSubtype = Cell(row, 5);
                var year = Cell(row, 6);
                var month = Cell(row, 7);
                var product = Cell(row, 8);
                var yearNumber = ParseInt(year);
                var monthNumber = ParseInt(month);
                return new[]
                {
                    code,
                    name,
                    department,
                    municipality,
                    borderFlag,
                    buyerSubtype,
                    year,
                    month,
                    BuildPeriod(yearNumber, monthNumber),
                    product,
                    ProductCanonical(product),
                    FormatDecimal(ParseDecimal(Cell(row, 9))),
                    FormatDecimal(ParseDecimal(Cell(row, 10)))
                };
            })
            .Where(static row => !string.IsNullOrWhiteSpace(row[0]))
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(
            [
                "CODIGO_SICOM_COMPRADOR",
                "NOMBRE_COMERCIAL_COMPRADOR",
                "DEPARTAMENTO",
                "MUNICIPIO",
                "ZONA_FRONTERA",
                "SUBTIPO_AGENTE_COMPRADOR",
                "ANO",
                "MES",
                "PERIODO",
                "PRODUCTO",
                "PRODUCTO_CANONICO",
                "VOLUMEN_ACEPTADO",
                "VOLUMEN_DESPACHADO"
            ],
            rows,
            table.Note);
    }

    public static MdxTable BuildStationsTable(MdxTable baseTable)
    {
        var rows = BuildStationRows(baseTable)
            .Where(static row => row.AcceptedTotal > 0)
            .OrderBy(static row => row.GalMonthAcceptedTotal)
            .ThenBy(static row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rankedRows = AssignDeciles(rows);
        return new MdxTable(StationHeaders(), rankedRows.Select(StationToCsvRow).Cast<IReadOnlyList<string>>().ToList(), null);
    }

    public static MdxTable BuildSummaryTable(MdxTable stationsTable)
    {
        var decileIndex = HeaderIndex(stationsTable, "DECIL_ACEPTADO_TOTAL");
        var acceptedIndex = HeaderIndex(stationsTable, "GAL_MES_ACEPTADO_TOTAL");
        var dispatchedIndex = HeaderIndex(stationsTable, "GAL_MES_DESPACHADO_TOTAL");

        var rows = stationsTable.Rows
            .Select(row => new
            {
                Decile = ParseInt(Cell(row, decileIndex)) ?? 0,
                Accepted = ParseDecimal(Cell(row, acceptedIndex)),
                Dispatched = ParseDecimal(Cell(row, dispatchedIndex))
            })
            .Where(static row => row.Decile > 0)
            .GroupBy(static row => row.Decile)
            .OrderBy(static group => group.Key)
            .Select(group =>
            {
                var accepted = group.Select(static row => row.Accepted).ToList();
                var dispatched = group.Select(static row => row.Dispatched).ToList();
                return new[]
                {
                    group.Key.ToString(CultureInfo.InvariantCulture),
                    group.Count().ToString(CultureInfo.InvariantCulture),
                    FormatDecimal(Average(accepted)),
                    FormatDecimal(StandardDeviation(accepted)),
                    FormatDecimal(accepted.Count == 0 ? 0 : accepted.Min()),
                    FormatDecimal(accepted.Count == 0 ? 0 : accepted.Max()),
                    FormatDecimal(Average(dispatched)),
                    FormatDecimal(StandardDeviation(dispatched)),
                    FormatDecimal(dispatched.Count == 0 ? 0 : dispatched.Min()),
                    FormatDecimal(dispatched.Count == 0 ? 0 : dispatched.Max())
                };
            })
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(
            [
                "DECIL",
                "CANTIDAD_EDS",
                "MEDIA_GAL_MES_ACEPTADO",
                "DESVIACION_GAL_MES_ACEPTADO",
                "MINIMA_GAL_MES_ACEPTADO",
                "MAXIMA_GAL_MES_ACEPTADO",
                "MEDIA_GAL_MES_DESPACHADO",
                "DESVIACION_GAL_MES_DESPACHADO",
                "MINIMA_GAL_MES_DESPACHADO",
                "MAXIMA_GAL_MES_DESPACHADO"
            ],
            rows,
            null);
    }

    public static string BuildEquivalentCommand(EdsPercentilesReportOptions options, string outputDirectory)
    {
        return string.Join(" ", [
            "dotnet run -- report eds_percentiles",
            $"--end-year {options.EndYear}",
            $"--end-month {options.EndMonth}",
            $"--product {ProductArgument(options.Product)}",
            $"--buyer-scope {BuyerScopeArgument(options.BuyerScope)}",
            $"--output-dir \"{outputDirectory}\""
        ]);
    }

    public static EdsBuyerScope ParseBuyerScope(string buyerScope)
    {
        return NormalizeInvariant(buyerScope).Replace("-", "_", StringComparison.Ordinal) switch
        {
            "EDS" or "SOLO_EDS" or "AUTOMOTRIZ" => EdsBuyerScope.Eds,
            "EDS_FLUVIAL" or "EDS_Y_FLUVIAL" or "AUTOMOTRIZ_FLUVIAL" => EdsBuyerScope.EdsFluvial,
            "EDS_FLUVIAL_INDUSTRIAL" or "EDS_FLUVIAL_COMERCIALIZADOR_INDUSTRIAL" or "EDS_FLUVIAL_COMER_INDUSTRIAL" =>
                EdsBuyerScope.EdsFluvialIndustrial,
            _ => throw new ArgumentException("--buyer-scope must be eds, eds_fluvial, or eds_fluvial_industrial.")
        };
    }

    private static IReadOnlyList<StationRow> BuildStationRows(MdxTable baseTable)
    {
        var indexes = new BaseIndexes(
            HeaderIndex(baseTable, "CODIGO_SICOM_COMPRADOR"),
            HeaderIndex(baseTable, "NOMBRE_COMERCIAL_COMPRADOR"),
            HeaderIndex(baseTable, "DEPARTAMENTO"),
            HeaderIndex(baseTable, "MUNICIPIO"),
            HeaderIndex(baseTable, "ZONA_FRONTERA"),
            HeaderIndex(baseTable, "SUBTIPO_AGENTE_COMPRADOR"),
            HeaderIndex(baseTable, "PRODUCTO_CANONICO"),
            HeaderIndex(baseTable, "VOLUMEN_ACEPTADO"),
            HeaderIndex(baseTable, "VOLUMEN_DESPACHADO"));

        return baseTable.Rows
            .GroupBy(row => new StationKey(
                Cell(row, indexes.Code),
                Cell(row, indexes.Name),
                Cell(row, indexes.Department),
                Cell(row, indexes.Municipality),
                Cell(row, indexes.BorderFlag)))
            .Select(group =>
            {
                var acceptedByProduct = CanonicalProductOrder.ToDictionary(static product => product, static _ => 0m, StringComparer.OrdinalIgnoreCase);
                var dispatchedByProduct = CanonicalProductOrder.ToDictionary(static product => product, static _ => 0m, StringComparer.OrdinalIgnoreCase);
                var subtypes = group
                    .Select(row => Cell(row, indexes.BuyerSubtype))
                    .Where(static subtype => !string.IsNullOrWhiteSpace(subtype))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var row in group)
                {
                    var product = Cell(row, indexes.ProductCanonical);
                    if (!acceptedByProduct.ContainsKey(product))
                    {
                        continue;
                    }

                    acceptedByProduct[product] += ParseDecimal(Cell(row, indexes.Accepted));
                    dispatchedByProduct[product] += ParseDecimal(Cell(row, indexes.Dispatched));
                }

                return new StationRow(
                    group.Key.Code,
                    group.Key.Name,
                    group.Key.Department,
                    group.Key.Municipality,
                    group.Key.BorderFlag,
                    string.Join(" | ", subtypes),
                    acceptedByProduct,
                    dispatchedByProduct,
                    Decile: 0);
            })
            .ToList();
    }

    private static IReadOnlyList<StationRow> AssignDeciles(IReadOnlyList<StationRow> rows)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        return rows
            .Select((row, index) => row with { Decile = (index * 10 / rows.Count) + 1 })
            .ToList();
    }

    private static IReadOnlyList<string> StationToCsvRow(StationRow row)
    {
        var csvRow = new List<string>
        {
            row.Code,
            row.Name,
            row.Department,
            row.Municipality,
            row.BorderFlag,
            row.BuyerSubtypes
        };

        foreach (var product in CanonicalProductOrder)
        {
            var accepted = row.AcceptedByProduct[product];
            var dispatched = row.DispatchedByProduct[product];
            csvRow.Add(FormatDecimal(accepted));
            csvRow.Add(FormatDecimal(dispatched));
            csvRow.Add(FormatDecimal(accepted / WindowMonthCount));
            csvRow.Add(FormatDecimal(dispatched / WindowMonthCount));
        }

        csvRow.Add(FormatDecimal(row.AcceptedTotal));
        csvRow.Add(FormatDecimal(row.DispatchedTotal));
        csvRow.Add(FormatDecimal(row.GalMonthAcceptedTotal));
        csvRow.Add(FormatDecimal(row.GalMonthDispatchedTotal));
        csvRow.Add(row.Decile.ToString(CultureInfo.InvariantCulture));
        return csvRow;
    }

    private static IReadOnlyList<string> StationHeaders()
    {
        var headers = new List<string>
        {
            "CODIGO_SICOM_COMPRADOR",
            "NOMBRE_COMERCIAL_COMPRADOR",
            "DEPARTAMENTO",
            "MUNICIPIO",
            "ZONA_FRONTERA",
            "SUBTIPOS_COMPRADOR"
        };

        foreach (var product in CanonicalProductOrder.Select(static product => product.ToUpperInvariant()))
        {
            headers.Add($"VOLUMEN_ACEPTADO_{product}_12M");
            headers.Add($"VOLUMEN_DESPACHADO_{product}_12M");
            headers.Add($"GAL_MES_ACEPTADO_{product}");
            headers.Add($"GAL_MES_DESPACHADO_{product}");
        }

        headers.Add("VOLUMEN_ACEPTADO_TOTAL_12M");
        headers.Add("VOLUMEN_DESPACHADO_TOTAL_12M");
        headers.Add("GAL_MES_ACEPTADO_TOTAL");
        headers.Add("GAL_MES_DESPACHADO_TOTAL");
        headers.Add("DECIL_ACEPTADO_TOTAL");
        return headers;
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

    private static IReadOnlyList<string> BuyerSubtypeUniqueNames(EdsBuyerScope buyerScope)
    {
        return buyerScope switch
        {
            EdsBuyerScope.Eds => [
                "ESTACION DE SERVICIO AUTOMOTRIZ"
            ],
            EdsBuyerScope.EdsFluvial => [
                "ESTACION DE SERVICIO AUTOMOTRIZ",
                "ESTACION DE SERVICIO FLUVIAL"
            ],
            EdsBuyerScope.EdsFluvialIndustrial => [
                "ESTACION DE SERVICIO AUTOMOTRIZ",
                "ESTACION DE SERVICIO FLUVIAL",
                "COMERCIALIZADOR INDUSTRIAL"
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(buyerScope), buyerScope, null)
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

    private static string BuyerScopeArgument(EdsBuyerScope buyerScope)
    {
        return buyerScope switch
        {
            EdsBuyerScope.Eds => "eds",
            EdsBuyerScope.EdsFluvial => "eds_fluvial",
            EdsBuyerScope.EdsFluvialIndustrial => "eds_fluvial_industrial",
            _ => throw new ArgumentOutOfRangeException(nameof(buyerScope), buyerScope, null)
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

        return normalized.ToLowerInvariant();
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

        return borderZone.Trim();
    }

    private static string BuildPeriod(int? year, int? month)
    {
        return year is null || month is null ? "" : $"{year.Value:0000}-{month.Value:00}";
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

    private static decimal Average(IReadOnlyList<decimal> values)
    {
        return values.Count == 0 ? 0 : values.Sum() / values.Count;
    }

    private static decimal StandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count <= 1)
        {
            return 0;
        }

        var average = (double)Average(values);
        var variance = values
            .Select(value => Math.Pow((double)value - average, 2))
            .Sum() / (values.Count - 1);
        return (decimal)Math.Sqrt(variance);
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static string NormalizeInvariant(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    private sealed record BaseIndexes(
        int Code,
        int Name,
        int Department,
        int Municipality,
        int BorderFlag,
        int BuyerSubtype,
        int ProductCanonical,
        int Accepted,
        int Dispatched);

    private sealed record StationKey(
        string Code,
        string Name,
        string Department,
        string Municipality,
        string BorderFlag);

    private sealed record StationRow(
        string Code,
        string Name,
        string Department,
        string Municipality,
        string BorderFlag,
        string BuyerSubtypes,
        IReadOnlyDictionary<string, decimal> AcceptedByProduct,
        IReadOnlyDictionary<string, decimal> DispatchedByProduct,
        int Decile)
    {
        public decimal AcceptedTotal => AcceptedByProduct.Values.Sum();
        public decimal DispatchedTotal => DispatchedByProduct.Values.Sum();
        public decimal GalMonthAcceptedTotal => AcceptedTotal / WindowMonthCount;
        public decimal GalMonthDispatchedTotal => DispatchedTotal / WindowMonthCount;
    }
}
