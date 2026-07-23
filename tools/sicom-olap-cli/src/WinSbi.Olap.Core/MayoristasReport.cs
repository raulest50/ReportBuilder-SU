using System.Globalization;
using System.Text;

namespace WinSbi.Olap.Core;

public static class MayoristasReport
{
    public const string CatalogName = "SBI-Ordenes-Pedidos";
    public const string CubeName = "Ordenes-Pedidos";

    public static MayoristasReportOptions CreateOptions(
        int year,
        int? quarter,
        int? semester,
        bool annual,
        string? months,
        string measure,
        string product)
    {
        var period = ReportPeriods.Resolve(year, quarter, semester, annual, months);

        return new MayoristasReportOptions(
            period.Year,
            period.Months,
            period.Label,
            period.CommandPart,
            ParseMeasure(measure),
            ParseProduct(product));
    }

    public static MayoristasReportOptions CreateOptions(
        int year,
        int? quarter,
        int? semester,
        string? months,
        string measure,
        string product)
    {
        return CreateOptions(year, quarter, semester, annual: false, months, measure, product);
    }

    public static MayoristasReportOptions CreateOptions(
        int year,
        int? quarter,
        string? months,
        string measure,
        string product)
    {
        return CreateOptions(year, quarter, null, annual: false, months, measure, product);
    }

    public static IReadOnlyList<int> MonthsForQuarter(int quarter)
    {
        return ReportPeriods.MonthsForQuarter(quarter);
    }

    public static IReadOnlyList<int> MonthsForSemester(int semester)
    {
        return ReportPeriods.MonthsForSemester(semester);
    }

    public static IReadOnlyList<int> ParseMonths(string months)
    {
        return ReportPeriods.ParseMonths(months);
    }

    public static MayoristasMeasureSelection ParseMeasure(string measure)
    {
        return measure.Trim().ToLowerInvariant() switch
        {
            "accepted" or "aceptado" => MayoristasMeasureSelection.Accepted,
            "dispatched" or "despachado" => MayoristasMeasureSelection.Dispatched,
            "both" or "ambos" => MayoristasMeasureSelection.Both,
            _ => throw new ArgumentException("--measure must be accepted, dispatched, or both.")
        };
    }

    public static MayoristasProductSelection ParseProduct(string product)
    {
        return product.Trim().ToLowerInvariant() switch
        {
            "all" or "todos" => MayoristasProductSelection.All,
            "corriente" => MayoristasProductSelection.Corriente,
            "extra" => MayoristasProductSelection.Extra,
            "diesel" or "acpm" or "biodiesel" => MayoristasProductSelection.Diesel,
            _ => throw new ArgumentException("--product must be all, corriente, extra, or diesel.")
        };
    }

    public static string DefaultOutputDirectory(MayoristasReportOptions options)
    {
        return Path.Combine("reports", CatalogName, "mayoristas", options.PeriodLabel);
    }

    public static string BuildMdx(MayoristasReportOptions options)
    {
        var monthMembers = string.Join($",{Environment.NewLine}", options.Months.Select(static month =>
            $"    [MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[{month}]"));
        var productMembers = string.Join($",{Environment.NewLine}", ProductUniqueNames(options.Product).Select(static product =>
            $"    [PRODUCTO].[DESCRIPCION PRODUCTO].&[{product}]"));
        var measureMembers = string.Join($",{Environment.NewLine}", MeasureUniqueNames(options.Measure).Select(static measure =>
            $"    {measure}"));

        return $$"""
WITH
SET [target_months] AS {
{{monthMembers}}
}
SET [target_buyers] AS {
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ],
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO FLUVIAL],
    [COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[COMERCIALIZADOR INDUSTRIAL]
}
SET [target_products] AS {
{{productMembers}}
}
SET [target_measures] AS {
{{measureMembers}}
}
SET [rows] AS
    NONEMPTY(
        [PROVEEDOR].[NOMBRE COMERCIAL PROVEEDOR].[NOMBRE COMERCIAL PROVEEDOR].Members
        * [target_months]
        * { [MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[{{options.Year}}] }
        * [target_products]
        * [target_buyers],
        [target_measures]
    )
SELECT
    [target_measures] ON COLUMNS,
    [rows] ON ROWS
FROM [{{CubeName}}]
WHERE ([PROVEEDOR].[TIPO AGENTE PROVEEDOR].&[DISTRIBUIDOR MAYORISTA])
""";
    }

    public static async Task<MayoristasReportResult> GenerateAsync(
        IXmlaClient client,
        MayoristasReportOptions options,
        string outputDirectory,
        string equivalentCommand,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var mdx = BuildMdx(options);
        var result = await client.ExecuteMdxAsync(
            CatalogName,
            mdx,
            debugOptions: debugOptions,
            cancellationToken: cancellationToken);

        var detailTable = NormalizeDetailTable(MdxTableBuilder.BuildTable(result), options);
        var providerSummary = BuildProviderSummary(detailTable, options);
        var productSummary = options.Product == MayoristasProductSelection.All
            ? BuildProductSummary(detailTable, options)
            : null;

        Directory.CreateDirectory(outputDirectory);
        var detailPath = Path.Combine(outputDirectory, "mayoristas-detalle.csv");
        var providerSummaryPath = Path.Combine(outputDirectory, "mayoristas-resumen.csv");
        var productSummaryPath = productSummary is null ? null : Path.Combine(outputDirectory, "mayoristas-productos.csv");

        await File.WriteAllTextAsync(detailPath, OutputFormatters.ToCsv(detailTable), cancellationToken);
        await File.WriteAllTextAsync(providerSummaryPath, OutputFormatters.ToCsv(providerSummary), cancellationToken);
        if (productSummary is not null && productSummaryPath is not null)
        {
            await File.WriteAllTextAsync(productSummaryPath, OutputFormatters.ToCsv(productSummary), cancellationToken);
        }

        return new MayoristasReportResult(
            options,
            outputDirectory,
            detailPath,
            providerSummaryPath,
            productSummaryPath,
            detailTable,
            providerSummary,
            productSummary,
            equivalentCommand);
    }

