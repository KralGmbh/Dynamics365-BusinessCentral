namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>Describes a failed request or a failed deserialization.</summary>
public sealed class BusinessCentralErrorInfo
{
    /// <summary>HTTP method of the request.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Full request URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The failure.</summary>
    public Exception Exception { get; init; } = default!;

    /// <summary>Elapsed time, when the failure happened after a response arrived.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Response status code, when one was received.</summary>
    public int? StatusCode { get; init; }
    /// <summary>Raw response body, when one was read.</summary>
    public string? ResponseBody { get; init; }

}
