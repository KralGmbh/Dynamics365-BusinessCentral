namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>
/// Describes a request that is about to be retried after a throttled or transient failure.
/// </summary>
public sealed class BusinessCentralRetryInfo
{
    /// <summary>HTTP method of the request being retried.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>URL of the request being retried.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Status code that triggered the retry.</summary>
    public int StatusCode { get; init; }

    /// <summary>1-based retry number: <c>1</c> is the first retry after the original attempt.</summary>
    public int Attempt { get; init; }

    /// <summary>How long the client will wait before retrying.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>
    /// Whether <see cref="Delay"/> came from the server's <c>Retry-After</c> header rather
    /// than from computed backoff.
    /// </summary>
    public bool FromRetryAfter { get; init; }
}
