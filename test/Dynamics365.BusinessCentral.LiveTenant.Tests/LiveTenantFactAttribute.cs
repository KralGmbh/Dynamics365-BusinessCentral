namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// A fact that needs a live Business Central tenant, and <b>skips</b> — rather than fails — when
/// none is configured.
/// </summary>
/// <remarks>
/// <para>
/// Skipping is the whole point. A contributor without tenant credentials must not see permanent
/// red, or the suite trains people to ignore it. The trade is that an unconfigured CI job reports
/// green while proving nothing, so the workflow that runs these asserts the credentials are
/// present before invoking the suite — see <c>.github/workflows/live-tenant.yml</c>.
/// </para>
/// <para>
/// xUnit v2 evaluates <see cref="FactAttribute.Skip"/> at discovery, so this reads the
/// credentials once per test-class construction rather than per run. That is fine: credentials
/// do not appear mid-run.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveTenantFactAttribute : FactAttribute
{
    public LiveTenantFactAttribute()
    {
        if (!LiveTenant.IsConfigured)
            Skip = "No live-tenant credentials configured (BC_TENANT_ID / BC_CLIENT_ID / " +
                   "BC_CLIENT_SECRET, or .env/dev-tenant.md). Skipped, not failed — see " +
                   "LiveTenantFactAttribute.";
    }
}
