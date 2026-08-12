using System.Text.Json;
using Dynamics365.BusinessCentral.Testing;
using Xunit.Abstractions;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// The derived <c>$select</c> (F2) and the casing behaviour it was once wrongly documented
/// against.
/// </summary>
public sealed class ProjectionTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every annotated entity type in this assembly projects onto columns that actually exist on
    /// the tenant.
    /// </summary>
    /// <remarks>
    /// This is the shipped M4 validator run against a real <c>$metadata</c> document — the one
    /// check a transport fake structurally cannot perform, pointed at the package's own entity
    /// types. It closes the loop for this suite: a future test entity with a typo'd column fails
    /// here, naming the property, instead of failing somewhere else with a bare 400.
    /// </remarks>
    [LiveTenantFact]
    public async Task Derived_projections_resolve_against_the_tenants_metadata()
    {
        var client = LiveTenant.CreateClient();

        var report = await BusinessCentralMetadata.ValidateAssemblyAsync(
            client, typeof(LdatSummaryRow).Assembly);

        output.WriteLine(report.Describe());

        // Skipped types would make this fact quietly vacuous: a suite that checks nothing reports
        // exactly the same green as one that checks everything.
        Assert.NotEmpty(report.Checked);
        Assert.Empty(report.Skipped);
        Assert.True(report.IsValid, report.Describe());
    }

    /// <summary>
    /// <c>$select</c> is case-insensitive, and the server answers in its own canonical casing
    /// whatever casing was asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the M1 finding, pinned. Alpha.6 documented <c>$select</c> as case-<i>sensitive</i>
    /// and alpha.7 baked that claim into an exception hint; a live probe falsified it, and the
    /// claim has since drifted back into the docs twice. The unit suite guards the wording
    /// (<c>Hint_Makes_No_Case_Sensitivity_Claim</c>); only this fact guards the underlying
    /// behaviour.
    /// </para>
    /// <para>
    /// <c>LdatSummary</c> is the right page for it because it answers <c>SystemId</c> in
    /// PascalCase while its siblings answer <c>systemId</c> — so asking in the "wrong" casing here
    /// is not hypothetical.
    /// </para>
    /// <para>
    /// Note what is <b>not</b> claimed: that Business Central is case-insensitive in general. This
    /// is one SaaS tenant. The on-prem OData stack is unmeasured, which is why the package's
    /// column matching is <c>OrdinalIgnoreCase</c> rather than the docs asserting a rule.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task Select_is_case_insensitive_and_answered_in_the_pages_own_casing()
    {
        var client = LiveTenant.CreateClient();

        // Deliberately the wrong casing for both: the page answers SystemId / serialNo.
        var response = await client.QueryRawAsync<JsonElement>(
            "LdatSummary?$select=SYSTEMID,SERIALNO&$top=1");

        var rows = response.GetProperty("value");
        Assert.True(rows.GetArrayLength() > 0, "Expected at least one row.");

        var names = rows[0].EnumerateObject().Select(p => p.Name).ToArray();
        output.WriteLine($"asked for SYSTEMID,SERIALNO — answered: {string.Join(", ", names)}");

        // The request was accepted at all: that is the case-insensitivity finding.
        Assert.Contains(names, n => n.Equals("SystemId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.Equals("serialNo", StringComparison.OrdinalIgnoreCase));

        // And the answer came back in the page's casing, not ours.
        Assert.DoesNotContain("SYSTEMID", names);
        Assert.DoesNotContain("SERIALNO", names);
    }

    /// <summary>
    /// The derived projection is what makes a wide page affordable: the same rows, without it,
    /// carry every column the page publishes.
    /// </summary>
    /// <remarks>
    /// Recorded as a measurement rather than a threshold. <c>LDATSalesLine</c> publishes 373
    /// columns; <see cref="SalesLine"/> declares two. Asserting a specific ratio would break when
    /// the tenant's schema changes, so this asserts only the direction and prints the size.
    /// </remarks>
    [LiveTenantFact]
    public async Task Derived_select_transfers_a_fraction_of_the_unprojected_row()
    {
        var client = LiveTenant.CreateClient();

        var projected = await client.QueryRawAsync<JsonElement>(
            "LDATSalesLine?$select=systemId,ccoProdOrderNo&$top=25");
        var full = await client.QueryRawAsync<JsonElement>("LDATSalesLine?$top=25");

        var projectedSize = projected.GetRawText().Length;
        var fullSize = full.GetRawText().Length;

        output.WriteLine($"25 rows: projected={projectedSize:N0} chars, unprojected={fullSize:N0} chars " +
                         $"(~{fullSize / (double)projectedSize:F0}x)");

        Assert.True(projectedSize < fullSize);
    }
}
