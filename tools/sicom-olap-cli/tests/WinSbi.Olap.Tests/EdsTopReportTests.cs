using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class EdsTopReportTests
{
    [Fact]
    public void BuildsDispatchedTopMdxForQuarter()
    {
        var options = EdsTopReport.CreateOptions(2026, 2, null, null, "dispatched", "all", 20);

        var mdx = EdsTopReport.BuildTopMdx(options);

        Assert.Contains("TOPCOUNT", mdx);
        Assert.Contains("[Measures].[VOLUMEN DESPACHADO PERIODO]", mdx);
        Assert.Contains("[MOVIMIENTOS ORDEN PEDIDO].[MES DESPACHO].&[4]", mdx);
        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]", mdx);
        Assert.Contains("GASOLINA MOTOR CORRIENTE", mdx);
        Assert.Contains("GASOLINA MOTOR EXTRA", mdx);
        Assert.Contains("BIODIESEL CON MEZCLA", mdx);
    }

    [Fact]
    public void BuildsAnnualCommand()
    {
        var options = EdsTopReport.CreateOptions(
            2025, null, null, annual: true, months: null, measure: "dispatched", product: "all", top: 20);

        var command = EdsTopReport.BuildEquivalentCommand(options, "output");

        Assert.Equal("2025-A", options.PeriodLabel);
        Assert.Equal(Enumerable.Range(1, 12), options.Months);
        Assert.Contains("report eds_top", command);
        Assert.Contains("--annual", command);
        Assert.Contains("--measure dispatched", command);
    }

    [Fact]
    public void RejectsBothMeasures()
    {
        Assert.Throws<ArgumentException>(() =>
            EdsTopReport.CreateOptions(2026, 2, null, null, "both", "all", 20));
    }

    [Fact]
    public void CalculatesDispatchedNationalShare()
    {
        var options = EdsTopReport.CreateOptions(2026, 2, null, null, "dispatched", "all", 1);
        var top = new[]
        {
            new EdsTopReport.TopEdsRow(1, "123", "EDS UNO", "BOGOTA", "BOGOTA D.C.", "NO", 100, 90)
        };
        var table = EdsTopReport.BuildOutputTable(top, 900, options);

        Assert.Equal("90", table.Rows[0][9]);
        Assert.Equal("10", table.Rows[0][10]);
        Assert.Equal(11, table.Headers.Count);
    }

    [Fact]
    public void NormalizesEqualVolumesWithCodeTieBreak()
    {
        var options = EdsTopReport.CreateOptions(2026, 2, null, null, "dispatched", "all", 2);
        var source = new MdxTable(
            ["CODIGO", "NOMBRE", "MUNICIPIO", "DEPARTAMENTO", "ZONA", "ACEPTADO", "DESPACHADO"],
            [
                ["B", "EDS B", "M", "D", "NO", "100", "90"],
                ["A", "EDS A", "M", "D", "NO", "100", "90"]
            ],
            null);

        var rows = EdsTopReport.NormalizeTopRows(source, options);

        Assert.Equal("A", rows[0].Code);
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal("B", rows[1].Code);
    }

    [Fact]
    public void RejectsNegativeVolumes()
    {
        var options = EdsTopReport.CreateOptions(2026, 2, null, null, "dispatched", "all", 1);
        var source = new MdxTable(
            ["CODIGO", "NOMBRE", "MUNICIPIO", "DEPARTAMENTO", "ZONA", "ACEPTADO", "DESPACHADO"],
            [["A", "EDS A", "M", "D", "NO", "100", "-1"]],
            null);

        Assert.Throws<InvalidDataException>(() => EdsTopReport.NormalizeTopRows(source, options));
    }
}
