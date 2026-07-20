using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class MayoristasReportTests
{
    [Fact]
    public void BuildsMonthsForQuarter()
    {
        Assert.Equal([4, 5, 6], MayoristasReport.MonthsForQuarter(2));
    }

    [Fact]
    public void ParsesManualMonthsSortedAndDistinct()
    {
        Assert.Equal([1, 3, 5], MayoristasReport.ParseMonths("5, 1,3,3"));
    }

    [Fact]
    public void CreatesDefaultQuarterOutputDirectory()
    {
        var options = MayoristasReport.CreateOptions(2026, 2, null, "accepted", "all");

        Assert.Equal(Path.Combine("reports", "SBI-Ordenes-Pedidos", "mayoristas", "2026-Q2"), MayoristasReport.DefaultOutputDirectory(options));
    }

    [Fact]
    public void GeneratesAcceptedMdxForDiesel()
    {
        var options = MayoristasReport.CreateOptions(2026, 2, null, "accepted", "diesel");
        var mdx = MayoristasReport.BuildMdx(options);

        Assert.Contains("[Measures].[VOLUMEN ACEPTADO]", mdx);
        Assert.DoesNotContain("[Measures].[VOLUMEN DESPACHADO]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[BIODIESEL CON MEZCLA]", mdx);
        Assert.DoesNotContain("GASOLINA MOTOR CORRIENTE", mdx);
    }

    [Fact]
    public void GeneratesBothMeasuresMdxForAllProducts()
    {
        var options = MayoristasReport.CreateOptions(2026, null, "4,5,6", "both", "all");
        var mdx = MayoristasReport.BuildMdx(options);

        Assert.Contains("[Measures].[VOLUMEN ACEPTADO]", mdx);
        Assert.Contains("[Measures].[VOLUMEN DESPACHADO]", mdx);
        Assert.Contains("GASOLINA MOTOR CORRIENTE", mdx);
        Assert.Contains("GASOLINA MOTOR EXTRA", mdx);
        Assert.Contains("BIODIESEL CON MEZCLA", mdx);
    }

    [Fact]
    public void BuildsProviderSummaryWithParticipation()
    {
        var options = MayoristasReport.CreateOptions(2026, 2, null, "accepted", "corriente");
        var detail = new MdxTable(
            ["NOMBRE", "MES DESPACHO", "AÑO DESPACHO", "PRODUCTO", "SUBTIPO AGENTE", "VOLUMEN"],
            [
                ["A", "4", "2026", "GASOLINA MOTOR CORRIENTE", "ESTACION DE SERVICIO AUTOMOTRIZ", "30"],
                ["B", "4", "2026", "GASOLINA MOTOR CORRIENTE", "ESTACION DE SERVICIO AUTOMOTRIZ", "10"]
            ],
            null);

        var summary = MayoristasReport.BuildProviderSummary(detail, options);

        Assert.Equal(["NOMBRE", "VOLUMEN", "PARTICIPACION"], summary.Headers);
        Assert.Equal(["A", "30", "75"], summary.Rows[0]);
        Assert.Equal(["B", "10", "25"], summary.Rows[1]);
    }

    [Fact]
    public void BuildsProductSummaryForAllProducts()
    {
        var options = MayoristasReport.CreateOptions(2026, 2, null, "accepted", "all");
        var detail = new MdxTable(
            ["NOMBRE", "MES DESPACHO", "AÑO DESPACHO", "PRODUCTO", "SUBTIPO AGENTE", "VOLUMEN"],
            [
                ["A", "4", "2026", "GASOLINA MOTOR CORRIENTE", "ESTACION DE SERVICIO AUTOMOTRIZ", "60"],
                ["A", "4", "2026", "BIODIESEL CON MEZCLA", "ESTACION DE SERVICIO AUTOMOTRIZ", "40"]
            ],
            null);

        var summary = MayoristasReport.BuildProductSummary(detail, options);

        Assert.Equal(["PRODUCTO", "VOLUMEN", "PARTICIPACION"], summary.Headers);
        Assert.Equal(["GASOLINA MOTOR CORRIENTE", "60", "60"], summary.Rows[0]);
        Assert.Equal(["BIODIESEL CON MEZCLA", "40", "40"], summary.Rows[1]);
    }
}
