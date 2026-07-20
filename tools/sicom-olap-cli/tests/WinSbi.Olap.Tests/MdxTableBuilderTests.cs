using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class MdxTableBuilderTests
{
    [Fact]
    public void BuildsScalarTable()
    {
        var table = MdxTableBuilder.BuildTable(new MdxQueryResult(
            "123",
            [],
            [],
            [new MdxCell(0, "123", "")],
            []));

        Assert.Equal(["Value"], table.Headers);
        Assert.Equal("123", table.Rows[0][0]);
    }

    [Fact]
    public void BuildsAxis0Table()
    {
        var result = new MdxQueryResult(
            "10",
            [],
            [
                new MdxAxis("Axis0", 0, [
                    new MdxTuple([new MdxMember("Measure A", "[Measures].[A]", "", "Measures")]),
                    new MdxTuple([new MdxMember("Measure B", "[Measures].[B]", "", "Measures")])
                ])
            ],
            [
                new MdxCell(0, "10", ""),
                new MdxCell(1, "20", "")
            ],
            []);

        var table = MdxTableBuilder.BuildTable(result);

        Assert.Equal(["Column", "Value"], table.Headers);
        Assert.Equal("Measure A", table.Rows[0][0]);
        Assert.Equal("10", table.Rows[0][1]);
        Assert.Equal("Measure B", table.Rows[1][0]);
        Assert.Equal("20", table.Rows[1][1]);
    }

    [Fact]
    public void BuildsAxis0Axis1Table()
    {
        var result = new MdxQueryResult(
            "10",
            [],
            [
                new MdxAxis("Axis0", 0, [
                    new MdxTuple([new MdxMember("Measure A", "[Measures].[A]", "", "Measures")]),
                    new MdxTuple([new MdxMember("Measure B", "[Measures].[B]", "", "Measures")])
                ]),
                new MdxAxis("Axis1", 1, [
                    new MdxTuple([new MdxMember("2025", "[Date].[2025]", "Year", "Date")]),
                    new MdxTuple([new MdxMember("2026", "[Date].[2026]", "Year", "Date")])
                ])
            ],
            [
                new MdxCell(0, "1", ""),
                new MdxCell(1, "2", ""),
                new MdxCell(2, "3", ""),
                new MdxCell(3, "4", "")
            ],
            []);

        var table = MdxTableBuilder.BuildTable(result);

        Assert.Equal(["Year", "Measure A", "Measure B"], table.Headers);
        Assert.Equal(["2025", "1", "2"], table.Rows[0]);
        Assert.Equal(["2026", "3", "4"], table.Rows[1]);
    }

    [Fact]
    public void CsvEscapesQuotesCommasAndLineBreaks()
    {
        var csv = OutputFormatters.ToCsv(new MdxTable(
            ["Name", "Value"],
            [["A,B", "one \"two\"\nthree"]],
            null));

        Assert.Contains("\"A,B\",\"one \"\"two\"\"", csv);
        Assert.Contains("three\"", csv);
    }
}
