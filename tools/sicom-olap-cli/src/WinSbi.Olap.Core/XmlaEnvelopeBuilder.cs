using System.Xml.Linq;

namespace WinSbi.Olap.Core;

public static class XmlaEnvelopeBuilder
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Xmla = "urn:schemas-microsoft-com:xml-analysis";

    public static string BuildDiscoverEnvelope(
        string endpointUrl,
        string requestType,
        IReadOnlyDictionary<string, string> restrictions,
        IReadOnlyDictionary<string, string> properties,
        int timeoutSeconds)
    {
        var restrictionElements = restrictions
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => new XElement(Xmla + item.Key, item.Value));

        var propertyList = new List<XElement>
        {
            new(Xmla + "DataSourceInfo", $"Provider=MSOLAP;Data Source={endpointUrl};"),
            new(Xmla + "Format", "Tabular"),
            new(Xmla + "Timeout", Math.Max(1, timeoutSeconds))
        };

        propertyList.AddRange(properties
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => new XElement(Xmla + item.Key, item.Value)));

        var document = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XElement(Soap + "Body",
                    new XElement(Xmla + "Discover",
                        new XElement(Xmla + "RequestType", requestType),
                        new XElement(Xmla + "Restrictions",
                            new XElement(Xmla + "RestrictionList", restrictionElements)),
                        new XElement(Xmla + "Properties",
                            new XElement(Xmla + "PropertyList", propertyList))))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string BuildExecuteEnvelope(
        string endpointUrl,
        string catalogName,
        string statement,
        string axisFormat,
        string? content,
        int timeoutSeconds)
    {
        var propertyList = new List<XElement>
        {
            new(Xmla + "DataSourceInfo", $"Provider=MSOLAP;Data Source={endpointUrl};"),
            new(Xmla + "Catalog", catalogName),
            new(Xmla + "Format", "Multidimensional"),
            new(Xmla + "AxisFormat", string.IsNullOrWhiteSpace(axisFormat) ? "ClusterFormat" : axisFormat),
            new(Xmla + "Timeout", Math.Max(1, timeoutSeconds))
        };

        if (!string.IsNullOrWhiteSpace(content))
        {
            propertyList.Add(new XElement(Xmla + "Content", content));
        }

        var document = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XElement(Soap + "Body",
                    new XElement(Xmla + "Execute",
                        new XElement(Xmla + "Command",
                            new XElement(Xmla + "Statement", statement)),
                        new XElement(Xmla + "Properties",
                            new XElement(Xmla + "PropertyList", propertyList))))));

        return document.ToString(SaveOptions.DisableFormatting);
    }
}
