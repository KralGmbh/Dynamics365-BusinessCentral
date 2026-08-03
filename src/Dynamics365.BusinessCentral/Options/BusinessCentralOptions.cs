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

    /// <summary>
    /// Per-attempt timeout applied to the data <see cref="HttpClient"/> registered by
    /// <c>AddBusinessCentral</c>. Defaults to <see langword="null"/> — the
    /// <see cref="HttpClient"/> default of 100 seconds. A timeout surfaces as
    /// <c>BusinessCentralConnectionException</c> and is retried under the normal replay
    /// rules.
    /// </summary>
    /// <remarks>
    /// Applies per attempt, not per logical call — budget for
    /// <c>Retry.MaxAttempts × (RequestTimeout + backoff)</c> when composing with an outer
    /// execution timeout. Only honoured on the DI registration path; a manually
    /// constructed client owns its <see cref="HttpClient"/> and its timeout.
    /// </remarks>
    public TimeSpan? RequestTimeout { get; set; }

    /// <summary>
    /// Whether a fluent query with no explicit projection derives its <c>$select</c> from the
    /// entity type. Defaults to <see langword="true"/>. Set to <see langword="false"/> to
    /// restore pre-2.0 behaviour — no <c>$select</c>, every column returned — for every query
    /// at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registration-level counterpart to the per-query <c>SelectAll()</c>. It exists so a
    /// consumer whose entity classes are broad shared types, rather than per-use projections,
    /// can opt out in one line instead of at every call site.
    /// </para>
    /// <para>
    /// The default is on because deriving the projection is the feature, and an unflipped flag
    /// ships nothing. The risk it carries is narrow and loud — a property mapping to no
    /// Business Central column fails the query with a <c>400</c> naming the field — and
    /// <c>BusinessCentralMetadata.AssertProjectionsResolveAsync</c> in the Testing package
    /// turns that into a build-time check. Explicit <c>Select(...)</c> is unaffected either way.
    /// </para>
    /// </remarks>
    public bool DeriveSelect { get; set; } = true;

    /// <summary>
    /// Longest request URL the client will build before refusing to send it, in characters.
    /// Defaults to <c>4000</c>; set to <see langword="null"/> to disable the check entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing about a URL that is merely long is invalid — this guards the point past which
    /// Business Central (or the IIS/gateway in front of it, whose own defaults are a 4,096-byte
    /// URL and a 2,048-byte query string) answers with an opaque <c>400</c> or <c>404</c> that
    /// never mentions length. The client turns that into an <see cref="ArgumentException"/>
    /// naming the actual length, the limit and the likely cause, thrown before the request
    /// leaves the process.
    /// </para>
    /// <para>
    /// <b>The default of 4,000 is an estimate, not a measurement.</b> Unlike the rest of this
    /// package's defaults it was reasoned from those IIS limits rather than observed against a
    /// tenant, because the real ceiling depends on the deployment and on whatever sits in front
    /// of it. That is what <see cref="UrlLengthWarningThreshold"/> is for: measure your own
    /// workload, then set this from evidence — or to <see langword="null"/> if your deployment
    /// accepts more.
    /// </para>
    /// <para>
    /// The usual cause is a bulk key lookup: <c>Filter.In</c> renders a same-field <c>or</c>-chain
    /// because Business Central rejects the OData <c>in</c> operator, and <c>(no eq 'EBH100') or </c>
    /// costs about twice what <c>'EBH100',</c> would once percent-encoded (38 characters against 17).
    /// Chunk the values.
    /// </para>
    /// <para>
    /// Server-issued <c>@odata.nextLink</c> continuations are never checked — the server
    /// produced them, so its own limits already apply.
    /// </para>
    /// </remarks>
    public int? MaxUrlLength { get; set; } = 4000;

    /// <summary>
    /// URL length, in characters, at which <c>IBusinessCentralObserver.OnUrlLengthWarning</c>
    /// is raised. Defaults to <c>2000</c>; set to <see langword="null"/> to disable the
    /// warning. Must not exceed <see cref="MaxUrlLength"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately well below <see cref="MaxUrlLength"/>: the gap is the measurement window.
    /// A URL in it is sent normally and observed, so a deployment can discover the length
    /// distribution its real workload produces — and size chunking against evidence — without
    /// a single working query being turned into an exception.
    /// </remarks>
    public int? UrlLengthWarningThreshold { get; set; } = 2000;

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
