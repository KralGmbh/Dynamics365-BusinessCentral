namespace Dynamics365.BusinessCentral.Options;

/// <summary>
/// Controls how the client retries throttled (<c>429</c>) and transient (<c>503</c>,
/// <c>504</c>, <c>408</c>) responses.
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
}
