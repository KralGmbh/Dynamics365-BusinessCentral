namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>
/// Describes a request whose query string crossed the configured length warning threshold.
/// </summary>
/// <remarks>
/// Raised before the request is sent, so it fires whether or not Business Central would have
/// accepted it. Its purpose is measurement: an OR-chained <c>Filter.In</c> is about twice the
/// encoded width of the <c>in (...)</c> form it substitutes for, and the honest way to size
/// chunking is to observe real lengths in a real deployment. Aggregate
/// <see cref="QueryStringLength"/> across a workload to find the true headroom before raising
/// or lowering <c>BusinessCentralOptions.MaxQueryStringLength</c>.
/// </remarks>
public sealed class BusinessCentralUrlLengthInfo
{
    /// <summary>The fully built request URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Length of <see cref="Url"/> in characters, including scheme, host and path.</summary>
    public int UrlLength { get; init; }

    /// <summary>
    /// Length of everything after the first <c>?</c> — <b>the measured quantity</b>, because
    /// that is the part Business Central's gateway limits. Unlike the full URL it does not move
    /// with environment name, company name or entity-set path, which is what makes a portable
    /// default possible.
    /// </summary>
    public int QueryStringLength { get; init; }

    /// <summary>
    /// The warning threshold that was crossed
    /// (<c>BusinessCentralOptions.QueryStringLengthWarningThreshold</c>).
    /// </summary>
    public int Threshold { get; init; }

    /// <summary>
    /// The hard limit (<c>BusinessCentralOptions.MaxQueryStringLength</c>), or
    /// <see langword="null"/> when no limit is configured.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Whether <see cref="QueryStringLength"/> also exceeded <see cref="Limit"/>, meaning the
    /// request was rejected client-side rather than sent.
    /// </summary>
    public bool ExceedsLimit { get; init; }

    /// <summary>
    /// Number of <c>or</c> clauses detected in the URL. A high count on a long query string
    /// points at an OR-chained <c>Filter.In</c> as the cause — see
    /// <see cref="BusinessCentralUrlLengthInfo"/> remarks.
    /// </summary>
    public int OrClauseCount { get; init; }
}
