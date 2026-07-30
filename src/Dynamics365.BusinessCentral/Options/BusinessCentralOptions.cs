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

    /// <summary>
    /// Maximum rows per page requested on streaming reads (<c>QueryAllAsync</c>,
    /// <c>QueryStreamAsync</c>, the fluent <c>StreamAsync</c>/<c>ToAllAsync</c>), sent as
    /// <c>Prefer: odata.maxpagesize={value}</c>. Defaults to <see langword="null"/> —
    /// <b>no preference is sent</b>, and Business Central pages at its own configured
    /// Max Page Size (20,000 online; the server setting on-premises), driving continuation
    /// via <c>@odata.nextLink</c>.
    /// </summary>
    /// <remarks>
    /// The server clamps the preference to its own maximum, so a large value cannot raise
    /// a deployment's ceiling — this can only ask for <i>smaller</i> pages, e.g. to bound
    /// per-response memory or avoid timeouts on slow pages. Override per query with
    /// <c>QueryOptions.WithPageSize</c> / the fluent <c>PageSize(n)</c>.
    /// </remarks>
    public int? MaxPageSize { get; set; }

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
