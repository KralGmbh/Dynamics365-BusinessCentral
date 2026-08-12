using Xunit.Abstractions;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// Server-driven paging against a real tenant — the one part of 2.0 that unit tests have the
/// least leverage over, because a scripted transport can only replay a <c>@odata.nextLink</c>
/// this repository wrote itself.
/// </summary>
/// <remarks>
/// <para>
/// 1.0 paged with client-generated <c>$top</c>/<c>$skip</c> at a page size of 1,000. Offset
/// paging can skip or duplicate rows when the underlying collection changes between requests, and
/// it ignores the server's own cursor entirely. 2.0 follows the server's continuation instead.
/// These facts are what make that claim checkable rather than asserted.
/// </para>
/// <para>
/// Completeness is checked against the endpoint's own <c>$count</c>, and identity is checked with
/// a distinct-key count. Both are needed: a run that dropped one page and duplicated another
/// could still return the right <i>number</i> of rows.
/// </para>
/// <para>
/// <b>The count is bracketed, not sampled.</b> <c>$count</c> and the paged read are separate
/// requests against a live tenant, so a row inserted or deleted between them would fail an exact
/// comparison while paging was working perfectly — a flake that looks exactly like the regression
/// this suite exists to catch, which is the worst kind. Each fact therefore counts before and
/// after, and requires the fetched total to fall within that bracket. When nothing changed the two
/// counts are equal and the check is exact; when something did, the tolerance is the measured size
/// of the change rather than a guess. A dropped or duplicated page still fails, because it moves
/// the total far outside a bracket that only ever spans concurrent tenant activity.
/// </para>
/// </remarks>
public sealed class PagingTests(ITestOutputHelper output)
{
    /// <summary>
    /// The everyday guard: a page-size preference the client sends forces several continuations
    /// over a small set, and every row arrives exactly once.
    /// </summary>
    /// <remarks>
    /// Deliberately cheap and deterministic — ~1,900 rows in pages of 500. This is the fact that
    /// should stay fast enough that nobody is tempted to skip the suite.
    /// </remarks>
    [LiveTenantFact]
    public async Task Requested_page_size_forces_continuations_and_returns_every_row_once()
    {
        var client = LiveTenant.CreateClient();

        var before = await client.Query<LdatSummaryRow>().CountAsync();
        Assert.True(before > 500, $"This fact needs a set larger than one requested page; got {before}.");

        var rows = await client.Query<LdatSummaryRow>().PageSize(500).ToAllAsync();

        var after = await client.Query<LdatSummaryRow>().CountAsync();

        output.WriteLine($"LdatSummary: $count={before}..{after}, fetched={rows.Count}, " +
                         $"pages≈{Math.Ceiling(before / 500d)}");

        AssertCompleteWithin(before, after, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.SystemId).Distinct().Count());
    }

    /// <summary>
    /// The fact that could not exist without a tenant: with <b>no</b> page preference at all, the
    /// server pages on its own authority and the client follows the cursor it issues.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape 1.0 never saw. Its <c>$top=1000</c> pacing meant the server never had to
    /// truncate, so no <c>@odata.nextLink</c> was ever emitted and the client's inability to
    /// follow one was invisible.
    /// </para>
    /// <para>
    /// The page size is <b>reported, not asserted</b>. It is the server's configured Max Page
    /// Size — a tenant setting, not a package constant — so pinning the number here would make an
    /// unrelated administrative change look like a package regression. What is asserted is the
    /// property the package owns: that a continuation was issued, and that following it returned
    /// the complete set.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task Server_pages_on_its_own_and_the_client_follows_the_continuation()
    {
        var client = LiveTenant.CreateClient();

        var before = await client.Query<SalesLine>().CountAsync();

        // One request, no page preference: whatever comes back is the server's own page.
        var firstPage = await client.Query<SalesLine>().ToPageAsync();

        var rows = await client.Query<SalesLine>().ToAllAsync();

        var after = await client.Query<SalesLine>().CountAsync();

        output.WriteLine($"LDATSalesLine: $count={before}..{after}, server page={firstPage.Items.Count}, " +
                         $"nextLink={(firstPage.HasMore ? "issued" : "none")}, fetched={rows.Count}");

        Assert.True(
            firstPage.HasMore,
            $"This fact needs a set larger than the server's Max Page Size. The whole set " +
            $"({before} rows) came back in one page, so no continuation was exercised. Pick a " +
            $"larger entity set rather than weakening this assertion.");

        Assert.True(firstPage.Items.Count < before);

        AssertCompleteWithin(before, after, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.SystemId).Distinct().Count());
    }

    /// <summary>
    /// Requires <paramref name="fetched"/> to lie within the bracket the two counts describe.
    /// </summary>
    private static void AssertCompleteWithin(long before, long after, int fetched)
    {
        var low = Math.Min(before, after);
        var high = Math.Max(before, after);

        Assert.True(
            fetched >= low && fetched <= high,
            $"Paged read returned {fetched} rows, outside the {low}..{high} the endpoint reported " +
            $"either side of it. A count that moved during the read explains a result inside that " +
            $"bracket; nothing outside it is explained by tenant activity.");
    }
}
