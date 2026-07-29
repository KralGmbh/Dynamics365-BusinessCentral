namespace Dynamics365.BusinessCentral.Options;

/// <summary>
/// Controls how the client retries throttled (<c>429</c>) and transient (<c>408</c>,
/// <c>502</c>, <c>503</c>, <c>504</c>) responses, as well as connection-level failures
/// and client-side timeouts where no response arrived at all.
/// </summary>
/// <remarks>
/// Business Central throttles aggressively and answers with a <c>Retry-After</c> header;
/// honouring it is almost always the correct behaviour. This is separate from the
/// single automatic retry performed after a <c>401</c>, which is not configurable.
/// </remarks>
public sealed class BusinessCentralRetryOptions
{
    /// <summary>Set to <see langword="false"/> to surface transient failures immediately.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Total number of attempts, including the first. <c>3</c> means the original request
    /// plus two retries. Values below <c>1</c> are treated as <c>1</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay before the first retry; doubles on each subsequent attempt.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound for a single delay, including one taken from <c>Retry-After</c>.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/> (the default) a <c>Retry-After</c> header takes precedence
    /// over the computed backoff, capped by <see cref="MaxDelay"/>.
    /// </summary>
    public bool HonorRetryAfter { get; set; } = true;

    /// <summary>
    /// How much random spread is added to every retry delay, as a fraction of the delay:
    /// the actual wait is <c>delay + random(0, delay × JitterFactor)</c>, still capped by
    /// <see cref="MaxDelay"/>. Defaults to <c>0.2</c>; set <c>0</c> for deterministic delays.
    /// </summary>
    /// <remarks>
    /// Without jitter, every caller that is throttled at the same moment retries at the same
    /// moment — Business Central hands all of them the same <c>Retry-After</c> — and they
    /// re-throttle each other in lockstep. The spread is <b>added</b>, never subtracted: a
    /// <c>Retry-After</c> is a minimum wait, and retrying early guarantees another <c>429</c>.
    /// </remarks>
    public double JitterFactor { get; set; } = 0.2;

    /// <summary>
    /// Whether a <c>POST</c> may be replayed after a transient failure <i>other than</i>
    /// <c>429</c>. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>429</c> means the request was rejected before it was processed, so replaying it is
    /// always safe and happens regardless of this setting.
    /// </para>
    /// <para>
    /// <c>408</c>, <c>502</c>, <c>503</c> and <c>504</c> are ambiguous: Business Central may
    /// have applied the write before the failure surfaced. Replaying a <c>POST</c> would then
    /// create a duplicate record, so by default it is not retried and the exception is raised
    /// for the caller to handle. Idempotent methods — <c>GET</c>, <c>PUT</c>, <c>DELETE</c> —
    /// are always retried, because replaying them converges on the same state. Connection
    /// failures and client-side timeouts are equally ambiguous — the request may have reached
    /// the server even though no response arrived — and follow the same rules.
    /// </para>
    /// <para>
    /// Set this to <see langword="true"/> only when the endpoint you POST to deduplicates
    /// server-side, or when duplicates are acceptable.
    /// </para>
    /// <para>
    /// This setting covers <c>POST</c> only. <c>PATCH</c> is always replayed, because this
    /// client sends a JSON merge of absolute field values, which converges when applied
    /// twice. Disable retries entirely, or pass a real <c>If-Match</c> ETag, if you need a
    /// <c>PATCH</c> held back.
    /// </para>
    /// </remarks>
    public bool RetryPostOnTransientFailures { get; set; }
}
