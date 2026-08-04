using System.Runtime.CompilerServices;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// The auto-paging state machine, used by both public entry points —
/// <c>BusinessCentralClient.QueryStreamAsync</c> (path-based) and
/// <c>BusinessCentralQuery&lt;T&gt;.StreamAsync</c> (fluent). One implementation so the two
/// cannot drift apart; only the page-fetching delegates differ per caller.
/// </summary>
/// <remarks>
/// <para>
/// Paging is <b>server-driven</b> (verified against a live BC SaaS tenant): the first
/// request carries no <c>$top</c> unless the caller set a result cap, the server pages at
/// its own Max Page Size — or at the <c>Prefer: odata.maxpagesize</c> preference the fetch
/// delegates send when one is configured — and continuation follows
/// <c>@odata.nextLink</c>, an opaque <c>$skiptoken</c> cursor immune to the offset-shift
/// hazard of <c>$skip</c> paging. No nextLink means the response is complete: either the
/// server served everything, or the caller's <c>$top</c> budget is satisfied.
/// </para>
/// <para>
/// <c>limit</c> (<c>$top</c> semantics) caps emitted rows, enforced mid-page client-side
/// and also sent to the server so it never over-serves a capped query.
/// </para>
/// </remarks>
internal static class QueryPager
{
    public static async IAsyncEnumerable<TEntity> StreamAsync<TEntity>(
        int? limit,
        int initialSkip,
        Func<int?, int, CancellationToken, Task<ODataResponse<TEntity>>> fetchFirstPage,
        Func<string, CancellationToken, Task<ODataResponse<TEntity>>> fetchNextPage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // $top=0 is a request for no rows at all.
        if (limit == 0)
            yield break;

        var emitted = 0;

        var page = await fetchFirstPage(limit, initialSkip, cancellationToken).ConfigureAwait(false);

        // The cursor that produced the page in hand, so a server that fails to advance it
        // cannot spin this loop forever.
        string? followed = null;

        while (true)
        {
            foreach (var entity in page.Value)
            {
                yield return entity;

                emitted++;

                if (limit is { } cap && emitted >= cap)
                    yield break;
            }

            // No continuation means the collection (or the caller's $top budget) is
            // exhausted — BC offers a nextLink on every page it truncates.
            if (string.IsNullOrWhiteSpace(page.NextLink))
                yield break;

            // No-progress guard. An empty page whose nextLink is the cursor we just followed
            // would return the same empty page forever; following it again cannot produce a
            // row that following it once did not. Terminating therefore loses nothing, which
            // is why this stops rather than throws. Both conditions are required: an empty
            // page with a *new* cursor is legitimate (a page whose every row was filtered
            // server-side), and so is a repeated cursor that still carried rows.
            if (page.Value.Count == 0 &&
                string.Equals(page.NextLink, followed, StringComparison.Ordinal))
            {
                yield break;
            }

            followed = page.NextLink;

            page = await fetchNextPage(followed!, cancellationToken).ConfigureAwait(false);
        }
    }
}
