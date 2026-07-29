namespace Dynamics365.BusinessCentral;

/// <summary>
/// Names of the <see cref="System.Net.Http.IHttpClientFactory"/> clients this package
/// registers, so consumers can address them from their own HTTP configuration — most
/// commonly to exempt them from a global resilience handler.
/// </summary>
/// <remarks>
/// The package's built-in retry honours <c>Retry-After</c> and refuses to replay a
/// <c>POST</c> on ambiguous transient failures. A generic outer handler (e.g.
/// <c>AddStandardResilienceHandler</c> applied via <c>ConfigureHttpClientDefaults</c>)
/// does neither, and the two compose multiplicatively. Prefer disabling the outer handler
/// for these two clients over disabling <c>Retry</c> here.
/// </remarks>
public static class BusinessCentralHttpClients
{
    /// <summary>Name of the client used for OAuth2 token acquisition.</summary>
    public const string Token = "Dynamics365.BusinessCentral.Token";

    /// <summary>Name of the client used for data requests.</summary>
    public const string Client = "Dynamics365.BusinessCentral.Client";
}
