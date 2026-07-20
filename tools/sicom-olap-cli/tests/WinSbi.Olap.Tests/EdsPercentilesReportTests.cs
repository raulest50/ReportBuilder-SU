using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class EdsPercentilesReportTests
{
    [Fact]
    public void BuildsRollingTwelveMonthWindowAcrossYears()
    {
        var options = EdsPercentilesReport.CreateOptions(2026, 6, "all");

        Assert.Equal(
            ["2025-07", "2025-08", "2025-09", "2025-10", "2025-11", "2025-12", "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06"],
            options.WindowMonths.Select(static month => $"{month.Year:0000}-{month.Month:00}").ToList());
        Assert.Equal("2026-06", options.PeriodLabel);
        Assert.Equal(Path.Combine("reports", "SBI-Ordenes-Pedidos", "eds_percentiles", "2026-06"), EdsPercentilesReport.DefaultOutputDirectory(options));
    }

    [Fact]
    public void RejectsInvalidEndMonth()
    {
        Assert.Throws<ArgumentException>(() => EdsPercentilesReport.CreateOptions(2026, 0, "all"));
        Assert.Throws<ArgumentException>(() => EdsPercentilesReport.CreateOptions(2026, 13, "all"));
    }

    [Fact]
    public void BuildsMdxWithAutomotiveStationsProductsWindowAndBothMeasures()
    {
        var options = EdsPercentilesReport.CreateOptions(2026, 6, "all");

        var mdx = EdsPercentilesReport.BuildMdx(options);

        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]", mdx);
        Assert.DoesNotContain("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO FLUVIAL]", mdx);
        Assert.DoesNotContain("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[COMERCIALIZADOR INDUSTRIAL]", mdx);
        Assert.Contains("[COMPRADOR].[CODIGO SICOM COMPRADOR]", mdx);
        Assert.Contains("[COMPRADOR].[NOMBRE COMERCIAL COMPRADOR]", mdx);
        Assert.Contains("[target_buyer_subtypes]", mdx);
        Assert.Contains("[Measures].[VOLUMEN ACEPTADO]", mdx);
        Assert.Contains("[Measures].[VOLUMEN DESPACHADO]", mdx);
        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[2025]", mdx);
        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[7]", mdx);
        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[2026]", mdx);
        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[6]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[GASOLINA MOTOR CORRIENTE]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[GASOLINA MOTOR EXTRA]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[BIODIESEL CON MEZCLA]", mdx);
    }

    [Fact]
    public void BuildsMdxForEdsAndFluvialBuyerScope()
    {
        var options = EdsPercentilesReport.CreateOptions(2026, 6, "all", "eds_fluvial");

        var mdx = EdsPercentilesReport.BuildMdx(options);

        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]", mdx);
        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO FLUVIAL]", mdx);
        Assert.DoesNotContain("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[COMERCIALIZADOR INDUSTRIAL]", mdx);
    }

    [Fact]
    public void BuildsMdxForEdsFluvialAndIndustrialBuyerScope()
    {
        var options = EdsPercentilesReport.CreateOptions(2026, 6, "all", "eds_fluvial_industrial");

        var mdx = EdsPercentilesReport.BuildMdx(options);

        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]", mdx);
        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO FLUVIAL]", mdx);
        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[COMERCIALIZADOR INDUSTRIAL]", mdx);
    }

    [Fact]
    public void RejectsInvalidBuyerScope()
    {
        var exception = Assert.Throws<ArgumentException>(() => EdsPercentilesReport.CreateOptions(2026, 6, "all", "otro"));

        Assert.Contains("--buyer-scope", exception.Message);
    }

    [Fact]
    public void NormalizesBaseTableWithCanonicalProductAndPeriod()
    {
        var source = new MdxTable(
            ["Row 1", "Row 2", "Row 3", "Row 4", "Row 5", "Row 6", "Row 7", "Row 8", "Row 9", "VOLUMEN ACEPTADO", "VOLUMEN DESPACHADO"],
            [[
                "123",
                "EDS UNO",
                "BOGOTA D.C.",
                "BOGOTA",
                "SI",
                "ESTACION DE SERVICIO AUTOMOTRIZ",
                "2026",
                "6",
                "BIODIESEL CON MEZCLA",
                "120",
                "110"
            ]],
            null);

        var normalized = EdsPercentilesReport.NormalizeBaseTable(source);

        Assert.Equal(
            ["CODIGO_SICOM_COMPRADOR", "NOMBRE_COMERCIAL_COMPRADOR", "DEPARTAMENTO", "MUNICIPIO", "ZONA_FRONTERA", "SUBTIPO_AGENTE_COMPRADOR", "ANO", "MES", "PERIODO", "PRODUCTO", "PRODUCTO_CANONICO", "VOLUMEN_ACEPTADO", "VOLUMEN_DESPACHADO"],
            normalized.Headers);
        Assert.Equal(["123", "EDS UNO", "BOGOTA D.C.", "BOGOTA", "SI", "ESTACION DE SERVICIO AUTOMOTRIZ", "2026", "6", "2026-06", "BIODIESEL CON MEZCLA", "diesel", "120", "110"], normalized.Rows[0]);
    }

    [Fact]
    public void BuildsStationsWithMissingMonthsAsZeroAndKeepsMeasuresSeparated()
    {
        var baseTable = BuildBaseTable([
            Row("A", "EDS A", "BOGOTA", "BOGOTA", "NO", "2026", "6", "GASOLINA MOTOR CORRIENTE", "corriente", "120", "60")
        ]);

        var stations = EdsPercentilesReport.BuildStationsTable(baseTable);
        var row = stations.Rows.Single();

        Assert.Equal("120", Cell(stations, row, "VOLUMEN_ACEPTADO_CORRIENTE_12M"));
        Assert.Equal("60", Cell(stations, row, "VOLUMEN_DESPACHADO_CORRIENTE_12M"));
        Assert.Equal("ESTACION DE SERVICIO AUTOMOTRIZ", Cell(stations, row, "SUBTIPOS_COMPRADOR"));
        Assert.Equal("10", Cell(stations, row, "GAL_MES_ACEPTADO_CORRIENTE"));
        Assert.Equal("5", Cell(stations, row, "GAL_MES_DESPACHADO_CORRIENTE"));
        Assert.Equal("120", Cell(stations, row, "VOLUMEN_ACEPTADO_TOTAL_12M"));
        Assert.Equal("60", Cell(stations, row, "VOLUMEN_DESPACHADO_TOTAL_12M"));
        Assert.Equal("10", Cell(stations, row, "GAL_MES_ACEPTADO_TOTAL"));
        Assert.Equal("5", Cell(stations, row, "GAL_MES_DESPACHADO_TOTAL"));
    }

    [Fact]
    public void AssignsStableAcceptedDecilesAndBuildsSummary()
    {
        var rows = Enumerable.Range(1, 10)
            .Select(index => Row(
                index.ToString("000"),
                $"EDS {index:000}",
                "BOGOTA",
                "BOGOTA",
                "NO",
                "2026",
                "6",
                "GASOLINA MOTOR CORRIENTE",
                "corriente",
                (index * 120).ToString(),
                (index * 60).ToString()))
            .ToList();
        var baseTable = BuildBaseTable(rows);

        var stations = EdsPercentilesReport.BuildStationsTable(baseTable);
        var summary = EdsPercentilesReport.BuildSummaryTable(stations);

        Assert.Equal("1", Cell(stations, stations.Rows[0], "DECIL_ACEPTADO_TOTAL"));
        Assert.Equal("10", Cell(stations, stations.Rows[^1], "DECIL_ACEPTADO_TOTAL"));
        Assert.Equal("1", summary.Rows[0][0]);
        Assert.Equal("1", summary.Rows[0][1]);
        Assert.Equal("10", summary.Rows[0][2]);
        Assert.Equal("0", summary.Rows[0][3]);
        Assert.Equal("10", summary.Rows[0][4]);
        Assert.Equal("10", summary.Rows[0][5]);
        Assert.Equal("5", summary.Rows[0][6]);
    }

    [Fact]
    public void BuildsEquivalentCommand()
    {
        var options = EdsPercentilesReport.CreateOptions(2026, 6, "diesel", "eds_fluvial");

        var command = EdsPercentilesReport.BuildEquivalentCommand(options, @"reports\SBI-Ordenes-Pedidos\eds_percentiles\2026-06");

        Assert.Contains("dotnet run -- report eds_percentiles", command);
        Assert.Contains("--end-year 2026", command);
        Assert.Contains("--end-month 6", command);
        Assert.Contains("--product diesel", command);
        Assert.Contains("--buyer-scope eds_fluvial", command);
    }

    private static MdxTable BuildBaseTable(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        return new MdxTable(
            ["CODIGO_SICOM_COMPRADOR", "NOMBRE_COMERCIAL_COMPRADOR", "DEPARTAMENTO", "MUNICIPIO", "ZONA_FRONTERA", "SUBTIPO_AGENTE_COMPRADOR", "ANO", "MES", "PERIODO", "PRODUCTO", "PRODUCTO_CANONICO", "VOLUMEN_ACEPTADO", "VOLUMEN_DESPACHADO"],
            rows,
            null);
    }

    private static IReadOnlyList<string> Row(
        string code,
        string name,
        string department,
        string municipality,
        string borderFlag,
        string year,
        string month,
        string product,
        string productCanonical,
        string accepted,
        string dispatched)
    {
        return [code, name, department, municipality, borderFlag, "ESTACION DE SERVICIO AUTOMOTRIZ", year, month, $"{year}-{int.Parse(month):00}", product, productCanonical, accepted, dispatched];
    }

    private static string Cell(MdxTable table, IReadOnlyList<string> row, string header)
    {
        var index = table.Headers.ToList().FindIndex(value => string.Equals(value, header, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? "" : row[index];
    }
}
