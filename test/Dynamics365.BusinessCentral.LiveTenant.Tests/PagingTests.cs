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
/// <b>And both facts check that paging happened at all.</b> A read that came back complete in a
/// single response satisfies every row-level assertion here while exercising no continuation
/// whatsoever — so each fact first establishes that the set could not have arrived in one page:
/// by counting the requests the read issued, or by holding a first page that carried a
/// continuation. Without that, the day Business Central stops honouring
/// <c>Prefer: odata.maxpagesize</c> is the day these facts go quietly vacuous.
/// </para>
/// <para>
/// <b>The count is bracketed, not sampled</b> — see <see cref="LiveTenantAssert"/>.
/// </para>
/// </remarks>
public sealed class PagingTests(ITestOutputHelper output)
{
    /// <summary>Page preference for the first fact. Small enough to force several continuations.</summary>
    private const int RequestedPageSize = 500;

    /// <summary>
    /// The everyday guard: a page-size preference the client sends forces several continuations
    /// over a small set, and every row arrives exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately cheap and deterministic — ~1,900 rows in pages of 500. This is the fact that
    /// should stay fast enough that nobody is tempted to skip the suite.
    /// </para>
    /// <para>
    /// The continuations are counted rather than inferred. <c>ToPageAsync</c> cannot be used to
    /// inspect the first page here, because single-page reads deliberately send no
    /// <c>odata.maxpagesize</c> preference — on a one-shot request it would silently truncate the
    /// result. So the proof comes from the wire instead: <c>n</c> rows cannot arrive in fewer than
    /// <c>ceil(n / 500)</c> requests unless a page carried more than the 500 asked for, which is
    /// exactly the regression this guards.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task Requested_page_size_forces_continuations_and_returns_every_row_once()
    {
        var wire = new RecordingObserver();
        var client = LiveTenant.CreateClient(observer: wire);

        var before = await client.Query<LdatSummaryRow>().CountAsync();
        Assert.True(
            before > RequestedPageSize,
            $"This fact needs a set larger than one requested page; got {before}.");

        wire.Clear();
        var rows = await client.Query<LdatSummaryRow>().PageSize(RequestedPageSize).ToAllAsync();
        var requests = wire.Requests.Count;

        var after = await client.Query<LdatSummaryRow>().CountAsync();

        output.WriteLine($"LdatSummary: $count={before}..{after}, fetched={rows.Count}, " +
                         $"requests={requests} at odata.maxpagesize={RequestedPageSize}");

        var minimumRequests = (int)Math.Ceiling(rows.Count / (double)RequestedPageSize);

        Assert.True(
            requests >= minimumRequests,
            $"{rows.Count} rows arrived in {requests} request(s), so at least one page carried " +
            $"more than the {RequestedPageSize} rows preferred — {minimumRequests} were needed. " +
            $"Either Business Central stopped honouring Prefer: odata.maxpagesize or the client " +
            $"stopped sending it; in both cases this fact was about to pass without following a " +
            $"single continuation.");

        LiveTenantAssert.WithinBracket(before, after, rows.Count, "Paged read of LdatSummary");
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

        LiveTenantAssert.WithinBracket(before, after, rows.Count, "Paged read of LDATSalesLine");
        Assert.Equal(rows.Count, rows.Select(r => r.SystemId).Distinct().Count());
    }
}
