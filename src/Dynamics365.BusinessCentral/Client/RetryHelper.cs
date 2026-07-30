using Dynamics365.BusinessCentral.Options;

namespace Dynamics365.BusinessCentral.Client;

/// <summary>
/// Retry mechanics shared by the data pipeline (<see cref="BusinessCentralClient"/>) and
/// token acquisition (<see cref="BusinessCentralTokenProvider"/>): delay computation with
/// jitter, and classification of network-level failures.
/// </summary>
internal static class RetryHelper
{
    /// <summary>
    /// A server-supplied <c>Retry-After</c> wins over computed backoff; otherwise the delay
    /// doubles per attempt. Both are jittered by <see cref="BusinessCentralRetryOptions.JitterFactor"/>
    /// and capped by <see cref="BusinessCentralRetryOptions.MaxDelay"/>.
    /// </summary>
    public static TimeSpan ComputeDelay(
        BusinessCentralRetryOptions retry,
        TimeSpan? retryAfter,
        int attempt)
    {
        var max = Floor(retry.MaxDelay);

        TimeSpan baseline;

        if (retry.HonorRetryAfter && retryAfter is { } requested)
        {
            baseline = Clamp(requested, max);
        }
        else
        {
            var milliseconds = Floor(retry.BaseDelay).TotalMilliseconds * Math.Pow(2, attempt - 1);

            // A large BaseDelay or a high attempt count overflows to a value TimeSpan cannot
            // represent — or to Infinity — and TimeSpan.FromMilliseconds throws on both. Compare
            // in double space first so a transient failure never becomes a crash.
            baseline = double.IsNaN(milliseconds) || milliseconds >= max.TotalMilliseconds
                ? max
                : Clamp(TimeSpan.FromMilliseconds(milliseconds), max);
        }

        return AddJitter(baseline, retry.JitterFactor, max);
    }

    /// <summary>
    /// Spreads the delay by a random amount in <c>[0, delay × factor]</c> so concurrent
    /// failures do not retry in lockstep and re-throttle each other.
    /// </summary>
    /// <remarks>
    /// The jitter is <b>additive only</b>. A <c>Retry-After</c> is a minimum wait — retrying
    /// earlier than the server asked guarantees another <c>429</c> — so spreading can only
    /// ever extend the delay. Still capped by <c>MaxDelay</c>, which means a baseline already
    /// at the cap cannot be spread; that is the cost of keeping <c>MaxDelay</c> a hard bound.
    /// </remarks>
    private static TimeSpan AddJitter(TimeSpan delay, double factor, TimeSpan max)
    {
        // JitterFactor is public input that may arrive via configuration binding — a NaN
        // never compares true, so it must be rejected explicitly or it slips past <= 0.
        if (double.IsNaN(factor) || factor <= 0 || delay <= TimeSpan.Zero)
            return delay;

        var extra = delay.TotalMilliseconds * factor * Random.Shared.NextDouble();

        // A huge or infinite factor overflows `extra` to Infinity — or to NaN when the
        // random draw is exactly 0 — and TimeSpan.FromMilliseconds throws on both. The
        // jittered delay can never exceed MaxDelay anyway, so compare in double space
        // first, same as the backoff overflow guard in ComputeDelay.
        if (double.IsNaN(extra) || delay.TotalMilliseconds + extra >= max.TotalMilliseconds)
            return max;

        return Clamp(delay + TimeSpan.FromMilliseconds(extra), max);
    }

    /// <summary>
    /// Whether the send failed without any response arriving: a connection-level error, or
    /// the <see cref="HttpClient"/> timeout. A cancellation requested through the caller's
    /// token is not a network failure and propagates as-is.
    /// </summary>
    public static bool IsNetworkFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>
    /// Single-line failure description. <paramref name="target"/> names what was being
    /// called — "Business Central" for data requests, "the token endpoint" for token
    /// acquisition — so a connectivity problem at login.microsoftonline.com is not
    /// misattributed to Business Central itself.
    /// </summary>
    public static string NetworkFailureMessage(Exception ex, string target) =>
        ex is TaskCanceledException
            ? $"The request timed out before {target} responded."
            : $"The connection to {target} failed: {ex.Message}";

    private static TimeSpan Floor(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static TimeSpan Clamp(TimeSpan value, TimeSpan max)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;

        return value > max ? max : value;
    }
}
