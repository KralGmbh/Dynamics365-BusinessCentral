using System.Text.Json;

namespace Dynamics365.BusinessCentral.Options;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> used for every payload the client reads
/// or writes. Property-name resolution for filters and projections follows the same
/// settings, so typed field selectors always agree with deserialization.
/// </summary>
public static class BusinessCentralJson
{
    /// <summary>camelCase on write, case-insensitive on read.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
