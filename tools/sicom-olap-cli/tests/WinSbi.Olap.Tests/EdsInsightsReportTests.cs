using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class EdsInsightsReportTests
{
    [Fact]
    public void CreatesDefaultQuarterOutputDirectory()
    {
        var options = EdsInsightsReport.CreateOptions(2026, 2, null, "all", 7, 20);

        Assert.Equal(Path.Combine("reports", "SBI-Ordenes-Pedidos", "eds_insights", "2026-Q2"), EdsInsightsReport.DefaultOutputDirectory(options));
    }

    [Fact]
    public void RejectsInvalidTopCounts()
    {
        Assert.Throws<ArgumentException>(() => EdsInsightsReport.CreateOptions(2026, 2, null, "all", 0, 20));
        Assert.Throws<ArgumentException>(() => EdsInsightsReport.CreateOptions(2026, 2, null, "all", 7, 0));
    }

    [Fact]
    public void GeneratesActiveFlagsMdxFromAgentsCube()
    {
        var mdx = EdsInsightsReport.BuildActiveFlagsNationalMdx();

        Assert.Contains("[BANDERA].[BANDERA]", mdx);
        Assert.Contains("[Measures].[AGENTE CANTIDAD]", mdx);
        Assert.Contains("[Measures].[REGISTROS CANTIDAD]", mdx);
        Assert.Contains("[AGENTE].[NUEVO ESTADO].&[ACTIVO]", mdx);
        Assert.Contains("[SUBTIPO AGENTE].[SUBTIPO AGENTE].&[ESTACION DE SERVICIO AUTOMOTRIZ]", mdx);
        Assert.Contains("FROM [Agentes]", mdx);
    }

    [Fact]
    public void GeneratesTopCitiesMdxWithAcceptedRankingAndProducts()
    {
        var options = EdsInsightsReport.CreateOptions(2026, 2, null, "all", 7, 20);
        var mdx = EdsInsightsReport.BuildTopCitiesMdx(options);

        Assert.Contains("TOPCOUNT", mdx);
        Assert.Contains("[Measures].[VOLUMEN ACEPTADO PERIODO]", mdx);
        Assert.Contains("[Measures].[VOLUMEN DESPACHADO PERIODO]", mdx);
        Assert.Contains("[COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR]", mdx);
        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[4]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[GASOLINA MOTOR CORRIENTE]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[GASOLINA MOTOR EXTRA]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[BIODIESEL CON MEZCLA]", mdx);
    }

    [Fact]
    public void GeneratesBorderMdxForCurrentAndPreviousYear()
    {
        var options = EdsInsightsReport.CreateOptions(2026, null, "4,5,6", "diesel", 7, 20);
        var mdx = EdsInsightsReport.BuildBorderVolumesMdx(options);

        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[2025]", mdx);
        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[AÑO DESPACHO].&[2026]", mdx);
        Assert.Contains("[COMPRADOR].[ZONA FRONTERA]", mdx);
        Assert.Contains("[Measures].[VOLUMEN ACEPTADO PERIODO]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[BIODIESEL CON MEZCLA]", mdx);
        Assert.DoesNotContain("GASOLINA MOTOR CORRIENTE", mdx);
    }

    [Fact]
    public void NormalizesNationalFlagsWithParticipation()
    {
        var source = new MdxTable(
            ["BANDERA", "AGENTE CANTIDAD", "REGISTROS CANTIDAD"],
            [
                ["TERPEL", "30", "31"],
                ["PRIMAX", "10", "11"]
            ],
            null);

        var table = EdsInsightsReport.NormalizeNationalFlagsTable(source);

        Assert.Equal(["BANDERA", "EDS_ACTIVAS", "REGISTROS_CANTIDAD", "PARTICIPACION_EDS_NACIONAL_PCT"], table.Headers);
        Assert.Equal(["TERPEL", "30", "31", "75"], table.Rows[0]);
        Assert.Equal(["PRIMAX", "10", "11", "25"], table.Rows[1]);
    }

    [Fact]
    public void BuildsTopCitiesTableWithNationalParticipation()
    {
        var cities = new[]
        {
            new EdsInsightsReport.CityRow(1, "11", "BOGOTA D.C.", "11001", "BOGOTA", 80, 78),
            new EdsInsightsReport.CityRow(2, "05", "ANTIOQUIA", "05001", "MEDELLIN", 20, 19)
        };

        var table = EdsInsightsReport.BuildTopCitiesTable(cities, 100);

        Assert.Equal("80", table.Rows[0][6]);
        Assert.Equal("20", table.Rows[1][6]);
    }

    [Fact]
    public void BuildsTopEdsTableWithFlagAndDominantSupplier()
    {
        var eds = new[]
        {
            new EdsInsightsReport.TopEdsRow(1, "123", "EDS UNO", "BOGOTA", "BOGOTA D.C.", "NO", 100, 90)
        };
        var flags = new Dictionary<string, string> { ["123"] = "TERPEL" };
        var suppliers = new Dictionary<string, EdsInsightsReport.DominantSupplier>
        {
            ["123"] = new("PROVEEDOR A", "PLANTA A", 60, 100)
        };

        var table = EdsInsightsReport.BuildTopEdsTable(eds, flags, suppliers, 200);

        Assert.Equal("TERPEL", table.Rows[0][6]);
        Assert.Equal("50", table.Rows[0][9]);
        Assert.Equal("PROVEEDOR A", table.Rows[0][10]);
        Assert.Equal("PLANTA A", table.Rows[0][11]);
        Assert.Equal("60", table.Rows[0][12]);
    }

    [Fact]
    public void BuildsBorderSummaryWithGalMonthPerEdsAndYoy()
    {
        var options = EdsInsightsReport.CreateOptions(2026, 2, null, "corriente", 7, 20);
        var volumes = new[]
        {
            new EdsInsightsReport.BorderVolumeRow("SI", 2025, "GASOLINA MOTOR CORRIENTE", "corriente", 50, 48),
            new EdsInsightsReport.BorderVolumeRow("SI", 2026, "GASOLINA MOTOR CORRIENTE", "corriente", 100, 98),
            new EdsInsightsReport.BorderVolumeRow("NO", 2026, "GASOLINA MOTOR CORRIENTE", "corriente", 300, 295)
        };
        var active = new[]
        {
            new EdsInsightsReport.BorderActiveRow("SI", 2),
            new EdsInsightsReport.BorderActiveRow("NO", 6)
        };

        var table = EdsInsightsReport.BuildBorderSummaryTable(volumes, active, options);

        Assert.Equal("SI", table.Rows[0][0]);
        Assert.Equal("25", table.Rows[0][2]);
        Assert.Equal("16.6666666667", table.Rows[0][6]);
        Assert.Equal("100", table.Rows[0][8]);
        Assert.Equal("TOTAL NACIONAL", table.Rows[2][0]);
    }

    [Fact]
    public void BuildsBorderProductsParticipation()
    {
        var options = EdsInsightsReport.CreateOptions(2026, 2, null, "all", 7, 20);
        var volumes = new[]
        {
            new EdsInsightsReport.BorderVolumeRow("SI", 2026, "GASOLINA MOTOR CORRIENTE", "corriente", 40, 39),
            new EdsInsightsReport.BorderVolumeRow("NO", 2026, "GASOLINA MOTOR CORRIENTE", "corriente", 60, 59),
            new EdsInsightsReport.BorderVolumeRow("SI", 2026, "BIODIESEL CON MEZCLA", "diesel", 10, 9),
            new EdsInsightsReport.BorderVolumeRow("NO", 2026, "BIODIESEL CON MEZCLA", "diesel", 90, 89)
        };

        var table = EdsInsightsReport.BuildBorderProductsTable(volumes, options);

        var siCorriente = table.Rows.Single(row => row[0] == "SI" && row[2] == "corriente");
        Assert.Equal("80", siCorriente[5]);
        Assert.Equal("40", siCorriente[6]);
    }

    [Fact]
    public void BuildsEquivalentCommand()
    {
        var options = EdsInsightsReport.CreateOptions(2026, 2, null, "all", 7, 20);

        var command = EdsInsightsReport.BuildEquivalentCommand(options, @"reports\SBI-Ordenes-Pedidos\eds_insights\2026-Q2");

        Assert.Contains("dotnet run -- report eds_insights", command);
        Assert.Contains("--year 2026", command);
        Assert.Contains("--quarter 2", command);
        Assert.Contains("--top-cities 7", command);
        Assert.Contains("--top-eds 20", command);
    }
}
