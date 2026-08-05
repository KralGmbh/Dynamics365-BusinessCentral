using Dynamics365.BusinessCentral.Errors;
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
/// <para>
/// Continuation is trusted only as far as it advances: a cursor that has already been
/// followed throws <see cref="BusinessCentralProtocolException"/> rather than replaying the
/// rows it produced the first time.
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

        // Every cursor already followed. A single previous-cursor check would catch only a
        // self-loop; a cycle (A → B → A) needs the whole history. Bounded by page count, which
        // at BC's page sizes is small even for a full-table stream.
        var visited = new HashSet<string>(StringComparer.Ordinal);

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
            var nextLink = page.NextLink;
            if (string.IsNullOrWhiteSpace(nextLink))
                yield break;

            // Every repeated cursor is a protocol fault, whether or not this page carried
            // rows. A nextLink asserts that continuation remains; an empty page does not let
            // the client conclude the result is complete. Stopping quietly would report a
            // potentially truncated result as success, while following it can loop forever.
            // Both self-loops and longer cycles land here.
            if (!visited.Add(nextLink))
            {
                throw new BusinessCentralProtocolException(
                    "Business Central returned an @odata.nextLink that has already been followed, " +
                    "so paging is not advancing. Following it again would repeat rows already " +
                    "returned. Retry the query; if it persists, the correlation ID from the page " +
                    "request identifies it to Microsoft support.",
                    nextLink);
            }

            page = await fetchNextPage(nextLink, cancellationToken).ConfigureAwait(false);
        }
    }
}
