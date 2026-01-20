namespace Dynamics365.BusinessCentral.Diagnostics;

public sealed class BusinessCentralTokenInfo
{
    public DateTime ExpiresAt { get; init; }

    public bool FromCache { get; init; }
}