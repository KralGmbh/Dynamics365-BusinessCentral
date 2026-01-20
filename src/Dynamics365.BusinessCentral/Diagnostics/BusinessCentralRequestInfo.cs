namespace Dynamics365.BusinessCentral.Diagnostics;

public sealed class BusinessCentralRequestInfo
{
    public string Method { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public TimeSpan? Duration { get; init; }

    public int? StatusCode { get; init; }
}