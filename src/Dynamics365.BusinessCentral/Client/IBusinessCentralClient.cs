using Dynamics365.BusinessCentral.OData;

namespace Dynamics365.BusinessCentral.Client;

/// <summary>
/// Client abstraction for querying and modifying data in Microsoft Dynamics 365 Business Central via OData.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Query{TEntity}()"/> is the recommended entry point — it is strongly typed and
/// covers filtering, ordering, projection, expansion, paging and counting. The
/// <c>QueryAsync</c>/<c>PostAsync</c> family remains for direct, path-based access.
/// </para>
/// <para>
/// Every member has a default implementation, so a hand-written test fake only implements
/// the members it actually exercises — additions to this interface never break it. An
/// unimplemented member throws <see cref="NotSupportedException"/> naming itself.
/// <see cref="FirstOrDefaultAsync{TEntity}"/> composes over
/// <see cref="QueryAsync{TEntity}(string, ODataFilter?, Action{QueryOptions}?, IEnumerable{string}?, CancellationToken)"/>,
/// so a fake implementing that overload gets it for free.
/// </para>
/// </remarks>
public interface IBusinessCentralClient
{
    private static NotSupportedException NotImplemented(string member) =>
        new($"{nameof(IBusinessCentralClient)}.{member} is not implemented by this type. " +
            "Interface members have default implementations so partial test fakes keep " +
            "compiling; implement the member to use it.");

    /// <summary>Company this client is scoped to.</summary>
    string Company => throw NotImplemented(nameof(Company));

    /// <summary>
    /// Returns a client scoped to a different company, sharing the same HTTP client and
    /// access-token cache. Returns <see langword="this"/> when the company is unchanged.
    /// </summary>
    /// <param name="company">Company name, as returned by <see cref="GetCompaniesAsync"/>.</param>
    IBusinessCentralClient ForCompany(string company) =>
        throw NotImplemented(nameof(ForCompany));

    /// <summary>
    /// Starts a strongly-typed query. The entity set path comes from
    /// <see cref="BusinessCentralEntityAttribute"/> on <typeparamref name="TEntity"/>.
    /// </summary>
    /// <typeparam name="TEntity">Entity type to query.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TEntity"/> is not annotated; use <see cref="Query{TEntity}(string)"/>.
    /// </exception>
    IBusinessCentralQuery<TEntity> Query<TEntity>() =>
        throw NotImplemented(nameof(Query));

    /// <summary>Starts a strongly-typed query against an explicit entity set path.</summary>
    /// <typeparam name="TEntity">Entity type to query.</typeparam>
    /// <param name="path">Relative OData entity path, e.g. <c>salesOrders</c>.</param>
    IBusinessCentralQuery<TEntity> Query<TEntity>(string path) =>
        throw NotImplemented(nameof(Query));

    /// <summary>Lists the companies available in the tenant.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<BusinessCentralCompany>> GetCompaniesAsync(CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(GetCompaniesAsync));

    /// <summary>
    /// Fetches the tenant's raw <c>$metadata</c> document (EDMX XML) from the service root.
    /// </summary>
    /// <remarks>
    /// Returned as a string rather than a parsed model on purpose: the package does not own
    /// an EDMX object model, and inventing one would be a permanent compatibility liability.
    /// The document is large — a real tenant measured ~8 MB across 542 entity sets — so treat
    /// this as a build- or startup-time call, not a per-request one. The
    /// <c>Dynamics365.BusinessCentral.Testing</c> package consumes it to check that every
    /// derived <c>$select</c> resolves against the tenant's actual columns.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> GetMetadataAsync(CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(GetMetadataAsync));

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
        CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(QueryAsync));

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
        CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(QueryAsync));

    /// <summary>
    /// Fetches a single entity by key, returning <see langword="default"/> when it does not
    /// exist. "Does this entity exist" is a question, not an error — a <c>404</c> yields
    /// <see langword="default"/>; every other failure still throws.
    /// </summary>
    /// <remarks>
    /// The <c>404</c> is still reported to the diagnostics observer as a failed request,
    /// because on the wire it is one.
    /// </remarks>
    /// <typeparam name="TEntity">The entity type to deserialize the response into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="key">Entity key: a systemId, or an alternate key such as <c>No='1000'</c>.</param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TEntity?> GetAsync<TEntity>(
        string path,
        string key,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(GetAsync));

    /// <summary>
    /// Returns the first entity matching <paramref name="filter"/>, or
    /// <see langword="default"/> when nothing matches. Sends <c>$top=1</c>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the response into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="filter">Optional strongly-typed OData filter expression.</param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    async Task<TEntity?> FirstOrDefaultAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var page = await QueryAsync<TEntity>(path, filter, o => o.WithTop(1), select, cancellationToken)
            .ConfigureAwait(false);

        return page.Count == 0 ? default : page[0];
    }

    /// <summary>
    /// Executes an OData query and retrieves all matching entities by automatically paging
    /// through the result set. Prefer <see cref="QueryStreamAsync{TEntity}"/> for large sets.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the OData result into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="filter">Optional strongly-typed OData filter expression.</param>
    /// <param name="options">
    /// Optional query options. Paging is server-driven via <c>@odata.nextLink</c>; use
    /// <c>WithPageSize</c> to request smaller pages (<c>Prefer: odata.maxpagesize</c>).
    /// </param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<TEntity>> QueryAllAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(QueryAllAsync));

    /// <summary>
    /// Streams every matching entity, fetching pages lazily so the whole result set is
    /// never held in memory. Stopping the enumeration stops fetching.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to deserialize the OData result into.</typeparam>
    /// <param name="path">Relative OData entity path.</param>
    /// <param name="filter">Optional strongly-typed OData filter expression.</param>
    /// <param name="options">
    /// Optional query options. Paging is server-driven via <c>@odata.nextLink</c>; use
    /// <c>WithPageSize</c> to request smaller pages (<c>Prefer: odata.maxpagesize</c>).
    /// </param>
    /// <param name="select">Optional list of fields to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<TEntity> QueryStreamAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(QueryStreamAsync));

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
        CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(QueryRawAsync));

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
        where T : class =>
        throw NotImplemented(nameof(PatchAsync));

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
        where T : class =>
        throw NotImplemented(nameof(PostAsync));

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
        where TPayload : class =>
        throw NotImplemented(nameof(PostAsync));

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
        where TPayload : class =>
        throw NotImplemented(nameof(PatchAsync));

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
        where TPayload : class =>
        throw NotImplemented(nameof(PutAsync));

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
        where T : class =>
        throw NotImplemented(nameof(PutAsync));

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
        CancellationToken cancellationToken = default) =>
        throw NotImplemented(nameof(DeleteAsync));
}
