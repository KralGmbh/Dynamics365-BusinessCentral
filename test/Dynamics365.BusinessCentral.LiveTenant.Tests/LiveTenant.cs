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
/// <b>Segment, not substring.</b> A substring test passes for <c>KRALTEST2</c> — a real
/// environment name shape, and one that would send live queries somewhere other than the intended
/// sandbox while the guard reported itself satisfied. Environment names are path segments, so the
/// check compares them as path segments.
/// </para>
/// </remarks>
public static class LiveTenant
{
    /// <summary>The only environment these tests may ever touch.</summary>
    public const string Environment = "KRALTEST";

    /// <summary>Company to scope the client to.</summary>
    public const string Company = "KRAL AG";

    /// <summary>Whether a tenant is configured. Drives <see cref="LiveTenantFactAttribute"/>.</summary>
    public static bool IsConfigured => LiveTenantCredentials.TryLoad() is not null;

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

        var segments = resolved.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment => segment.Equals(Environment, StringComparison.OrdinalIgnoreCase)))
            return;

        throw new InvalidOperationException(
            $"Live-tenant tests may only run against {Environment}. The resolved Business Central " +
            $"base URL does not name it as a path segment: '{resolved}'. This is a hard guardrail — " +
            "Production is read-only at all times. Fix the configuration; do not relax this check.");
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