    public static MdxTable NormalizeDetailTable(MdxTable table, MayoristasReportOptions options)
    {
        var measureHeaders = MeasureHeaders(options.Measure);
        var headers = new List<string>
        {
            "NOMBRE",
            "MES DESPACHO",
            "AÑO DESPACHO",
            "PRODUCTO",
            "SUBTIPO AGENTE"
        };
        headers.AddRange(measureHeaders);

        var rows = table.Rows
            .Select(row =>
            {
                var normalized = new List<string>();
                for (var index = 0; index < 5; index++)
                {
                    normalized.Add(index < row.Count ? row[index] : "");
                }

                for (var index = 0; index < measureHeaders.Count; index++)
                {
                    var sourceIndex = 5 + index;
                    normalized.Add(sourceIndex < row.Count ? row[sourceIndex] : "");
                }

                return normalized;
            })
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(headers, rows, table.Note);
    }

    public static MdxTable BuildProviderSummary(MdxTable detailTable, MayoristasReportOptions options)
    {
        return BuildSummary(detailTable, "NOMBRE", options);
    }

    public static MdxTable BuildProductSummary(MdxTable detailTable, MayoristasReportOptions options)
    {
        return BuildSummary(detailTable, "PRODUCTO", options);
    }

    public static string BuildEquivalentCommand(MayoristasReportOptions options, string outputDirectory)
    {
        return string.Join(" ", [
            "dotnet run -- report mayoristas",
            $"--year {options.Year}",
            options.PeriodCommandPart,
            $"--measure {MeasureArgument(options.Measure)}",
            $"--product {ProductArgument(options.Product)}",
            $"--output-dir \"{outputDirectory}\""
        ]);
    }

    private static MdxTable BuildSummary(MdxTable detailTable, string groupHeader, MayoristasReportOptions options)
    {
        var groupIndex = HeaderIndex(detailTable, groupHeader);
        var measureHeaders = MeasureHeaders(options.Measure);
        var measureIndexes = measureHeaders.Select(header => HeaderIndex(detailTable, header)).ToList();

        var summaries = detailTable.Rows
            .GroupBy(row => Cell(row, groupIndex), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var totals = measureIndexes
                    .Select(index => group.Sum(row => ParseDecimal(Cell(row, index))))
                    .ToList();
                return new SummaryRow(group.Key, totals);
            })
            .OrderByDescending(row => row.Totals.Count == 0 ? 0 : row.Totals[0])
            .ToList();

        var grandTotals = measureIndexes
            .Select(index => detailTable.Rows.Sum(row => ParseDecimal(Cell(row, index))))
            .ToList();

        var headers = new List<string> { groupHeader };
        foreach (var measureHeader in measureHeaders)
        {
            headers.Add(measureHeader);
            headers.Add(ParticipationHeader(measureHeader));
        }

        var rows = summaries
            .Select(summary =>
            {
                var row = new List<string> { summary.Name };
                for (var index = 0; index < summary.Totals.Count; index++)
                {
                    row.Add(FormatDecimal(summary.Totals[index]));
                    var participation = grandTotals[index] == 0 ? 0 : summary.Totals[index] * 100 / grandTotals[index];
                    row.Add(FormatPercentage(participation));
                }

                return row;
            })
            .Cast<IReadOnlyList<string>>()
            .ToList();

        return new MdxTable(headers, rows, null);
    }

    private static IReadOnlyList<string> MeasureHeaders(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => ["VOLUMEN"],
            MayoristasMeasureSelection.Dispatched => ["VOLUMEN"],
            MayoristasMeasureSelection.Both => ["VOLUMEN ACEPTADO", "VOLUMEN DESPACHADO"],
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
        };
    }

    private static IReadOnlyList<string> MeasureUniqueNames(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => ["[Measures].[VOLUMEN ACEPTADO]"],
            MayoristasMeasureSelection.Dispatched => ["[Measures].[VOLUMEN DESPACHADO]"],
            MayoristasMeasureSelection.Both => ["[Measures].[VOLUMEN ACEPTADO]", "[Measures].[VOLUMEN DESPACHADO]"],
            _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, null)
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

    private static bool IsQuarterPeriod(MayoristasReportOptions options, out int quarter)
    {
        var months = options.Months;
        if (months.SequenceEqual([1, 2, 3])) { quarter = 1; return true; }
        if (months.SequenceEqual([4, 5, 6])) { quarter = 2; return true; }
        if (months.SequenceEqual([7, 8, 9])) { quarter = 3; return true; }
        if (months.SequenceEqual([10, 11, 12])) { quarter = 4; return true; }
        quarter = 0;
        return false;
    }

    private static string MeasureArgument(MayoristasMeasureSelection measure)
    {
        return measure switch
        {
            MayoristasMeasureSelection.Accepted => "accepted",
            MayoristasMeasureSelection.Dispatched => "dispatched",
            MayoristasMeasureSelection.Both => "both",
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

    private static string ParticipationHeader(string measureHeader)
    {
        return measureHeader == "VOLUMEN"
            ? "PARTICIPACION"
            : $"PARTICIPACION {measureHeader.Replace("VOLUMEN ", "", StringComparison.OrdinalIgnoreCase)}";
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

    private static string FormatPercentage(decimal value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private sealed record SummaryRow(string Name, IReadOnlyList<decimal> Totals);
}
