using System.Linq.Expressions;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// Fluent, strongly-typed query over a Business Central entity set.
/// </summary>
/// <remarks>
/// Field names come from property selectors, so they survive renames and always agree with
/// how the entity is deserialized. Builder methods mutate and return the same instance —
/// call <c>Query&lt;T&gt;()</c> again for an independent query.
/// <code>
/// var orders = await client.Query&lt;SalesOrder&gt;()
///     .Where(Filter.Equals&lt;SalesOrder&gt;(o =&gt; o.Status, "Open"))
///     .OrderByDescending(o =&gt; o.Amount)
///     .ThenBy(o =&gt; o.No)
///     .Select(o =&gt; o.No, o =&gt; o.Amount)
///     .Top(50)
///     .ToListAsync();
/// </code>
/// </remarks>
/// <typeparam name="TEntity">Entity type being queried.</typeparam>
public interface IBusinessCentralQuery<TEntity>
{
    /// <summary>Adds a filter, combined with any existing filter using <c>and</c>.</summary>
    IBusinessCentralQuery<TEntity> Where(ODataFilter filter);

    /// <summary>Adds a raw OData <c>$filter</c> expression, combined using <c>and</c>.</summary>
    IBusinessCentralQuery<TEntity> Where(string filter);

    /// <summary>
    /// Adds a filter built with the entity type already inferred — no type argument to
    /// restate per operator:
    /// <code>
    /// .Where(f =&gt; f.Equals(x =&gt; x.Status, "Open").And(f.GreaterThan(x =&gt; x.Amount, 100)))
    /// </code>
    /// Combined with any existing filter using <c>and</c>, exactly like the other overloads.
    /// </summary>
    IBusinessCentralQuery<TEntity> Where(Func<IFilterBuilder<TEntity>, ODataFilter> build);

    /// <summary>Orders ascending, replacing any ordering set so far.</summary>
    IBusinessCentralQuery<TEntity> OrderBy(Expression<Func<TEntity, object?>> field);

    /// <summary>Orders descending, replacing any ordering set so far.</summary>
    IBusinessCentralQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> field);

    /// <summary>Appends an ascending sort key.</summary>
    IBusinessCentralQuery<TEntity> ThenBy(Expression<Func<TEntity, object?>> field);

    /// <summary>Appends a descending sort key.</summary>
    IBusinessCentralQuery<TEntity> ThenByDescending(Expression<Func<TEntity, object?>> field);

    /// <summary>
    /// Restricts the returned fields (<c>$select</c>). When neither this nor
    /// <see cref="SelectAll"/> is called, <c>$select</c> is <b>derived from
    /// <typeparamref name="TEntity"/></b>: its settable scalar properties, resolved to
    /// wire names the same way filters and deserialization are. The entity class states
    /// the projection once — call sites stop restating it.
    /// </summary>
    IBusinessCentralQuery<TEntity> Select(params Expression<Func<TEntity, object?>>[] fields);

    /// <summary>
    /// Requests every column (<b>no</b> <c>$select</c>), suppressing the derived
    /// projection — for deliberately partial entity types where the full row is wanted,
    /// e.g. for diagnostics. Mutually exclusive with <see cref="Select"/>: whichever was
    /// called last wins.
    /// </summary>
    IBusinessCentralQuery<TEntity> SelectAll();

    /// <summary>Expands navigation properties (<c>$expand</c>).</summary>
    IBusinessCentralQuery<TEntity> Expand(params Expression<Func<TEntity, object?>>[] fields);

    /// <summary>Expands navigation properties using raw OData expand syntax.</summary>
    IBusinessCentralQuery<TEntity> Expand(params string[] fields);

    /// <summary>Limits the number of entities returned (<c>$top</c>).</summary>
    IBusinessCentralQuery<TEntity> Top(int count);

    /// <summary>Skips entities (<c>$skip</c>).</summary>
    IBusinessCentralQuery<TEntity> Skip(int count);

    /// <summary>
    /// Requests at most <paramref name="size"/> rows per page when auto-paging, via
    /// <c>Prefer: odata.maxpagesize</c>. Not a result limit. When unset, the server pages
    /// at its own configured Max Page Size (or <c>BusinessCentralOptions.MaxPageSize</c>,
    /// when that is set); the server clamps the value to its own maximum.
    /// </summary>
    IBusinessCentralQuery<TEntity> PageSize(int size);

    /// <summary>Executes a single request and returns that page's entities.</summary>
    Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default);

    /// <summary>Pages through the whole result set and returns everything.</summary>
    Task<List<TEntity>> ToAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the whole result set, fetching pages lazily. Prefer this over
    /// <see cref="ToAllAsync"/> for large sets, or when you may stop early.
    /// </summary>
    IAsyncEnumerable<TEntity> StreamAsync(CancellationToken cancellationToken = default);

    /// <summary>Executes a single request and returns the page plus the total count.</summary>
    Task<BusinessCentralPage<TEntity>> ToPageAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the first matching entity, or <see langword="null"/> when none match.</summary>
    Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns how many entities match, asking the server for the count rather than fetching
    /// the rows — <b>where the endpoint supports it</b>. See the remarks: it does not always.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The request is <c>$count=true&amp;$top=0</c> with no <c>$select</c>, so normally one
    /// round trip returns the number and no rows.
    /// </para>
    /// <para>
    /// <b>When the endpoint ignores <c>$count</c></b> — some published pages do — there is no
    /// count in the response, and this falls back to <b>streaming the entire result set and
    /// counting it</b>. That fallback is invisible, unbounded and potentially very expensive:
    /// against a six-figure entity set it is many round trips, each buffering a full server
    /// page. It is a correctness guarantee, not a performance one.
    /// </para>
    /// <para>
    /// If the cost matters more than the exact number, prefer a bounded probe — a
    /// <c>Top(n)</c> read whose length you inspect — or verify once that your endpoint honours
    /// <c>$count</c> before relying on this in a hot path.
    /// </para>
    /// </remarks>
    Task<long> CountAsync(CancellationToken cancellationToken = default);
}
