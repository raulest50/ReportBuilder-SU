using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace WinSbi.Olap.Core;

public sealed class XmlaClient : IXmlaClient
{
    private readonly string _endpointUrl;
    private readonly HttpClient _httpClient;
    private readonly int _serverTimeoutSeconds;

    public XmlaClient(string endpointUrl, HttpClient httpClient, int serverTimeoutSeconds)
    {
        _endpointUrl = endpointUrl;
        _httpClient = httpClient;
        _serverTimeoutSeconds = Math.Max(1, serverTimeoutSeconds);
    }

    public static XmlaClient Create(SourceConnection connection)
    {
        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(connection.User, connection.Password),
            PreAuthenticate = true
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(connection.TimeoutSeconds + 15)
        };

        if (connection.AuthMode.Equals("basic", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Encoding.UTF8.GetBytes($"{connection.User}:{connection.Password}");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        }
        else if (!connection.AuthMode.Equals("challenge", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Auth mode must be 'basic' or 'challenge'.", nameof(connection));
        }

        return new XmlaClient(connection.Source.Url, client, connection.TimeoutSeconds);
    }

    public async Task<EndpointProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Head, _endpointUrl);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        return new EndpointProbeResult(
            _endpointUrl,
            (int)response.StatusCode,
            response.ReasonPhrase ?? "",
            response.Headers.WwwAuthenticate.ToString(),
            response.Headers.Server.ToString(),
            response.IsSuccessStatusCode);
    }

    public async Task<DiscoverResult> DiscoverAsync(
        string requestType,
        IReadOnlyDictionary<string, string> restrictions,
        IReadOnlyDictionary<string, string> properties,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var request = XmlaEnvelopeBuilder.BuildDiscoverEnvelope(
            _endpointUrl,
            requestType,
            restrictions,
            properties,
            _serverTimeoutSeconds);
        var result = await PostXmlaAsync(
            request,
            "\"urn:schemas-microsoft-com:xml-analysis:Discover\"",
            debugOptions,
            cancellationToken);

        if (result.StatusCode is < 200 or > 299)
        {
            throw new XmlaHttpException(
                $"XMLA Discover failed: {result.StatusCode} {result.ReasonPhrase}. " +
                $"WWW-Authenticate: {result.WwwAuthenticate}. Body: {TrimForError(result.Xml)}",
                result.StatusCode,
                result.ReasonPhrase,
                result.WwwAuthenticate,
                result.Xml);
        }

        return XmlaResponseParser.ParseDiscoverResponse(result.Xml);
    }

    public async Task<MdxQueryResult> ExecuteMdxAsync(
        string catalogName,
        string statement,
        string axisFormat = "ClusterFormat",
        string? content = null,
        XmlaDebugOptions? debugOptions = null,
        CancellationToken cancellationToken = default)
    {
        var request = XmlaEnvelopeBuilder.BuildExecuteEnvelope(
            _endpointUrl,
            catalogName,
            statement,
            axisFormat,
            content,
            _serverTimeoutSeconds);
        var result = await PostXmlaAsync(
            request,
            "\"urn:schemas-microsoft-com:xml-analysis:Execute\"",
            debugOptions,
            cancellationToken);

        if (result.StatusCode is < 200 or > 299)
        {
            throw new XmlaHttpException(
                $"XMLA Execute failed: {result.StatusCode} {result.ReasonPhrase}. " +
                $"WWW-Authenticate: {result.WwwAuthenticate}. Body: {TrimForError(result.Xml)}",
                result.StatusCode,
                result.ReasonPhrase,
                result.WwwAuthenticate,
                result.Xml);
        }

        return XmlaResponseParser.ParseMdxResponse(result.Xml);
    }

    private async Task<XmlaHttpResult> PostXmlaAsync(
        string request,
        string soapAction,
        XmlaDebugOptions? debugOptions,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        if (!string.IsNullOrWhiteSpace(debugOptions?.RequestOutputPath))
        {
            await WriteDebugXmlAsync(debugOptions.RequestOutputPath, request, cancellationToken);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
                message.Content = new StringContent(request, Encoding.UTF8, "text/xml");
                message.Headers.TryAddWithoutValidation("SOAPAction", soapAction);

                using var response = await _httpClient.SendAsync(message, cancellationToken);
                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(debugOptions?.ResponseOutputPath))
                {
                    await WriteDebugXmlAsync(debugOptions.ResponseOutputPath, xml, cancellationToken);
                }

                return new XmlaHttpResult(
                    (int)response.StatusCode,
                    response.ReasonPhrase ?? "",
                    response.Headers.WwwAuthenticate.ToString(),
                    xml);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransientTransportError(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }

    private static bool IsTransientTransportError(Exception exception)
    {
        var text = exception.ToString();
        return text.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connection reset", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("unexpected eof", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteDebugXmlAsync(string path, string xml, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, xml, Encoding.UTF8, cancellationToken);
    }

    private static string TrimForError(string value)
    {
        const int maxLength = 1200;
        return value.Length <= maxLength ? value : $"{value[..maxLength]}...";
    }

    private sealed record XmlaHttpResult(int StatusCode, string ReasonPhrase, string WwwAuthenticate, string Xml);
}

public sealed class XmlaHttpException : HttpRequestException
{
    public XmlaHttpException(string message, int statusCode, string reasonPhrase, string wwwAuthenticate, string body)
        : base(message)
    {
        StatusCodeNumber = statusCode;
        ReasonPhrase = reasonPhrase;
        WwwAuthenticate = wwwAuthenticate;
        Body = body;
    }

    public int StatusCodeNumber { get; }

    public string ReasonPhrase { get; }

    public string WwwAuthenticate { get; }

    public string Body { get; }
}
