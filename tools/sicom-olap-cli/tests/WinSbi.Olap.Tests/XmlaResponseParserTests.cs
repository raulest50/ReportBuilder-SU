using WinSbi.Olap.Core;
using Xunit;

namespace WinSbi.Olap.Tests;

public sealed class XmlaResponseParserTests
{
    [Fact]
    public void ParsesRowsetRows()
    {
        var result = XmlaResponseParser.ParseDiscoverResponse(
            """
            <Envelope>
              <Body>
                <DiscoverResponse>
                  <return>
                    <root>
                      <row><CATALOG_NAME>SBI</CATALOG_NAME><DESCRIPTION>Desc</DESCRIPTION></row>
                    </root>
                  </return>
                </DiscoverResponse>
              </Body>
            </Envelope>
            """);

        Assert.Single(result.Rows);
        Assert.Equal("SBI", result.Rows[0]["CATALOG_NAME"]);
        Assert.Equal("Desc", result.Rows[0]["DESCRIPTION"]);
    }

    [Fact]
    public void ThrowsOnSoapFault()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => XmlaResponseParser.ParseDiscoverResponse(
            """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <soap:Fault>
                  <faultstring>bad credentials</faultstring>
                </soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """));

        Assert.Contains("bad credentials", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThrowsOnXmlaCellError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => XmlaResponseParser.ParseMdxResponse(
            """
            <Envelope>
              <Body>
                <ExecuteResponse>
                  <return>
                    <root>
                      <Error><Description>Invalid MDX</Description></Error>
                    </root>
                  </return>
                </ExecuteResponse>
              </Body>
            </Envelope>
            """));

        Assert.Contains("Invalid MDX", exception.Message);
    }
}
