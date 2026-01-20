using System.Text.Json;

namespace Dynamics365.BusinessCentral.Options;

public static class BusinessCentralJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
