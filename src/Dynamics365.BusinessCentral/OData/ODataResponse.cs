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
    Task<ODataResponse<TEntity>> FetchPageAsync<TEntity>(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        CancellationToken cancellationToken);

    Task<ODataResponse<TEntity>> FetchNextPageAsync<TEntity>(
        string absoluteUrl,
        CancellationToken cancellationToken);
}
