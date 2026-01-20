using Dynamics365.BusinessCentral.OData;

namespace Dynamics365.BusinessCentral.Client;

/// <summary>
/// Client abstraction for querying and modifying data in Microsoft Dynamics 365 Business Central via OData.
/// </summary>
public interface IBusinessCentralClient
{
    /// <summary>
    /// Executes an OData query against a Business Central entity and returns the matching entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the OData result into.</typeparam>
    /// <param name="path">Relative OData entity path (e.g. "SalesOrders").</param>
    /// <param name="filter">Optional strongly-typed OData filter expression.</param>
    /// <param name="options">Optional query options such as paging or ordering.</param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<TEntity>> QueryAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an OData query using a raw $filter string and returns the matching entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the OData result into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="filter">Raw OData $filter expression.</param>
    /// <param name="options">Optional query options such as paging or ordering.</param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<TEntity>> QueryAsync<TEntity>(
        string path,
        string filter,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an OData query and retrieves all matching entities by automatically paging through the result set.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the OData result into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="filter">Optional strongly-typed OData filter expression.</param>
    /// <param name="options">Optional query options such as page size or ordering.</param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<TEntity>> QueryAllAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a raw GET request against the given relative OData URL and deserializes the full response body.
    /// This method bypasses the standard query helpers and can be used for custom endpoints or complex responses.
    /// </summary>
    /// <typeparam name="TResponse">The type to deserialize the response body into.</typeparam>
    /// <param name="path">Relative OData URL including any query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TResponse> QueryRawAsync<TResponse>(
        string path,
        CancellationToken cancellationToken = default)
        where TResponse : class;

    /// <summary>
    /// Executes a PATCH request to partially update an existing Business Central entity.
    /// The request requires an If-Match header for optimistic concurrency control.
    /// </summary>
    /// <typeparam name="T">The entity type to serialize and deserialize.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">The Business Central systemId of the entity to update.</param>
    /// <param name="payload">Object to serialize and send as the PATCH body.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated entity returned by Business Central.</returns>
    Task<T> PatchAsync<T>(
        string path,
        string systemId,
        T payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Executes a POST request to create a new entity in Business Central.
    /// </summary>
    /// <typeparam name="T">The entity type to serialize and deserialize.</typeparam>
    /// <param name="path">Relative OData entity path where the entity should be created.</param>
    /// <param name="payload">Object to serialize and send as the POST body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created entity returned by Business Central.</returns>
    Task<T> PostAsync<T>(
        string path,
        T payload,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Executes a PUT request to fully replace an existing Business Central entity.
    /// The request requires an If-Match header for optimistic concurrency control.
    /// </summary>
    /// <typeparam name="T">The entity type to serialize and deserialize.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">The Business Central systemId of the entity to replace.</param>
    /// <param name="payload">Object to serialize and send as the PUT body.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated entity returned by Business Central.</returns>
    Task<T> PutAsync<T>(
        string path,
        string systemId,
        T payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Executes a DELETE request to remove an existing Business Central entity.
    /// The request requires an If-Match header for optimistic concurrency control.
    /// </summary>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">The Business Central systemId of the entity to delete.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(
        string path,
        string systemId,
        string ifMatch = "*",
        CancellationToken cancellationToken = default);
}
