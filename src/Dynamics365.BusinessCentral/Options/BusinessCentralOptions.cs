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
    /// Whether reads may be served from a Business Central database replica, sent as the
    /// <c>Data-Access-Intent</c> header. Defaults to
    /// <see cref="BusinessCentralDataAccessIntent.Unspecified"/> — no header, and the server
    /// uses whatever the page or query declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BusinessCentralDataAccessIntent.ReadOnly"/> is Microsoft's first listed
    /// client-performance recommendation: it lets Business Central answer from a replica,
    /// taking load off the primary database. Worth setting for reporting, synchronisation and
    /// any other read-dominated workload.
    /// </para>
    /// <para>
    /// <b>Opt-in on purpose.</b> Where a replica is genuinely used, replication lag means a
    /// read issued straight after a write may not observe it — so this is safe for a sync job
    /// and wrong for a read-after-write flow, and the package cannot tell which you are. The
    /// header is only ever sent on <c>GET</c>: Microsoft documents that modification requests
    /// reject <c>ReadOnly</c> outright, so applying it blindly would break every write.
    /// </para>
    /// <para>
    /// See <see href="https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/odata-client-performance">
    /// OData/API web service client performance</see>.
    /// </para>
    /// </remarks>
    public BusinessCentralDataAccessIntent DataAccessIntent { get; set; }

    /// <summary>
    /// Value for the <c>Accept-Language</c> header, e.g. <c>"en-US"</c> or <c>"de-DE"</c>.
    /// Defaults to <see langword="null"/> — no header, and Business Central uses the tenant
    /// default.
    /// </summary>
    /// <remarks>
    /// Controls the language of Business Central's error messages, which is what makes it
    /// worth setting: an integration that logs server errors wants them in one predictable
    /// language rather than whatever the tenant happens to be configured for. Microsoft notes
    /// it also governs regional formatting of responses.
    /// </remarks>
    public string? AcceptLanguage { get; set; }

    /// <summary>
    /// OData schema version to request, sent as <c>$schemaversion=</c> on queries. Defaults to
    /// <see langword="null"/> — no schema version is sent, and Business Central serves its
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this to <c>"2.1"</c> to enable the filter features Microsoft documents as requiring
    /// it. Two matter here:
    /// </para>
    /// <list type="bullet">
    /// <item>the <c>in</c> operator — <i>"In a list of values … Note: This only works in
    /// <c>$schemaversion=2.1</c>"</i>. Without it Business Central answers
    /// <c>BadRequest_MethodNotImplemented</c>, which is why <see cref="OData.ODataInStyle"/>
    /// defaults to an <c>or</c>-chain;</item>
    /// <item>nested function calls such as <c>contains(tolower(field), 'x')</c>, which on
    /// earlier schema versions either error or return undefined results.</item>
    /// </list>
    /// <para>
    /// <b>Required for <see cref="OData.ODataInStyle.Native"/> to work.</b> The two settings are
    /// a pair: asking for a native <c>in</c> without this is a request Business Central will
    /// reject.
    /// </para>
    /// <para>
    /// See <see href="https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/webservices/use-filter-expressions-in-odata-uris">
    /// Using Filter Expressions in OData URIs</see>. Verify against your own endpoint before
    /// relying on it — availability depends on the deployment and its version.
    /// </para>
    /// </remarks>
    public string? SchemaVersion { get; set; }

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
    /// Longest <b>query string</b> the client will build before refusing to send the request,
    /// in characters. Defaults to <c>8000</c>; set to <see langword="null"/> to disable the
    /// check entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The query string, not the whole URL, because that is what Business Central's gateway
    /// actually limits. Measured against a live SaaS tenant: the ceiling sits at <b>8,099</b>
    /// accepted query-string characters and is invariant across environments, while the full
    /// URL is not — the prefix moves with environment name, company name
    /// (<c>Company('KRAL%20AG')</c>, where the escaped space inflates it further) and
    /// entity-set path. A full-URL limit would be too strict on deployments with long prefixes
    /// and too loose on short ones.
    /// </para>
    /// <para>
    /// Past the server's own ceiling Business Central answers <c>414 URI Too Long</c>, which is
    /// not opaque. The value of failing client-side first is the diagnosis: this throws an
    /// <see cref="ArgumentException"/> naming the actual length, the limit, the <c>or</c>-clause
    /// count and <c>Filter.In</c> as the likely cause, before the request leaves the process.
    /// </para>
    /// <para>
    /// The usual cause is a bulk key lookup. <c>Filter.In</c> renders a same-field
    /// <c>or</c>-chain by default because Business Central gates the OData <c>in</c> operator on
    /// schema version 2.1, and <c>(no eq 'EBH00000') or </c> encodes to 38 characters per key
    /// against 17 for <c>'EBH00000',</c>. Setting <see cref="SchemaVersion"/> to <c>"2.1"</c> and
    /// passing <see cref="OData.ODataInStyle.Native"/> recovers essentially all of that.
    /// </para>
    /// <para>
    /// Server-issued <c>@odata.nextLink</c> continuations are never checked — the server
    /// produced them, so its own limits already applied.
    /// </para>
    /// <para>
    /// <b>Measured on one tenant and one gateway.</b> The default leaves headroom under 8,099
    /// rather than sitting on it; raise it if your deployment demonstrably accepts more.
    /// </para>
    /// </remarks>
    public int? MaxQueryStringLength { get; set; } = 8000;

    /// <summary>
    /// Query-string length, in characters, at which
    /// <c>IBusinessCentralObserver.OnUrlLengthWarning</c> is raised. Defaults to <c>6000</c>;
    /// set to <see langword="null"/> to disable the warning. Must not exceed
    /// <see cref="MaxQueryStringLength"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately below <see cref="MaxQueryStringLength"/>: the gap is the measurement
    /// window. A request in it is sent normally and observed, so a deployment can discover the
    /// length distribution its real workload produces — and size chunking against evidence —
    /// without a single working query being turned into an exception.
    /// </remarks>
    public int? QueryStringLengthWarningThreshold { get; set; } = 6000;

    /// <summary>
    /// How <c>Filter.In</c> renders when the call site leaves it at
    /// <see cref="OData.ODataInStyle.Auto"/>. Defaults to <see cref="OData.ODataInStyle.Auto"/>,
    /// which follows <see cref="SchemaVersion"/>.
    /// </summary>
    /// <remarks>
    /// Set <see cref="OData.ODataInStyle.OrChain"/> to keep the portable rendering even on a
    /// 2.1 endpoint, or <see cref="OData.ODataInStyle.Native"/> to force <c>in</c> on a
    /// deployment whose schema version this package cannot infer. Per-call styles always win
    /// over this.
    /// </remarks>
    public OData.ODataInStyle InStyle { get; set; } = OData.ODataInStyle.Auto;

    /// <summary>
    /// Whether membership filters left at <see cref="OData.ODataInStyle.Auto"/> should render
    /// as the native <c>in</c> operator.
    /// </summary>
    /// <remarks>
    /// Business Central gates <c>in</c> on schema version 2.1, so the honest signal that an
    /// endpoint accepts it is the caller having asked for that version. Parsed rather than
    /// compared as a string so <c>"2.10"</c> and future versions behave sensibly, and so a
    /// value this package cannot parse falls back to the portable rendering rather than
    /// guessing.
    /// </remarks>
    internal bool UseNativeIn => InStyle switch
    {
        OData.ODataInStyle.Native => true,
        OData.ODataInStyle.OrChain => false,
        _ => decimal.TryParse(
                 SchemaVersion,
                 System.Globalization.NumberStyles.Number,
                 System.Globalization.CultureInfo.InvariantCulture,
                 out var version) && version >= 2.1m
    };

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
