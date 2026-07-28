namespace Dynamics365.BusinessCentral.Options;

/// <summary>
/// Connection settings for a Business Central environment.
/// </summary>
/// <remarks>
/// Only <see cref="TenantId"/>, <see cref="ClientId"/>, <see cref="ClientSecret"/> and
/// <see cref="Company"/> are required. <see cref="BaseUrl"/> and <see cref="TokenEndpoint"/>
/// default to the Business Central SaaS endpoints and support the placeholders
/// <c>{tenant}</c> and <c>{environment}</c>, which are substituted from
/// <see cref="TenantId"/> and <see cref="Environment"/>.
/// </remarks>
public sealed class BusinessCentralOptions
{
    /// <summary>Microsoft Entra tenant ID (GUID) that owns the Business Central subscription.</summary>
    public string TenantId { get; set; } = default!;

    /// <summary>Application (client) ID of the Entra app registration used for authentication.</summary>
    public string ClientId { get; set; } = default!;

    /// <summary>Client secret of the Entra app registration.</summary>
    public string ClientSecret { get; set; } = default!;

    /// <summary>Display name of the Business Central company, e.g. <c>CRONUS AG</c>.</summary>
    public string Company { get; set; } = default!;

    /// <summary>
    /// Business Central environment name. Substituted for <c>{environment}</c> in
    /// <see cref="BaseUrl"/>. Defaults to <c>Production</c>.
    /// </summary>
    public string Environment { get; set; } = "Production";

    /// <summary>
    /// OData service root. Supports the <c>{tenant}</c> and <c>{environment}</c> placeholders.
    /// Defaults to the Business Central SaaS OData v4 endpoint.
    /// </summary>
    public string BaseUrl { get; set; } =
        "https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/ODataV4";

    /// <summary>OAuth2 scope requested for the access token.</summary>
    public string Scope { get; set; } = "https://api.businesscentral.dynamics.com/.default";

    /// <summary>
    /// OAuth2 token endpoint. Supports the <c>{tenant}</c> placeholder.
    /// </summary>
    public string TokenEndpoint { get; set; } =
        "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";

    /// <summary>Controls automatic retrying of throttled and transient failures.</summary>
    public BusinessCentralRetryOptions Retry { get; set; } = new();

    /// <summary><see cref="BaseUrl"/> with all placeholders substituted.</summary>
    internal string ResolvedBaseUrl => ResolvePlaceholders(BaseUrl);

    /// <summary><see cref="TokenEndpoint"/> with all placeholders substituted.</summary>
    internal string ResolvedTokenEndpoint => ResolvePlaceholders(TokenEndpoint);

    private string ResolvePlaceholders(string template)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        return template
            // {TenantId} is the historical spelling and stays supported.
            .Replace("{TenantId}", TenantId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{tenant}", TenantId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{environment}", Environment ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
