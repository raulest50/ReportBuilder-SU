using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class EdsMunicipiosReportTests
{
    [Fact]
    public void CreatesDefaultQuarterOutputDirectory()
    {
        var options = EdsMunicipiosReport.CreateOptions(2026, 2, null, "all");

        Assert.Equal(Path.Combine("reports", "SBI-Ordenes-Pedidos", "eds_municipios", "2026-Q2"), EdsMunicipiosReport.DefaultOutputDirectory(options));
    }

    [Fact]
    public void GeneratesMdxForAcceptedAutomotiveStationsAndMunicipalFields()
    {
        var options = EdsMunicipiosReport.CreateOptions(2026, 2, null, "all");
        var mdx = EdsMunicipiosReport.BuildMdx(options);

        Assert.Contains("[Measures].[VOLUMEN ACEPTADO]", mdx);
        Assert.Contains("[COMPRADOR].[SUBTIPO AGENTE COMPRADOR].&[ESTACION DE SERVICIO AUTOMOTRIZ]", mdx);
        Assert.Contains("[COMPRADOR].[CODIGO DANE DEPARTAMENTO AGENTE COMPRADOR]", mdx);
        Assert.Contains("[COMPRADOR].[DEPARTAMENTO AGENTE COMPRADOR]", mdx);
        Assert.Contains("[COMPRADOR].[CODIGO DANE MUNICIPIO AGENTE COMPRADOR]", mdx);
        Assert.Contains("[COMPRADOR].[MUNICIPIO AGENTE COMPRADOR]", mdx);
        Assert.Contains("[COMPRADOR].[ZONA FRONTERA]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[GASOLINA MOTOR CORRIENTE]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[GASOLINA MOTOR EXTRA]", mdx);
        Assert.Contains("[PRODUCTO].[DESCRIPCION PRODUCTO].&[BIODIESEL CON MEZCLA]", mdx);
    }

    [Fact]
    public void GeneratesActiveEdsMdxFromAgentsCube()
    {
        var mdx = EdsMunicipiosReport.BuildActiveEdsMdx();

        Assert.Contains("[Measures].[AGENTE CANTIDAD]", mdx);
        Assert.Contains("[SUBTIPO AGENTE].[SUBTIPO AGENTE].&[ESTACION DE SERVICIO AUTOMOTRIZ]", mdx);
        Assert.Contains("[AGENTE].[NUEVO ESTADO].&[ACTIVO]", mdx);
        Assert.Contains("[UBICACION INSTALACION].[CODIGO DANE MUNICIPIO INSTALACION]", mdx);
        Assert.Contains("[AGENTE].[ZONA DE FRONTERA]", mdx);
        Assert.Contains("FROM [Agentes]", mdx);
    }

    [Fact]
    public void NormalizesDetailTableWithDerivedColumns()
    {
        var options = EdsMunicipiosReport.CreateOptions(2026, 2, null, "corriente");
        var source = new MdxTable(
            ["Row 1", "Row 2", "Row 3", "Row 4", "Row 5", "Row 6", "Row 7", "Row 8", "VOLUMEN ACEPTADO"],
            [
                ["11", "BOGOTA D.C.", "11001", "BOGOTA", "SI", "2026", "4", "GASOLINA MOTOR CORRIENTE", "123.5"]
            ],
            null);

        var normalized = EdsMunicipiosReport.NormalizeDetailTable(source, options);

        Assert.Equal(
            [
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
            ],
            normalized.Headers);
        Assert.Equal(
            ["11", "BOGOTA D.C.", "11001", "BOGOTA", "SI", "SI", "2026", "4", "2", "2026-04", "GASOLINA MOTOR CORRIENTE", "corriente", "123.5"],
            normalized.Rows[0]);
    }

    [Fact]
    public void BuildsAverageSummaryDividingByAllSelectedMonths()
    {
        var options = EdsMunicipiosReport.CreateOptions(2026, null, "4,5,6", "corriente");
        var detail = new MdxTable(
            [
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
            ],
            [
                ["11", "BOGOTA D.C.", "11001", "BOGOTA", "SI", "SI", "2026", "4", "2", "2026-04", "GASOLINA MOTOR CORRIENTE", "corriente", "30"],
                ["11", "BOGOTA D.C.", "11001", "BOGOTA", "SI", "SI", "2026", "5", "2", "2026-05", "GASOLINA MOTOR CORRIENTE", "corriente", "60"]
            ],
            null);

        var summary = EdsMunicipiosReport.BuildAverageSummary(detail, options);

        Assert.Equal("3", summary.Rows[0][8]);
        Assert.Equal("90", summary.Rows[0][9]);
        Assert.Equal("30", summary.Rows[0][10]);
    }

    [Fact]
    public void NormalizesActiveEdsTableToOneRowPerMunicipality()
    {
        var source = new MdxTable(
            ["DeptCode", "Dept", "MunCode", "Mun", "Border", "AGENTE CANTIDAD"],
            [
                ["11", "BOGOTA D.C.", "11001", "BOGOTA", "SI", "2"],
                ["11", "BOGOTA D.C.", "11001", "BOGOTA", "NO", "3"]
            ],
            null);

        var normalized = EdsMunicipiosReport.NormalizeActiveEdsTable(source);

        Assert.Equal(
            ["CODIGO_DANE_DEPARTAMENTO", "DEPARTAMENTO", "CODIGO_DANE_MUNICIPIO", "MUNICIPIO", "ZONA_FRONTERA", "ES_ZONA_FRONTERA", "EDS_AUTOMOTRIZ_ACTIVAS"],
            normalized.Headers);
        Assert.Single(normalized.Rows);
        Assert.Equal(["11", "BOGOTA D.C.", "11001", "BOGOTA", "SI", "SI", "5"], normalized.Rows[0]);
    }

    [Fact]
    public void BuildsEquivalentCommand()
    {
        var options = EdsMunicipiosReport.CreateOptions(2026, 2, null, "diesel");

        var command = EdsMunicipiosReport.BuildEquivalentCommand(options, @"reports\SBI-Ordenes-Pedidos\eds_municipios\2026-Q2");

        Assert.Contains("dotnet run -- report eds_municipios", command);
        Assert.Contains("--year 2026", command);
        Assert.Contains("--quarter 2", command);
        Assert.Contains("--product diesel", command);
    }
}
