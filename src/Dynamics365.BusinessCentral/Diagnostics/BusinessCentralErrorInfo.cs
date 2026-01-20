namespace Dynamics365.BusinessCentral.Diagnostics;

public sealed class BusinessCentralErrorInfo
{
    public string Method { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public Exception Exception { get; init; } = default!;

    public TimeSpan? Duration { get; init; }

    public int? StatusCode { get; init; }
    public string? ResponseBody { get; init; }

}
