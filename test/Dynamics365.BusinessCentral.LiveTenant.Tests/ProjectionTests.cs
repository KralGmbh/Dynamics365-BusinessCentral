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
    /// <para>
    /// <b>Canonical means what <c>$metadata</c> publishes</b>, not a spelling written into this
    /// file. The second half of the claim is a relationship between the two documents the tenant
    /// serves — the schema and the page — so it is asserted as one, ordinally. Hard-coding the
    /// spellings would pin a value the tenant owns and, worse, would leave a drift to
    /// <c>systemid</c> passing: case-insensitive predicates accept anything, and rejecting only
    /// the all-caps spelling that was sent rejects almost nothing.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task Select_is_case_insensitive_and_answered_in_the_pages_own_casing()
    {
        var client = LiveTenant.CreateClient();

        var metadata = BusinessCentralMetadata.Parse(await client.GetMetadataAsync());

        Assert.True(
            metadata.TryGetColumns("LdatSummary", out var columns),
            "LdatSummary is not published on this tenant; this fact needs a page whose canonical " +
            "casing can be read from $metadata.");

        var systemId = Canonical(columns, "SystemId");
        var serialNo = Canonical(columns, "serialNo");

        // Deliberately the wrong casing for both. That the request differs from the canonical
        // spelling at all is what makes the probe meaningful, so it is checked rather than assumed.
        var askedSystemId = DifferentCasing(systemId);
        var askedSerialNo = DifferentCasing(serialNo);

        Assert.NotEqual(systemId, askedSystemId, StringComparer.Ordinal);
        Assert.NotEqual(serialNo, askedSerialNo, StringComparer.Ordinal);

        var response = await client.QueryRawAsync<JsonElement>(
            $"LdatSummary?$select={askedSystemId},{askedSerialNo}&$top=1");

        var rows = response.GetProperty("value");
        Assert.True(rows.GetArrayLength() > 0, "Expected at least one row.");

        var names = rows[0].EnumerateObject().Select(p => p.Name).ToArray();
        var propertyNames = names.Where(n => !n.StartsWith('@')).ToArray();
        output.WriteLine($"asked for {askedSystemId},{askedSerialNo} — $metadata says " +
                         $"{systemId},{serialNo} — answered: {string.Join(", ", names)}");

        // The request was accepted at all: that is the case-insensitivity finding.
        // It restricted the page to exactly those properties rather than silently ignoring the
        // unrecognized spellings and returning the full entity, and answered in the casing
        // $metadata publishes, character for character. OData annotations are not properties.
        Assert.Equal(2, propertyNames.Length);
        Assert.Contains(systemId, propertyNames, StringComparer.Ordinal);
        Assert.Contains(serialNo, propertyNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// The <c>$metadata</c> spelling of a column, matched case-insensitively.
    /// </summary>
    /// <remarks>
    /// <see cref="BusinessCentralMetadataModel.TryGetColumns"/> answers a case-insensitive set
    /// that holds the document's own strings, so enumerating it is how the canonical casing is
    /// recovered — the package itself never needs it, because its column matching is
    /// <c>OrdinalIgnoreCase</c> by design.
    /// </remarks>
    private static string Canonical(IReadOnlySet<string> columns, string column) =>
        columns.FirstOrDefault(c => c.Equals(column, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"$metadata does not publish a '{column}' column on LdatSummary. Pick a column that " +
            "exists rather than relaxing the comparison.");

    /// <summary>Chooses a casing guaranteed to differ from the canonical spelling.</summary>
    private static string DifferentCasing(string canonical)
    {
        var upper = canonical.ToUpperInvariant();
        return canonical.Equals(upper, StringComparison.Ordinal)
            ? canonical.ToLowerInvariant()
            : upper;
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
