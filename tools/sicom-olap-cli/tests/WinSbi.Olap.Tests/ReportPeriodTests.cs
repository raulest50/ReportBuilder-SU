using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class ReportPeriodTests
{
    [Fact]
    public void ResolvesFirstSemester()
    {
        var period = ReportPeriods.Resolve(2026, quarter: null, semester: 1, months: null);

        Assert.Equal([1, 2, 3, 4, 5, 6], period.Months);
        Assert.Equal("2026-S1", period.Label);
        Assert.Equal("--semester 1", period.CommandPart);
    }

    [Fact]
    public void ResolvesSecondSemester()
    {
        var period = ReportPeriods.Resolve(2026, quarter: null, semester: 2, months: null);

        Assert.Equal([7, 8, 9, 10, 11, 12], period.Months);
        Assert.Equal("2026-S2", period.Label);
        Assert.Equal("--semester 2", period.CommandPart);
    }

    [Fact]
    public void ResolvesAnnualPeriod()
    {
        var period = ReportPeriods.Resolve(2025, quarter: null, semester: null, annual: true, months: null);

        Assert.Equal(Enumerable.Range(1, 12), period.Months);
        Assert.Equal("2025-A", period.Label);
        Assert.Equal("--annual", period.CommandPart);
    }

    [Fact]
    public void RejectsInvalidSemester()
    {
        Assert.Throws<ArgumentException>(() => ReportPeriods.Resolve(2026, quarter: null, semester: 0, months: null));
        Assert.Throws<ArgumentException>(() => ReportPeriods.Resolve(2026, quarter: null, semester: 3, months: null));
    }

    [Fact]
    public void RejectsCombinedPeriodOptions()
    {
        Assert.Throws<ArgumentException>(() => ReportPeriods.Resolve(2026, quarter: 1, semester: 1, months: null));
        Assert.Throws<ArgumentException>(() => ReportPeriods.Resolve(2026, quarter: null, semester: 1, months: "1,2,3"));
        Assert.Throws<ArgumentException>(() => ReportPeriods.Resolve(2026, quarter: 1, semester: null, months: "1,2,3"));
        Assert.Throws<ArgumentException>(() => ReportPeriods.Resolve(2026, quarter: 1, semester: null, annual: true, months: null));
    }

    [Fact]
    public void KeepsManualSixMonthPeriodAsMonthsArgument()
    {
        var options = MayoristasReport.CreateOptions(
            2026,
            quarter: null,
            semester: null,
            months: "1,2,3,4,5,6",
            measure: "accepted",
            product: "all");

        var outputDirectory = Path.Combine("reports", "SBI-Ordenes-Pedidos", "mayoristas", "manual");
        var command = MayoristasReport.BuildEquivalentCommand(options, outputDirectory);

        Assert.Equal("2026-M01-M02-M03-M04-M05-M06", options.PeriodLabel);
        Assert.Contains("--months 1,2,3,4,5,6", command);
        Assert.DoesNotContain("--semester", command);
    }

    [Fact]
    public void BuildsSemesterDirectoriesAndCommandsForAllReports()
    {
        var mayoristas = MayoristasReport.CreateOptions(
            2026,
            quarter: null,
            semester: 1,
            months: null,
            measure: "accepted",
            product: "all");
        var edsMunicipios = EdsMunicipiosReport.CreateOptions(
            2026,
            quarter: null,
            semester: 1,
            months: null,
            product: "all");
        var edsInsights = EdsInsightsReport.CreateOptions(
            2026,
            quarter: null,
            semester: 1,
            months: null,
            product: "all",
            topCities: 7,
            topEds: 20);

        var mayoristasDirectory = Path.Combine("reports", "SBI-Ordenes-Pedidos", "mayoristas", "2026-S1");
        var municipiosDirectory = Path.Combine("reports", "SBI-Ordenes-Pedidos", "eds_municipios", "2026-S1");
        var insightsDirectory = Path.Combine("reports", "SBI-Ordenes-Pedidos", "eds_insights", "2026-S1");

        Assert.Equal(mayoristasDirectory, MayoristasReport.DefaultOutputDirectory(mayoristas));
        Assert.Equal(municipiosDirectory, EdsMunicipiosReport.DefaultOutputDirectory(edsMunicipios));
        Assert.Equal(insightsDirectory, EdsInsightsReport.DefaultOutputDirectory(edsInsights));

        Assert.Contains("--semester 1", MayoristasReport.BuildEquivalentCommand(mayoristas, mayoristasDirectory));
        Assert.Contains("--semester 1", EdsMunicipiosReport.BuildEquivalentCommand(edsMunicipios, municipiosDirectory));
        Assert.Contains("--semester 1", EdsInsightsReport.BuildEquivalentCommand(edsInsights, insightsDirectory));
    }
}
