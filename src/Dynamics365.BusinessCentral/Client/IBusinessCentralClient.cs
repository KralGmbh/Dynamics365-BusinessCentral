using Dynamics365.BusinessCentral.OData;

namespace Dynamics365.BusinessCentral.Client;

/// <summary>
/// Client abstraction for querying and modifying data in Microsoft Dynamics 365 Business Central via OData.
/// </summary>
/// <remarks>
/// <see cref="Query{TEntity}()"/> is the recommended entry point — it is strongly typed and
/// covers filtering, ordering, projection, expansion, paging and counting. The
/// <c>QueryAsync</c>/<c>PostAsync</c> family remains for direct, path-based access.
/// </remarks>
public interface IBusinessCentralClient
{
    /// <summary>Company this client is scoped to.</summary>
    string Company { get; }

    /// <summary>
    /// Returns a client scoped to a different company, sharing the same HTTP client and
    /// access-token cache. Returns <see langword="this"/> when the company is unchanged.
    /// </summary>
    /// <param name="company">Company name, as returned by <see cref="GetCompaniesAsync"/>.</param>
    IBusinessCentralClient ForCompany(string company);

    /// <summary>
    /// Starts a strongly-typed query. The entity set path comes from
    /// <see cref="BusinessCentralEntityAttribute"/> on <typeparamref name="TEntity"/>.
    /// </summary>
    /// <typeparam name="TEntity">Entity type to query.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TEntity"/> is not annotated; use <see cref="Query{TEntity}(string)"/>.
    /// </exception>
    IBusinessCentralQuery<TEntity> Query<TEntity>();

    /// <summary>Starts a strongly-typed query against an explicit entity set path.</summary>
    /// <typeparam name="TEntity">Entity type to query.</typeparam>
    /// <param name="path">Relative OData entity path, e.g. <c>salesOrders</c>.</param>
    IBusinessCentralQuery<TEntity> Query<TEntity>(string path);

    /// <summary>Lists the companies available in the tenant.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<BusinessCentralCompany>> GetCompaniesAsync(CancellationToken cancellationToken = default);

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
    /// Executes an OData query and retrieves all matching entities by automatically paging
    /// through the result set. Prefer <see cref="QueryStreamAsync{TEntity}"/> for large sets.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the OData result into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="filter">Optional strongly-typed OData filter expression.</param>
    /// <param name="options">Optional query options; use <c>WithPageSize</c> to size each round trip.</param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<TEntity>> QueryAllAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams every matching entity, fetching pages lazily so the whole result set is
    /// never held in memory. Stopping the enumeration stops fetching.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the OData result into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="filter">Optional strongly-typed OData filter expression.</param>
    /// <param name="options">Optional query options; use <c>WithPageSize</c> to size each round trip.</param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<TEntity> QueryStreamAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a raw GET request against the given relative OData URL and deserializes the full response body.
    /// The path may include its own query string, which is sent verbatim.
    /// </summary>
    /// <typeparam name="TResponse">
    /// The type to deserialize the response body into. Unconstrained, so value types such as
    /// <see cref="System.Text.Json.JsonElement"/> work for responses you have no model for.
    /// </typeparam>
    /// <param name="path">Relative OData URL including any query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TResponse> QueryRawAsync<TResponse>(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a PATCH request to partially update an existing Business Central entity.
    /// </summary>
    /// <typeparam name="T">The entity type to serialize and deserialize.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">Entity key: a systemId, or an alternate key such as <c>No='1000'</c>.</param>
    /// <param name="payload">Object to serialize and send as the PATCH body.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The updated entity returned by Business Central, or <paramref name="payload"/> when
    /// the server answered 204 NoContent.
    /// </returns>
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
    /// <returns>
    /// The created entity returned by Business Central, or <paramref name="payload"/> when
    /// the server answered 204 NoContent.
    /// </returns>
    Task<T> PostAsync<T>(
        string path,
        T payload,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Creates an entity, deserializing the response into a type different from the payload.
    /// </summary>
    /// <remarks>
    /// Use this when you post an anonymous object or DTO but want a typed response back —
    /// the single-generic <see cref="PostAsync{T}"/> forces both to be the same type.
    /// </remarks>
    /// <typeparam name="TPayload">Type sent in the request body.</typeparam>
    /// <typeparam name="TResult">Type to deserialize the response into.</typeparam>
    /// <param name="path">Relative OData entity path where the entity should be created.</param>
    /// <param name="payload">Object to serialize and send as the POST body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The created entity, or <see langword="default"/> when Business Central applied the
    /// write but returned no representation (<c>204 No Content</c> or an empty body). That
    /// means "created, but not echoed back" — not "failed"; failures throw.
    /// <para>
    /// For a reference <typeparamref name="TResult"/> this is <see langword="null"/>. For a
    /// value type it is <c>default</c> — notably <c>default(JsonElement)</c>, whose
    /// <c>ValueKind</c> is <c>Undefined</c>; check that rather than comparing to null.
    /// </para>
    /// </returns>
    Task<TResult?> PostAsync<TPayload, TResult>(
        string path,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TPayload : class;

    /// <summary>
    /// Partially updates an entity, deserializing the response into a type different from
    /// the payload.
    /// </summary>
    /// <typeparam name="TPayload">Type sent in the request body.</typeparam>
    /// <typeparam name="TResult">Type to deserialize the response into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">Entity key: a systemId, or an alternate key such as <c>No='1000'</c>.</param>
    /// <param name="payload">Object to serialize and send as the PATCH body.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The updated entity, or <see langword="default"/> when the server returned no
    /// representation. Failures throw.
    /// </returns>
    Task<TResult?> PatchAsync<TPayload, TResult>(
        string path,
        string systemId,
        TPayload payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where TPayload : class;

    /// <summary>
    /// Replaces an entity, deserializing the response into a type different from the payload.
    /// </summary>
    /// <typeparam name="TPayload">Type sent in the request body.</typeparam>
    /// <typeparam name="TResult">Type to deserialize the response into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">Entity key: a systemId, or an alternate key such as <c>No='1000'</c>.</param>
    /// <param name="payload">Object to serialize and send as the PUT body.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The updated entity, or <see langword="default"/> when the server returned no
    /// representation. Failures throw.
    /// </returns>
    Task<TResult?> PutAsync<TPayload, TResult>(
        string path,
        string systemId,
        TPayload payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where TPayload : class;

    /// <summary>
    /// Executes a PUT request to fully replace an existing Business Central entity.
    /// </summary>
    /// <typeparam name="T">The entity type to serialize and deserialize.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">Entity key: a systemId, or an alternate key such as <c>No='1000'</c>.</param>
    /// <param name="payload">Object to serialize and send as the PUT body.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The updated entity returned by Business Central, or <paramref name="payload"/> when
    /// the server answered 204 NoContent.
    /// </returns>
    Task<T> PutAsync<T>(
        string path,
        string systemId,
        T payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Executes a DELETE request to remove an existing Business Central entity.
    /// </summary>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="systemId">Entity key: a systemId, or an alternate key such as <c>No='1000'</c>.</param>
    /// <param name="ifMatch">ETag value for optimistic concurrency control (default "*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(
        string path,
        string systemId,
        string ifMatch = "*",
        CancellationToken cancellationToken = default);
}
