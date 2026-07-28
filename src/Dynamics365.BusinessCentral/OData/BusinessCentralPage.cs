namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// A single page of results, together with the server-reported total when
/// <c>$count</c> was requested.
/// </summary>
/// <typeparam name="TEntity">Entity type of the page contents.</typeparam>
public sealed class BusinessCentralPage<TEntity>
{
    /// <summary>Entities in this page.</summary>
    public IReadOnlyList<TEntity> Items { get; }

    /// <summary>
    /// Total number of entities matching the filter, ignoring paging. Populated only when
    /// the query asked for a count.
    /// </summary>
    public long? TotalCount { get; }

    /// <summary>
    /// Absolute URL of the next page when the server is driving paging, otherwise
    /// <see langword="null"/>.
    /// </summary>
    public string? NextLink { get; }

    /// <summary>Whether the server indicated that more results are available.</summary>
    public bool HasMore => !string.IsNullOrWhiteSpace(NextLink);

    /// <summary>Creates a page.</summary>
    /// <param name="items">Entities in this page.</param>
    /// <param name="totalCount">Server-reported total, when requested.</param>
    /// <param name="nextLink">Absolute URL of the next page, when present.</param>
    public BusinessCentralPage(IReadOnlyList<TEntity> items, long? totalCount, string? nextLink)
    {
        Items = items;
        TotalCount = totalCount;
        NextLink = nextLink;
    }
}
