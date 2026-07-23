using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class MayoristasHistoricoReportTests
{
    [Fact]
    public void CreatesExactHistoricalWindowAndYearParts()
    {
        var options = CreateOptions();

        Assert.Equal("2011-01_2026-06", options.PeriodLabel);
        Assert.Equal(16, options.YearParts.Count);
        Assert.Equal(186, options.YearParts.Sum(static part => part.Months.Count));
        Assert.Equal(Enumerable.Range(1, 12), options.YearParts[0].Months);
        Assert.Equal(Enumerable.Range(1, 6), options.YearParts[^1].Months);
        Assert.Equal(MayoristasMeasureSelection.Dispatched, options.Measure);
        Assert.Equal(EdsBuyerScope.EdsFluvial, options.BuyerScope);
    }

    [Fact]
    public void RejectsInvalidRangesAndBothMeasures()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MayoristasHistoricoReport.CreateOptions(
            2011, 0, 2026, 6, "dispatched", "all", "eds_fluvial", false));
        Assert.Throws<ArgumentException>(() => MayoristasHistoricoReport.CreateOptions(
            2026, 7, 2026, 6, "dispatched", "all", "eds_fluvial", false));
        Assert.Throws<ArgumentException>(() => MayoristasHistoricoReport.CreateOptions(
            2011, 1, 2026, 6, "both", "all", "eds_fluvial", false));
    }

    [Fact]
    public void BuildsHistoricalMdxWithoutTopCount()
    {
        var options = CreateOptions();
        var mdx = MayoristasHistoricoReport.BuildMdx(options, options.YearParts[^1]);

        Assert.Contains("[Measures].[VOLUMEN DESPACHADO]", mdx);
        Assert.Contains("[PROVEEDOR].[TIPO AGENTE PROVEEDOR].&[DISTRIBUIDOR MAYORISTA]", mdx);
        Assert.Contains("ESTACION DE SERVICIO AUTOMOTRIZ", mdx);
        Assert.Contains("ESTACION DE SERVICIO FLUVIAL", mdx);
        Assert.DoesNotContain("COMERCIALIZADOR INDUSTRIAL", mdx);
        Assert.Contains("GASOLINA MOTOR CORRIENTE", mdx);
        Assert.Contains("GASOLINA MOTOR EXTRA", mdx);
        Assert.Contains("BIODIESEL CON MEZCLA", mdx);
        Assert.DoesNotContain("TOPCOUNT", mdx, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[AÑO DESPACHO].&[2026]", mdx);
        Assert.DoesNotContain("[MES DESPACHO].&[7]", mdx);
    }

    [Fact]
    public void NormalizesAndFiltersInvalidVolumes()
    {
        var options = MayoristasHistoricoReport.CreateOptions(
            2026, 1, 2026, 1, "dispatched", "all", "eds_fluvial", false);
        var source = new MdxTable(
            ["provider", "month", "year", "product", "buyer", "volume"],
            [
                ["A", "1", "2026", "GASOLINA MOTOR CORRIENTE", "ESTACION DE SERVICIO AUTOMOTRIZ", "30.5"],
                ["B", "1", "2026", "GASOLINA MOTOR EXTRA", "ESTACION DE SERVICIO FLUVIAL", "-1"],
                ["C", "1", "2026", "BIODIESEL CON MEZCLA", "ESTACION DE SERVICIO AUTOMOTRIZ", "not-a-number"],
                ["D", "1", "2026", "BIODIESEL CON MEZCLA", "ESTACION DE SERVICIO AUTOMOTRIZ", "0"]
            ],
            null);

        var normalized = MayoristasHistoricoReport.NormalizePartTable(source, options, options.YearParts[0]);

        Assert.Equal(
            ["PERIODO", "ANO", "MES", "NOMBRE", "PRODUCTO", "SUBTIPO_AGENTE", "VOLUMEN_DESPACHADO"],
            normalized.Headers);
        Assert.Equal(2, normalized.Rows.Count);
        Assert.Equal("2026-01", normalized.Rows[0][0]);
        Assert.Equal("30.5", normalized.Rows[0][6]);
        Assert.Equal("0", normalized.Rows[1][6]);
    }

    [Fact]
    public void AggregatesProvidersAndCalculatesMonthlyParticipation()
    {
        var options = MayoristasHistoricoReport.CreateOptions(
            2026, 1, 2026, 2, "dispatched", "all", "eds_fluvial", false);
        var detail = new MdxTable(
            ["PERIODO", "ANO", "MES", "NOMBRE", "PRODUCTO", "SUBTIPO_AGENTE", "VOLUMEN_DESPACHADO"],
            [
                ["2026-01", "2026", "1", "A", "CORRIENTE", "AUTOMOTRIZ", "30"],
                ["2026-01", "2026", "1", "A", "EXTRA", "FLUVIAL", "20"],
                ["2026-01", "2026", "1", "B", "CORRIENTE", "AUTOMOTRIZ", "50"],
                ["2026-02", "2026", "2", "A", "CORRIENTE", "AUTOMOTRIZ", "30"],
                ["2026-02", "2026", "2", "B", "CORRIENTE", "AUTOMOTRIZ", "70"]
            ],
            null);

        var monthly = MayoristasHistoricoReport.BuildMonthlyTable(detail, options);

        Assert.Equal(
            ["PERIODO", "ANO", "MES", "NOMBRE", "VOLUMEN_DESPACHADO", "PARTICIPACION_TOTAL"],
            monthly.Headers);
        Assert.Equal(4, monthly.Rows.Count);
        Assert.Contains(monthly.Rows, row => row.SequenceEqual(["2026-01", "2026", "1", "A", "50", "50"]));
        Assert.Contains(monthly.Rows, row => row.SequenceEqual(["2026-02", "2026", "2", "B", "70", "70"]));
    }

    [Fact]
    public void RejectsMissingCompleteMonth()
    {
        var options = MayoristasHistoricoReport.CreateOptions(
            2026, 1, 2026, 2, "dispatched", "all", "eds_fluvial", false);
        var detail = new MdxTable(
            ["PERIODO", "ANO", "MES", "NOMBRE", "PRODUCTO", "SUBTIPO_AGENTE", "VOLUMEN_DESPACHADO"],
            [["2026-01", "2026", "1", "A", "CORRIENTE", "AUTOMOTRIZ", "30"]],
            null);

        Assert.Throws<InvalidDataException>(() => MayoristasHistoricoReport.BuildMonthlyTable(detail, options));
    }

    [Fact]
    public async Task ResumesCompatiblePartWithoutQueryingXmla()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"mayoristas-historico-{Guid.NewGuid():N}");
        try
        {
            var firstOptions = MayoristasHistoricoReport.CreateOptions(
                2026, 1, 2026, 1, "dispatched", "all", "eds_fluvial", false);
            var firstClient = new StubXmlaClient(BuildSingleRowResult());
            await MayoristasHistoricoReport.GenerateAsync(
                firstClient,
                firstOptions,
                outputDirectory,
                MayoristasHistoricoReport.BuildEquivalentCommand(firstOptions, outputDirectory));
            Assert.Equal(1, firstClient.ExecuteCount);

            var resumeOptions = firstOptions with { Resume = true };
            var resumeClient = new StubXmlaClient(BuildSingleRowResult(), throwOnExecute: true);
            var result = await MayoristasHistoricoReport.GenerateAsync(
                resumeClient,
                resumeOptions,
                outputDirectory,
                MayoristasHistoricoReport.BuildEquivalentCommand(resumeOptions, outputDirectory));

            Assert.Equal(0, resumeClient.ExecuteCount);
            Assert.Single(result.PartCsvPaths);
            Assert.True(File.Exists(result.DetailCsvPath));
            Assert.True(File.Exists(result.MonthlyCsvPath));
            Assert.True(File.Exists(result.ManifestJsonPath));
            Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void RejectsIncompatibleManifest()
    {
        var options = CreateOptions();
        var manifest = new MayoristasHistoricoManifest(
            1,
            "mayoristas_historico",
            "in_progress",
            "SBI-Ordenes-Pedidos",
            "Ordenes-Pedidos",
            "accepted",
            "all",
            "eds_fluvial",
            "DISTRIBUIDOR MAYORISTA",
            "2011-01",
            "2026-06",
            DateTimeOffset.UtcNow.ToString("O"),
            "",
            [],
            0,
            0);

        Assert.Throws<InvalidDataException>(() =>
            MayoristasHistoricoReport.ValidateManifestCompatibility(manifest, options));
    }

    private static MayoristasHistoricoReportOptions CreateOptions()
    {
        return MayoristasHistoricoReport.CreateOptions(
            2011, 1, 2026, 6, "dispatched", "all", "eds_fluvial", true);
    }

    private static MdxQueryResult BuildSingleRowResult()
    {
        var column = new MdxTuple([
            new MdxMember("VOLUMEN DESPACHADO", "[Measures].[VOLUMEN DESPACHADO]", "Measures", "Measures")
        ]);
        var row = new MdxTuple([
            new MdxMember("A", "", "NOMBRE", "PROVEEDOR"),
            new MdxMember("1", "", "MES", "MOVIMIENTOS"),
            new MdxMember("2026", "", "ANO", "MOVIMIENTOS"),
            new MdxMember("GASOLINA MOTOR CORRIENTE", "", "PRODUCTO", "PRODUCTO"),
            new MdxMember("ESTACION DE SERVICIO AUTOMOTRIZ", "", "SUBTIPO", "COMPRADOR")
        ]);
        return new MdxQueryResult(
            null,
            [],
            [new MdxAxis("Axis0", 0, [column]), new MdxAxis("Axis1", 1, [row])],
            [new MdxCell(0, "100", "100")],
            []);
    }

    private sealed class StubXmlaClient(MdxQueryResult result, bool throwOnExecute = false) : IXmlaClient
    {
        public int ExecuteCount { get; private set; }

        public Task<DiscoverResult> DiscoverAsync(
            string requestType,
            IReadOnlyDictionary<string, string> restrictions,
            IReadOnlyDictionary<string, string> properties,
            XmlaDebugOptions? debugOptions = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MdxQueryResult> ExecuteMdxAsync(
            string catalogName,
            string statement,
            string axisFormat = "ClusterFormat",
            string? content = null,
            XmlaDebugOptions? debugOptions = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            if (throwOnExecute)
            {
                throw new InvalidOperationException("XMLA should not be called during a compatible resume.");
            }

            return Task.FromResult(result);
        }
    }
}
