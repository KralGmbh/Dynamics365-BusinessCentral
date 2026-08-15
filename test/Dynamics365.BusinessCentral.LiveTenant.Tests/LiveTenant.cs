using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.Options;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// Builds a Business Central client for the live-tenant facts, and refuses to build one that
/// could reach any environment other than the sandbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>The primary guard is not here.</b> It is in Business Central: the app registration these
/// tests use is granted on the sandbox environment only and deliberately never added to
/// Production, so a token issued for it is rejected at Production's door. That is a credential
/// boundary, and it holds regardless of what this code does.
/// </para>
/// <para>
/// This class is the second line. Its value is failing <i>before</i> a request leaves the
/// process, with a message that says why, instead of an opaque 401 — and catching the case where
/// someone points this at the wrong environment while holding a credential that happens to work
/// there.
/// </para>
/// <para>
/// <b>Fail closed.</b> The check requires the sandbox to be <i>present</i> in the resolved base
/// URL, as a whole path segment. It deliberately does not test for the absence of "Production": a
/// renamed or added environment must fail, not slip through. And it asserts on the <i>resolved</i>
/// URL rather than <see cref="BusinessCentralOptions.Environment"/>, because <c>BaseUrl</c> can be
/// set wholesale — in which case the placeholder is never substituted and <c>Environment</c> is
/// decorative.
/// </para>
/// <para>
/// <b>Position, not presence.</b> Two weaker versions of this check were wrong in the same
/// direction. A substring test passes for <c>KRALTEST2</c>, a realistic name for a second sandbox.
/// Accepting the name in <i>any</i> path segment passes for a URL that merely carries it
/// somewhere harmless — <c>https://proxy/KRALTEST/v2.0/{tenant}/Production/ODataV4</c> would have
/// satisfied it while pointing squarely at Production. So the check reads the environment out of
/// the position Business Central actually puts it in: the segment immediately before
/// <c>ODataV4</c> in the service root.
/// </para>
/// <para>
/// Anything that is not that shape is rejected rather than interpreted. A base URL this code
/// cannot parse is one whose environment it cannot verify, and the whole point of the guard is
/// that "cannot verify" and "verified safe" must not be the same answer.
/// </para>
/// </remarks>
public static class LiveTenant
{
    /// <summary>The only environment these tests may ever touch.</summary>
    public const string Environment = "KRALTEST";

    /// <summary>Company to scope the client to.</summary>
    public const string Company = "KRAL AG";

    /// <summary>
    /// The environment variable that opts a run in to contacting the tenant.
    /// </summary>
    /// <remarks>
    /// Credentials alone are not enough, and that is the point. This project is part of the
    /// solution so it stays visible and maintained, which means <c>dotnet test</c> over the
    /// solution discovers it — and on a machine that has a <c>.env/dev-tenant.md</c>, an ordinary
    /// unit-test run would then send the whole suite at the tenant and make routine local testing
    /// depend on external state. Requiring a second, deliberate signal keeps the default run
    /// offline and fast.
    /// </remarks>
    public const string OptInVariable = "BC_LIVE_TENANT_TESTS";

    /// <summary>
    /// Whether this run may contact the tenant: opted in <i>and</i> configured. Drives
    /// <see cref="LiveTenantFactAttribute"/>.
    /// </summary>
    public static bool IsConfigured =>
        IsOptedIn && LiveTenantCredentials.TryLoad() is not null;

