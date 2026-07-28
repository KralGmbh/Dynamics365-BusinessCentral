namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>Describes a request that started, succeeded, or is being reported on.</summary>
public sealed class BusinessCentralRequestInfo
{
    /// <summary>HTTP method of the request.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Full request URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Elapsed time, populated once the response arrived.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Response status code, populated once the response arrived.</summary>
    public int? StatusCode { get; init; }
}