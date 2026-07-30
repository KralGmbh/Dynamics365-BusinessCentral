namespace Dynamics365.BusinessCentral.Testing;

/// <summary>
/// One request the code under test sent through <see cref="FakeBusinessCentral"/>.
/// Token-endpoint traffic is counted separately and never appears here.
/// </summary>
public sealed class RecordedBusinessCentralRequest
{
    internal RecordedBusinessCentralRequest(string method, Uri uri, string? body)
    {
        Method = method;
        Uri = uri;
        Body = body;
    }

    /// <summary>HTTP method, e.g. <c>GET</c>.</summary>
    public string Method { get; }

    /// <summary>Full request URI.</summary>
    public Uri Uri { get; }

    /// <summary>Request body as sent, or <see langword="null"/> for bodyless requests.</summary>
    public string? Body { get; }

    /// <summary>
    /// Path and query as sent on the wire, e.g.
    /// <c>/Company('TEST')/items?$filter=no%20eq%20'X'</c>.
    /// </summary>
    public string PathAndQuery => Uri.PathAndQuery;

    /// <summary>
    /// <see cref="PathAndQuery"/> with percent-encoding undone — the readable form for
    /// assertions: <c>/Company('TEST')/items?$filter=no eq 'X'</c>.
    /// </summary>
    public string DecodedPathAndQuery => System.Uri.UnescapeDataString(Uri.PathAndQuery);
}
