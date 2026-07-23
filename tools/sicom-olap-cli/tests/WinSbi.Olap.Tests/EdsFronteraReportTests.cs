using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class EdsFronteraReportTests
{
    [Fact]
    public void BuildsDispatchedVolumesMdxForBothYearsAndThreeProducts()
    {
        var options = EdsFronteraReport.CreateOptions(2026, 2, null, false, null);

        var mdx = EdsFronteraReport.BuildVolumesMdx(options);

        Assert.Contains("[AÑO DESPACHO].&[2025]", mdx);
        Assert.Contains("[AÑO DESPACHO].&[2026]", mdx);
        Assert.Contains("[Measures].[VOLUMEN DESPACHADO]", mdx);
        Assert.DoesNotContain("VOLUMEN ACEPTADO", mdx);
        Assert.Contains("[COMPRADOR].[ZONA FRONTERA]", mdx);
        Assert.Contains("GASOLINA MOTOR CORRIENTE", mdx);
        Assert.Contains("GASOLINA MOTOR EXTRA", mdx);
        Assert.Contains("BIODIESEL CON MEZCLA", mdx);
    }

    [Fact]
    public void BuildsFrontierMunicipalityTopFiveMdx()
    {
        var options = EdsFronteraReport.CreateOptions(2026, 2, null, false, null);

        var mdx = EdsFronteraReport.BuildMunicipalitiesMdx(options);

        Assert.Contains("TOPCOUNT", mdx);
        Assert.Contains("        5,", mdx);
        Assert.Contains("[COMPRADOR].[ZONA FRONTERA].&[SI]", mdx);
        Assert.Contains("[COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR]", mdx);
        Assert.Contains("[Measures].[VOLUMEN DESPACHADO]", mdx);
    }

    [Fact]
    public void BuildsActiveEdsMdxFromAgentsCube()
    {
        var mdx = EdsFronteraReport.BuildActiveEdsMdx();

        Assert.Contains("FROM [Agentes]", mdx);
        Assert.Contains("[AGENTE].[ZONA DE FRONTERA]", mdx);
        Assert.Contains("[AGENTE].[NUEVO ESTADO].&[ACTIVO]", mdx);
        Assert.Contains("ESTACION DE SERVICIO AUTOMOTRIZ", mdx);
    }

    [Fact]
    public void BuildsSummaryFromDispatchedVolumes()
    {
        var options = EdsFronteraReport.CreateOptions(2026, 2, null, false, null);
        var volumes = new[]
        {
            new EdsFronteraReport.ZoneVolumeRow("SI", 2025, "CORRIENTE", "corriente", 150),
            new EdsFronteraReport.ZoneVolumeRow("SI", 2026, "CORRIENTE", "corriente", 120),
            new EdsFronteraReport.ZoneVolumeRow("NO", 2025, "CORRIENTE", "corriente", 850),
            new EdsFronteraReport.ZoneVolumeRow("NO", 2026, "CORRIENTE", "corriente", 880)
        };
        var active = new[]
        {
            new EdsFronteraReport.ActiveEdsRow("SI", 2),
            new EdsFronteraReport.ActiveEdsRow("NO", 8)
        };

        var table = EdsFronteraReport.BuildSummaryTable(volumes, active, options);

        Assert.Equal("SI", table.Rows[0][0]);
        Assert.Equal("-30", table.Rows[0][3]);
        Assert.Equal("-20", table.Rows[0][4]);
        Assert.Equal("20", table.Rows[0][6]);
        Assert.Equal("20", table.Rows[0][8]);
        Assert.Equal("TOTAL NACIONAL", table.Rows[2][0]);
        Assert.Equal("0", table.Rows[2][3]);
    }

    [Fact]
    public void LeavesVariationEmptyWhenPreviousVolumeIsZero()
    {
        var options = EdsFronteraReport.CreateOptions(2026, 2, null, false, null);
        var volumes = new[]
        {
            new EdsFronteraReport.ZoneVolumeRow("SI", 2026, "CORRIENTE", "corriente", 10),
            new EdsFronteraReport.ZoneVolumeRow("NO", 2026, "CORRIENTE", "corriente", 90)
        };
        var active = new[]
        {
            new EdsFronteraReport.ActiveEdsRow("SI", 1),
            new EdsFronteraReport.ActiveEdsRow("NO", 9)
        };

        var table = EdsFronteraReport.BuildSummaryTable(volumes, active, options);

        Assert.Equal("", table.Rows[0][4]);
        Assert.Equal("", table.Rows[1][4]);
    }

    [Fact]
    public void RanksMunicipalitiesAndCalculatesFrontierShare()
    {
        var options = EdsFronteraReport.CreateOptions(2026, 2, null, false, null);
        var source = new MdxTable(
            ["COD_DEP", "DEP", "COD_MUN", "MUN", "VOLUMEN"],
            [
                ["20", "CESAR", "20011", "AGUACHICA", "20"],
                ["20", "CESAR", "20001", "VALLEDUPAR", "30"]
            ],
            null);
        var municipalities = EdsFronteraReport.NormalizeMunicipalityRows(source, 5);
        var volumes = new[]
        {
            new EdsFronteraReport.ZoneVolumeRow("SI", 2026, "CORRIENTE", "corriente", 100),
            new EdsFronteraReport.ZoneVolumeRow("NO", 2026, "CORRIENTE", "corriente", 400)
        };

        var table = EdsFronteraReport.BuildMunicipalitiesTable(municipalities, volumes, options);

        Assert.Equal("VALLEDUPAR", table.Rows[0][4]);
        Assert.Equal("30", table.Rows[0][6]);
        Assert.Equal("AGUACHICA", table.Rows[1][4]);
        Assert.Equal("20", table.Rows[1][6]);
    }
}
