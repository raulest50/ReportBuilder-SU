using System.Xml.Linq;
using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class XmlaEnvelopeTests
{
    [Fact]
    public void DiscoverEnvelopeContainsRequestTypeRestrictionsAndCatalog()
    {
        var xml = XmlaEnvelopeBuilder.BuildDiscoverEnvelope(
            "https://server/OLAP/msmdpump.dll",
            "MDSCHEMA_CUBES",
            new Dictionary<string, string> { ["CATALOG_NAME"] = "SBI" },
            new Dictionary<string, string> { ["Catalog"] = "SBI" },
            timeoutSeconds: 42);
        var document = XDocument.Parse(xml);

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "RequestType" && element.Value == "MDSCHEMA_CUBES");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "CATALOG_NAME" && element.Value == "SBI");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Catalog" && element.Value == "SBI");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Format" && element.Value == "Tabular");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Timeout" && element.Value == "42");
    }

    [Fact]
    public void ExecuteEnvelopeContainsMdxAndMultidimensionalProperties()
    {
        var xml = XmlaEnvelopeBuilder.BuildExecuteEnvelope(
            "https://server/OLAP/msmdpump.dll",
            "SBI",
            "SELECT {} ON COLUMNS FROM [Cube]",
            "ClusterFormat",
            content: null,
            timeoutSeconds: 120);
        var document = XDocument.Parse(xml);

        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Statement" && element.Value.Contains("FROM [Cube]", StringComparison.Ordinal));
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Catalog" && element.Value == "SBI");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Format" && element.Value == "Multidimensional");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "AxisFormat" && element.Value == "ClusterFormat");
    }
}
