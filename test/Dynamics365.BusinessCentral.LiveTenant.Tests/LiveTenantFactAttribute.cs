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
/// Two conditions, not one: the run must be opted in via
/// <see cref="LiveTenant.OptInVariable"/> <i>and</i> have credentials. Credentials alone would
/// mean a solution-wide <c>dotnet test</c> on a developer's machine quietly became a live-tenant
/// run.
/// </para>
/// <para>
/// xUnit v2 evaluates <see cref="FactAttribute.Skip"/> at discovery, so this reads the
/// environment once per test-class construction rather than per run. That is fine: neither the
/// opt-in nor the credentials appear mid-run.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveTenantFactAttribute : FactAttribute
{
    public LiveTenantFactAttribute()
    {
        if (!LiveTenant.IsOptedIn)
        {
            Skip = $"Live-tenant facts are opt-in: set {LiveTenant.OptInVariable}=1 to run them. " +
                   "Skipped, not failed — see LiveTenantFactAttribute.";
            return;
        }

        if (LiveTenantCredentials.TryLoad() is null)
            Skip = $"{LiveTenant.OptInVariable} is set but no credentials were found " +
                   "(BC_TENANT_ID / BC_CLIENT_ID / BC_CLIENT_SECRET, or .env/dev-tenant.md). " +
                   "Skipped, not failed — see LiveTenantFactAttribute.";
    }
}
