using System.Runtime.CompilerServices;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// The auto-paging state machine, used by both public entry points —
/// <c>BusinessCentralClient.QueryStreamAsync</c> (path-based) and
/// <c>BusinessCentralQuery&lt;T&gt;.StreamAsync</c> (fluent). One implementation so the two
/// cannot drift apart; only the page-fetching delegates differ per caller.
/// </summary>
/// <remarks>
/// Three-tier termination:
/// <list type="number">
/// <item>Follow <c>@odata.nextLink</c> whenever present — the server decides page size.</item>
/// <item>Once server-driven, a missing nextLink means the collection is exhausted; the
/// short-page rule no longer applies.</item>
/// <item>Otherwise stop on the first page shorter than requested.</item>
/// </list>
/// <c>limit</c> caps emitted rows (<c>$top</c> semantics), enforced mid-page and never
/// overshot by a request; <c>pageSize</c> sizes the round trips.
/// </remarks>
internal static class QueryPager
{
    /// <summary>Default rows-per-round-trip when the caller did not set a page size.</summary>
    public const int DefaultPageSize = 1000;

    public static async IAsyncEnumerable<TEntity> StreamAsync<TEntity>(
        int? limit,
        int pageSize,
        int initialSkip,
        Func<int, int, CancellationToken, Task<ODataResponse<TEntity>>> fetchPage,
        Func<string, CancellationToken, Task<ODataResponse<TEntity>>> fetchNextPage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // $top=0 is a request for no rows at all.
        if (limit == 0)
            yield break;

        // Guards the loop below: with a non-positive page size the "short page" termination
        // check can never fire, so it would request the same empty page forever. The public
        // setters reject these values; this covers the internal ones.
        if (pageSize <= 0)
            yield break;

        var skip = initialSkip;
        var emitted = 0;

        var requested = NextTop(pageSize, limit, emitted);
        var page = await fetchPage(requested, skip, cancellationToken).ConfigureAwait(false);

        // True once the server started driving paging via @odata.nextLink, at which point
        // it — not our $top — decides where the collection ends.
        var serverDriven = false;

        while (true)
        {
            var inPage = 0;

            foreach (var entity in page.Value)
            {
                yield return entity;

                emitted++;
                inPage++;

                if (limit is { } cap && emitted >= cap)
                    yield break;
            }

            if (!string.IsNullOrWhiteSpace(page.NextLink))
            {
                serverDriven = true;

                page = await fetchNextPage(page.NextLink!, cancellationToken).ConfigureAwait(false);

                continue;
            }

            // The server was paging and stopped offering a nextLink: nothing left.
            if (serverDriven)
                yield break;

            // No nextLink and a short page means the collection is exhausted.
            if (inPage < requested)
                yield break;

            skip += inPage;
            requested = NextTop(pageSize, limit, emitted);

            page = await fetchPage(requested, skip, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Page size for the next request, never overshooting a caller-set <c>$top</c>.</summary>
    private static int NextTop(int pageSize, int? limit, int emitted)
    {
        if (limit is not { } cap)
            return pageSize;

        var remaining = cap - emitted;
        return remaining < pageSize ? remaining : pageSize;
    }
}
