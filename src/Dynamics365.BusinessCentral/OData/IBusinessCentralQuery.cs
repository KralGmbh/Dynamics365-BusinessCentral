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

    /// <summary>Orders ascending, replacing any ordering set so far.</summary>
    IBusinessCentralQuery<TEntity> OrderBy(Expression<Func<TEntity, object?>> field);

    /// <summary>Orders descending, replacing any ordering set so far.</summary>
    IBusinessCentralQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> field);

    /// <summary>Appends an ascending sort key.</summary>
    IBusinessCentralQuery<TEntity> ThenBy(Expression<Func<TEntity, object?>> field);

    /// <summary>Appends a descending sort key.</summary>
    IBusinessCentralQuery<TEntity> ThenByDescending(Expression<Func<TEntity, object?>> field);

    /// <summary>Restricts the returned fields (<c>$select</c>).</summary>
    IBusinessCentralQuery<TEntity> Select(params Expression<Func<TEntity, object?>>[] fields);

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

    /// <summary>Returns how many entities match, without fetching them.</summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);
}
