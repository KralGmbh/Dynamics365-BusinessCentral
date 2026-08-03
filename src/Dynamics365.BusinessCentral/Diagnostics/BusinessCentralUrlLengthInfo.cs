namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>
/// Describes a request URL that crossed the configured length warning threshold.
/// </summary>
/// <remarks>
/// Raised before the request is sent, so it fires whether or not Business Central would
/// have accepted the URL. Its purpose is measurement: an OR-chained <c>Filter.In</c> grows
/// about twice as fast as the <c>in (...)</c> form it replaces, and the only way to
/// size chunking honestly is to observe real lengths in a real deployment. Aggregate
/// <see cref="Length"/> across a workload to find the true headroom before raising or
/// lowering <c>BusinessCentralOptions.MaxUrlLength</c>.
/// </remarks>
public sealed class BusinessCentralUrlLengthInfo
{
    /// <summary>The fully built request URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Length of <see cref="Url"/> in characters.</summary>
    public int Length { get; init; }

    /// <summary>
    /// The warning threshold that was crossed
    /// (<c>BusinessCentralOptions.UrlLengthWarningThreshold</c>).
    /// </summary>
    public int Threshold { get; init; }

    /// <summary>
    /// The hard limit (<c>BusinessCentralOptions.MaxUrlLength</c>), or
    /// <see langword="null"/> when no limit is configured.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Whether <see cref="Length"/> also exceeded <see cref="Limit"/>, meaning the request
    /// was rejected client-side rather than sent.
    /// </summary>
    public bool ExceedsLimit { get; init; }

    /// <summary>
    /// Number of <c>or</c> clauses detected in the URL. A high count on a long URL points
    /// at an OR-chained <c>Filter.In</c> as the cause — see
    /// <see cref="BusinessCentralUrlLengthInfo"/> remarks.
    /// </summary>
    public int OrClauseCount { get; init; }
}
