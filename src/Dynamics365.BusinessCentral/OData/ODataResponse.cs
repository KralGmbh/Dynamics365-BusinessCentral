using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.OData;

/// <summary>
/// The OData collection envelope Business Central wraps results in.
/// </summary>
internal sealed class ODataResponse<TEntity>
{
    [JsonPropertyName("value")]
    public List<TEntity> Value { get; set; } = [];

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; set; }

    [JsonPropertyName("@odata.count")]
    public long? Count { get; set; }
}

/// <summary>
/// Page-fetching primitives the fluent query builder needs from the client.
/// </summary>
internal interface IBusinessCentralQueryExecutor
{
    /// <summary>
    /// The registration-level <c>BusinessCentralOptions.MaxPageSize</c>, so the fluent
    /// builder can fall back to it when no per-query page size was set.
    /// </summary>
    int? DefaultMaxPageSize { get; }

    /// <summary>
    /// The registration-level <c>BusinessCentralOptions.DeriveSelect</c>. When
    /// <see langword="false"/>, a query with no explicit projection sends no <c>$select</c>
    /// at all, as if every query had called <c>SelectAll()</c>.
    /// </summary>
    bool DeriveSelect => true;

    /// <summary>
    /// Whether membership filters left at <see cref="ODataInStyle.Auto"/> render as the native
    /// <c>in</c> operator, resolved from the registration's schema version and style.
    /// </summary>
    bool UseNativeIn => false;

    /// <remarks>
    /// <c>maxPageSize</c>, when set, is sent as <c>Prefer: odata.maxpagesize</c>. Streaming
    /// reads pass the resolved preference; single-page reads pass <see langword="null"/> —
    /// a preference on a one-shot request would silently truncate it to the first server
    /// page.
    /// </remarks>
    Task<ODataResponse<TEntity>> FetchPageAsync<TEntity>(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        int? maxPageSize,
        CancellationToken cancellationToken);

    /// <remarks>
    /// <c>maxPageSize</c> must be the same preference as the page that produced the
    /// nextLink — re-sent on every continuation, because the preference applies per
    /// request, not per cursor.
    /// </remarks>
    Task<ODataResponse<TEntity>> FetchNextPageAsync<TEntity>(
        string absoluteUrl,
        int? maxPageSize,
        CancellationToken cancellationToken);
}