    /// <summary>
    /// Whether this is the scheduled workflow run rather than someone's machine.
    /// </summary>
    /// <remarks>
    /// Used only to turn "this run could not discriminate" from a printed note into a failure —
    /// see <c>DateFilterTests</c>. A contributor running under UTC has nothing to fix; the
    /// workflow, which sets <c>TZ</c> precisely so its run does discriminate, does.
    /// <c>GITHUB_ACTIONS</c> rather than the broader <c>CI</c>: the condition being detected is
    /// specifically "this is the job that <c>live-tenant.yml</c> configures", and a shell that
    /// happens to export <c>CI</c> is not that.
    /// </remarks>
    public static bool IsGitHubActions =>
        string.Equals(
            System.Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the opt-in variable is set to something other than a falsy value.</summary>
    public static bool IsOptedIn =>
        System.Environment.GetEnvironmentVariable(OptInVariable) is { Length: > 0 } value &&
        !value.Equals("0", StringComparison.Ordinal) &&
        !value.Equals("false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A client for the sandbox, or <see langword="null"/> when no credentials are configured.
    /// </summary>
    /// <param name="configure">
    /// Applied before the environment guard, so a test cannot configure its way past it.
    /// </param>
    public static IBusinessCentralClient? TryCreateClient(
        Action<BusinessCentralOptions>? configure = null,
        Diagnostics.IBusinessCentralObserver? observer = null)
    {
        var credentials = LiveTenantCredentials.TryLoad();
        if (credentials is null)
            return null;

        var options = new BusinessCentralOptions
        {
            TenantId = credentials.TenantId,
            ClientId = credentials.ClientId,
            ClientSecret = credentials.ClientSecret,
            Company = Company,
            Environment = Environment,
        };

        configure?.Invoke(options);

        GuardEnvironment(options);

        // Fully qualified: this project's namespace nests inside Dynamics365.BusinessCentral, so
        // a bare `Options` binds to the package's Options namespace, not the extensions type.
        return new BusinessCentralClient(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(options),
            observer);
    }

    /// <summary>
    /// Throws unless the resolved base URL names <see cref="Environment"/>. Public so the rule
    /// is assertable directly, without a tenant.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The resolved URL does not name the sandbox. Never downgrade this to a warning.
    /// </exception>
    public static void GuardEnvironment(BusinessCentralOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // ResolvedBaseUrl is internal to the package and this project is a consumer, so the same
        // substitution is repeated here (see BusinessCentralOptions.ResolvePlaceholders).
        var resolved = (options.BaseUrl ?? string.Empty)
            .Replace("{TenantId}", options.TenantId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{tenant}", options.TenantId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{environment}", options.Environment ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (EnvironmentOf(resolved) is { } environment &&
            environment.Equals(Environment, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Live-tenant tests may only run against {Environment}. The resolved Business Central " +
            $"base URL does not carry it in the environment position — expected " +
            $"'.../{{tenant}}/{Environment}/ODataV4', got '{resolved}'. This is a hard guardrail — " +
            "Production is read-only at all times. Fix the configuration; do not relax this check.");
    }

    /// <summary>
    /// The environment named by an OData service root, or <see langword="null"/> when the URL is
    /// not one — including when it is not a well-formed absolute URL at all.
    /// </summary>
    /// <remarks>
    /// Business Central's service root is
    /// <c>https://{host}/v2.0/{tenant}/{environment}/ODataV4</c>, so the environment is the
    /// segment immediately before <c>ODataV4</c>. Returning null for every other shape is what
    /// makes the caller fail closed.
    /// </remarks>
    private static string? EnvironmentOf(string resolvedBaseUrl)
    {
        if (!Uri.TryCreate(resolvedBaseUrl, UriKind.Absolute, out var uri))
            return null;

        var segments = uri.Segments
            .Select(segment => segment.Trim('/'))
            .Where(segment => segment.Length > 0)
            .ToArray();

        // The service root must *end* at ODataV4. Finding it anywhere else would re-admit the
        // proxy-prefix case this method exists to reject.
        if (segments.Length < 2 || !segments[^1].Equals("ODataV4", StringComparison.OrdinalIgnoreCase))
            return null;

        return segments[^2];
    }

    /// <summary>
    /// The client, or a skip-shaped failure. Every fact starts here; the attribute has already
    /// established that credentials exist, so a null at this point is a real fault.
    /// </summary>
    public static IBusinessCentralClient CreateClient(
        Action<BusinessCentralOptions>? configure = null,
        Diagnostics.IBusinessCentralObserver? observer = null) =>
        TryCreateClient(configure, observer)
        ?? throw new InvalidOperationException(
            "No live-tenant credentials. This fact should have been skipped — see " +
            nameof(LiveTenantFactAttribute) + ".");
}
