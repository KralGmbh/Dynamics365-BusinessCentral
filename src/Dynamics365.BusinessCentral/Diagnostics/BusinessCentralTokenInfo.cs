namespace Dynamics365.BusinessCentral.Diagnostics;

/// <summary>Describes an access token that was acquired or served from cache.</summary>
public sealed class BusinessCentralTokenInfo
{
    /// <summary>UTC expiry, already reduced by the safety margin.</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Whether the token came from cache rather than a fresh request.</summary>
    public bool FromCache { get; init; }
}