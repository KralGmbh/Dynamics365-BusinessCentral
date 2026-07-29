using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Dynamics365.BusinessCentral.Client;

/// <summary>
/// Default <see cref="IBusinessCentralClient"/> implementation.
/// </summary>
public sealed class BusinessCentralClient : IBusinessCentralClient, IBusinessCentralQueryExecutor
{
    private readonly HttpClient _http;
    private readonly BusinessCentralOptions _options;
    private readonly BusinessCentralUrlBuilder _urlBuilder;
    private readonly BusinessCentralTokenProvider _tokenProvider;
    private readonly IBusinessCentralObserver _observer;
    private readonly string _company;

    private const string BearerScheme = "Bearer";
    private const string IfMatchHeader = "If-Match";

    /// <summary>Default page size used when auto-paging.</summary>
    private const int DefaultPageSize = 1000;

    private static readonly JsonSerializerOptions _jsonOptions = BusinessCentralJson.Options;

    /// <summary>Creates a client for the company configured in <paramref name="options"/>.</summary>
    /// <param name="http">HTTP client used for data requests.</param>
    /// <param name="options">Connection settings.</param>
    /// <param name="observer">Optional diagnostics observer.</param>
    public BusinessCentralClient(
        HttpClient http,
        IOptions<BusinessCentralOptions> options,
        IBusinessCentralObserver? observer = null)
        : this(http, options, null, observer)
    {
    }

    internal BusinessCentralClient(
        HttpClient http,
        IOptions<BusinessCentralOptions> options,
        BusinessCentralTokenProvider? tokenProvider,
        IBusinessCentralObserver? observer)
    {
        _http = http;
        _options = options.Value;
        _observer = observer ?? new NullBusinessCentralObserver();
        _company = _options.Company;

        // No mutation of the supplied HttpClient: it may be pooled or shared, and setting
        // Timeout/DefaultRequestHeaders after first use throws. Per-request headers are
        // applied in HttpRequestExtensions.AddJsonHeaders instead.
        _tokenProvider = tokenProvider ?? new BusinessCentralTokenProvider(http, options, _observer);

        _urlBuilder = new BusinessCentralUrlBuilder(_options.ResolvedBaseUrl, _company);
    }

    /// <summary>Copy constructor used by <see cref="ForCompany"/>.</summary>
    private BusinessCentralClient(BusinessCentralClient source, string company)
    {
        _http = source._http;
        _options = source._options;
        _observer = source._observer;
        _tokenProvider = source._tokenProvider;
        _company = company;

        _urlBuilder = new BusinessCentralUrlBuilder(source._options.ResolvedBaseUrl, company);
    }

    /// <inheritdoc />
    public string Company => _company;

    /// <inheritdoc />
    public IBusinessCentralClient ForCompany(string company)
    {
        if (string.IsNullOrWhiteSpace(company))
            throw new ArgumentException("Company must not be empty.", nameof(company));

        return string.Equals(company, _company, StringComparison.Ordinal)
            ? this
            : new BusinessCentralClient(this, company);
    }

    /// <inheritdoc />
    public IBusinessCentralQuery<TEntity> Query<TEntity>() =>
        new BusinessCentralQuery<TEntity>(this, EntityPath.For<TEntity>());

    /// <inheritdoc />
    public IBusinessCentralQuery<TEntity> Query<TEntity>(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        return new BusinessCentralQuery<TEntity>(this, path);
    }

    /// <inheritdoc />
    public async Task<List<BusinessCentralCompany>> GetCompaniesAsync(
        CancellationToken cancellationToken = default)
    {
        // The company list lives at the service root, not under Company('...').
        var url = _urlBuilder.BuildServiceRootUrl("Company");

        using var res = await SendWithAuthRetryAsync(
            () => CreateJsonRequest(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);

        var wrapper = await DeserializeAsync<ODataResponse<BusinessCentralCompany>>(
            res,
            "Failed to deserialize the Business Central company list.",
            cancellationToken).ConfigureAwait(false);

        return wrapper.Value;
    }

    /// <inheritdoc />
    public async Task<TEntity?> GetAsync<TEntity>(
        string path,
        string key,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var url = _urlBuilder.BuildEntityUrl(path, key, select);

        try
        {
            using var res = await SendWithAuthRetryAsync(
                () => CreateJsonRequest(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);

            return await DeserializeAsync<TEntity>(
                res,
                "Failed to deserialize Business Central response.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (BusinessCentralNotFoundException)
        {
            // "Does this entity exist" is a question, not an error.
            return default;
        }
    }

    /// <inheritdoc />
    public async Task<TEntity?> FirstOrDefaultAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var page = await QueryAsync<TEntity>(path, filter, o => o.WithTop(1), select, cancellationToken)
            .ConfigureAwait(false);

        return page.Count == 0 ? default : page[0];
    }

    /// <inheritdoc />
    public Task<List<TEntity>> QueryAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
        => QueryAsync<TEntity>(path, filter?.Value ?? string.Empty, options, select, cancellationToken);

    /// <inheritdoc />
    public async Task<List<TEntity>> QueryAsync<TEntity>(
        string path,
        string filter,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var queryOptions = new QueryOptions();
        options?.Invoke(queryOptions);

        var page = await FetchPageAsync<TEntity>(path, filter, queryOptions, select, cancellationToken)
            .ConfigureAwait(false);

        return page.Value;
    }

    /// <inheritdoc />
    public async Task<TResponse> QueryRawAsync<TResponse>(
        string path,
        CancellationToken cancellationToken = default)
    {
        // BuildRawUrl (not BuildEntityUrl) so a caller-supplied query string such as
        // "salesOrders?$top=5" survives instead of being percent-encoded into the path.
        var url = _urlBuilder.BuildRawUrl(path);

        using var res = await SendWithAuthRetryAsync(
            () => CreateJsonRequest(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);

        return await DeserializeAsync<TResponse>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<TEntity>> QueryAllAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var all = new List<TEntity>();

        await foreach (var entity in QueryStreamAsync<TEntity>(path, filter, options, select, cancellationToken)
                           .ConfigureAwait(false))
        {
            all.Add(entity);
        }

        return all;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TEntity> QueryStreamAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var baseOptions = new QueryOptions();
        options?.Invoke(baseOptions);

        // Top is a result cap, exactly as documented on WithTop; PageSize sizes the round
        // trips. This mirrors BusinessCentralQuery<T>.StreamAsync — the two implementations
        // must stay in agreement.
        var limit = baseOptions.Top;

        // $top=0 is a request for no rows at all.
        if (limit == 0)
            yield break;

        var pageSize = baseOptions.PageSize ?? DefaultPageSize;

        // Guards the loop below: with a non-positive page size the "short page" termination
        // check can never fire, so it would request the same empty page forever. The public
        // setters reject these values; this covers the internal ones.
        if (pageSize <= 0)
            yield break;

        var filterValue = filter?.Value ?? string.Empty;

        // Honour a caller-supplied $skip as the starting offset instead of silently
        // restarting from the first page.
        var skip = baseOptions.Skip ?? 0;
        var emitted = 0;

        var requested = NextTop(pageSize, limit, emitted);

        var page = await FetchPageAsync<TEntity>(
                path, filterValue, PageOptions(baseOptions, requested, skip), select, cancellationToken)
            .ConfigureAwait(false);

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

            // Server-driven paging wins: when Business Central sends @odata.nextLink it
            // decides the page size, so a short page is not the end of the collection.
            if (!string.IsNullOrWhiteSpace(page.NextLink))
            {
                serverDriven = true;

                page = await FetchNextPageAsync<TEntity>(page.NextLink, cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            if (serverDriven)
                yield break;

            // No nextLink and a short page means the collection is exhausted.
            if (inPage < requested)
                yield break;

            skip += inPage;
            requested = NextTop(pageSize, limit, emitted);

            page = await FetchPageAsync<TEntity>(
                    path, filterValue, PageOptions(baseOptions, requested, skip), select, cancellationToken)
                .ConfigureAwait(false);
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

    private static QueryOptions PageOptions(QueryOptions baseOptions, int top, int skip)
    {
        var options = new QueryOptions
        {
            Top = top,
            Skip = skip,
            IncludeCount = baseOptions.IncludeCount
        };

        if (baseOptions.OrderBy != null)
            options.OrderBy = baseOptions.OrderBy;

        if (baseOptions.Expand.Count > 0)
            options.WithExpand([.. baseOptions.Expand]);

        return options;
    }

    async Task<ODataResponse<TEntity>> IBusinessCentralQueryExecutor.FetchPageAsync<TEntity>(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        CancellationToken cancellationToken)
        => await FetchPageAsync<TEntity>(path, filter, options, select, cancellationToken).ConfigureAwait(false);

    async Task<ODataResponse<TEntity>> IBusinessCentralQueryExecutor.FetchNextPageAsync<TEntity>(
        string absoluteUrl,
        CancellationToken cancellationToken)
        => await FetchNextPageAsync<TEntity>(absoluteUrl, cancellationToken).ConfigureAwait(false);

    private async Task<ODataResponse<TEntity>> FetchPageAsync<TEntity>(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        CancellationToken cancellationToken)
    {
        var url = _urlBuilder.BuildQueryUrl(path, filter, options, select);

        using var res = await SendWithAuthRetryAsync(
            () => CreateJsonRequest(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);

        return await DeserializeAsync<ODataResponse<TEntity>>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ODataResponse<TEntity>> FetchNextPageAsync<TEntity>(
        string absoluteUrl,
        CancellationToken cancellationToken)
    {
        using var res = await SendWithAuthRetryAsync(
            () => CreateJsonRequest(HttpMethod.Get, absoluteUrl), cancellationToken).ConfigureAwait(false);

        return await DeserializeAsync<ODataResponse<TEntity>>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, object? payload = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.AddJsonHeaders();

        if (payload != null)
        {
            req.Content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        return req;
    }

    /// <inheritdoc />
    public async Task<T> PatchAsync<T>(
        string path,
        string systemId,
        T payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where T : class
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        using var res = await SendWithAuthRetryAsync(
            () =>
            {
                var req = CreateJsonRequest(HttpMethod.Patch, url, payload);
                req.Headers.TryAddWithoutValidation(IfMatchHeader, ifMatch);
                req.AddReturnRepresentationPreference();
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadEntityOrEchoAsync(
            res, payload, "Failed to deserialize PATCH response.", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T> PostAsync<T>(
        string path,
        T payload,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var url = _urlBuilder.BuildEntityUrl(path);

        using var res = await SendWithAuthRetryAsync(
            () =>
            {
                var req = CreateJsonRequest(HttpMethod.Post, url, payload);
                req.AddReturnRepresentationPreference();
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadEntityOrEchoAsync(
            res, payload, "Failed to deserialize POST response.", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PostAsync<TPayload, TResult>(
        string path,
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        var url = _urlBuilder.BuildEntityUrl(path);

        using var res = await SendWithAuthRetryAsync(
            () =>
            {
                var req = CreateJsonRequest(HttpMethod.Post, url, payload);
                req.AddReturnRepresentationPreference();
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadEntityOrDefaultAsync<TResult>(
            res, "Failed to deserialize POST response.", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PatchAsync<TPayload, TResult>(
        string path,
        string systemId,
        TPayload payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        using var res = await SendWithAuthRetryAsync(
            () =>
            {
                var req = CreateJsonRequest(HttpMethod.Patch, url, payload);
                req.Headers.TryAddWithoutValidation(IfMatchHeader, ifMatch);
                req.AddReturnRepresentationPreference();
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadEntityOrDefaultAsync<TResult>(
            res, "Failed to deserialize PATCH response.", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult?> PutAsync<TPayload, TResult>(
        string path,
        string systemId,
        TPayload payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        using var res = await SendWithAuthRetryAsync(
            () =>
            {
                var req = CreateJsonRequest(HttpMethod.Put, url, payload);
                req.Headers.TryAddWithoutValidation(IfMatchHeader, ifMatch);
                req.AddReturnRepresentationPreference();
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadEntityOrDefaultAsync<TResult>(
            res, "Failed to deserialize PUT response.", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T> PutAsync<T>(
        string path,
        string systemId,
        T payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where T : class
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        using var res = await SendWithAuthRetryAsync(
            () =>
            {
                var req = CreateJsonRequest(HttpMethod.Put, url, payload);
                req.Headers.TryAddWithoutValidation(IfMatchHeader, ifMatch);
                req.AddReturnRepresentationPreference();
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        return await ReadEntityOrEchoAsync(
            res, payload, "Failed to deserialize PUT response.", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string path,
        string systemId,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        using var res = await SendWithAuthRetryAsync(
            () =>
            {
                var req = CreateJsonRequest(HttpMethod.Delete, url);
                req.Headers.TryAddWithoutValidation(IfMatchHeader, ifMatch);
                return req;
            },
            cancellationToken).ConfigureAwait(false);

        if (res.StatusCode != HttpStatusCode.NoContent &&
            res.StatusCode != HttpStatusCode.OK)
        {
            throw new BusinessCentralServerException(
                $"DELETE expected 200 OK or 204 NoContent but got {(int)res.StatusCode}.",
                res.StatusCode,
                HttpMethod.Delete.Method,
                url,
                null,
                null,
                null);
        }
    }

    /// <summary>
    /// Returns the entity Business Central echoed back, or the payload that was sent when
    /// the server answered 204 NoContent / an empty body. A write that succeeded without a
    /// representation is still a success — the request asked for one via Prefer, but the
    /// server is free to decline.
    /// </summary>
    private async Task<T> ReadEntityOrEchoAsync<T>(
        HttpResponseMessage res,
        T payload,
        string errorMessage,
        CancellationToken cancellationToken)
        where T : class
    {
        if (res.StatusCode == HttpStatusCode.NoContent)
            return payload;

        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(json))
            return payload;

        return Deserialize<T>(json, res, errorMessage);
    }

    /// <summary>
    /// Response reader for the two-generic write overloads, where the payload cannot stand
    /// in for the result. A 204 NoContent or empty body yields <see langword="default"/>,
    /// which is how the caller learns the server applied the write without returning a
    /// representation.
    /// </summary>
    private async Task<TResult?> ReadEntityOrDefaultAsync<TResult>(
        HttpResponseMessage res,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        if (res.StatusCode == HttpStatusCode.NoContent)
            return default;

        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(json))
            return default;

        return Deserialize<TResult>(json, res, errorMessage);
    }

    /// <summary>
    /// Sends a request, refreshing the token once on <c>401</c> and replaying transient
    /// failures according to <see cref="BusinessCentralRetryOptions"/>.
    /// </summary>
    /// <param name="createRequest">
    /// Builds the request. Called once per attempt — <b>not</b> reused — because
    /// <see cref="HttpClient"/> disposes request content once a send completes, so replaying
    /// a previously sent instance throws <see cref="ObjectDisposedException"/> for anything
    /// with a body.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken)
    {
        var request = createRequest();

        var method = request.Method;
        var url = request.RequestUri!.ToString();

        _observer.OnRequestStarting(new BusinessCentralRequestInfo
        {
            Method = method.Method,
            Url = url
        });

        var retry = _options.Retry ?? new BusinessCentralRetryOptions();
        var maxAttempts = retry.Enabled ? Math.Max(1, retry.MaxAttempts) : 1;

        // Guards against reporting the same failure twice: once at the throw site and
        // again in the catch-all below.
        var failureReported = false;

        var authRetried = false;
        var transientAttempt = 0;

        try
        {
            while (true)
            {
                var token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);

                request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);

                var stopwatch = Stopwatch.StartNew();

                HttpResponseMessage res;

                try
                {
                    res = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsNetworkFailure(ex, cancellationToken))
                {
                    stopwatch.Stop();

                    // No response was received, so there is no status code to map; wrap so
                    // the caller still only has BusinessCentralException to catch. Whether
                    // the request reached the server is as ambiguous as a 502/504, so the
                    // same replay rules apply (IsSafeToReplay holds a POST back).
                    var failure = new BusinessCentralConnectionException(
                        NetworkFailureMessage(ex), method.Method, url, ex);

                    _observer.OnRequestFailed(new BusinessCentralErrorInfo
                    {
                        Method = method.Method,
                        Url = url,
                        Duration = stopwatch.Elapsed,
                        Exception = failure
                    });

                    if (transientAttempt + 1 < maxAttempts &&
                        IsSafeToReplay(failure, method, retry))
                    {
                        transientAttempt++;

                        var delay = ComputeDelay(retry, null, transientAttempt);

                        _observer.OnRequestRetrying(new BusinessCentralRetryInfo
                        {
                            Method = method.Method,
                            Url = url,
                            Attempt = transientAttempt,
                            Delay = delay,
                            FromRetryAfter = false
                        });

                        request.Dispose();

                        if (delay > TimeSpan.Zero)
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                        request = createRequest();
                        continue;
                    }

                    failureReported = true;

                    request.Dispose();

                    throw failure;
                }

                res.RequestMessage ??= request;

                stopwatch.Stop();

                if (res.StatusCode == HttpStatusCode.Unauthorized && !authRetried)
                {
                    authRetried = true;

                    _observer.OnRequestFailed(new BusinessCentralErrorInfo
                    {
                        Method = method.Method,
                        Url = url,
                        Duration = stopwatch.Elapsed,
                        StatusCode = (int)res.StatusCode,
                        ResponseBody = await ReadBodySafeAsync(res, cancellationToken).ConfigureAwait(false),
                        Exception = new UnauthorizedAccessException("Unauthorized – retrying with refreshed token")
                    });

                    await _tokenProvider.InvalidateAsync(cancellationToken).ConfigureAwait(false);

                    request = Replace(res, request, createRequest);
                    continue;
                }

                if (!res.IsSuccessStatusCode)
                {
                    var failure = await BusinessCentralExceptionFactory
                        .CreateAsync(res, cancellationToken).ConfigureAwait(false);

                    _observer.OnRequestFailed(new BusinessCentralErrorInfo
                    {
                        Method = method.Method,
                        Url = url,
                        Duration = stopwatch.Elapsed,
                        StatusCode = (int)res.StatusCode,
                        ResponseBody = failure.ResponseBody,
                        Exception = failure
                    });

                    if (transientAttempt + 1 < maxAttempts &&
                        IsSafeToReplay(failure, method, retry))
                    {
                        transientAttempt++;

                        var fromRetryAfter = retry.HonorRetryAfter && failure.RetryAfter != null;
                        var delay = ComputeDelay(retry, failure.RetryAfter, transientAttempt);

                        _observer.OnRequestRetrying(new BusinessCentralRetryInfo
                        {
                            Method = method.Method,
                            Url = url,
                            StatusCode = (int)res.StatusCode,
                            Attempt = transientAttempt,
                            Delay = delay,
                            FromRetryAfter = fromRetryAfter
                        });

                        // Release the failed attempt before sleeping. Under throttling the
                        // backoff window is exactly when buffered responses would otherwise
                        // pile up across concurrent callers.
                        res.Dispose();
                        request.Dispose();

                        if (delay > TimeSpan.Zero)
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                        request = createRequest();
                        continue;
                    }

                    failureReported = true;

                    res.Dispose();
                    request.Dispose();

                    throw failure;
                }

                _observer.OnRequestSucceeded(new BusinessCentralRequestInfo
                {
                    Method = method.Method,
                    Url = url,
                    Duration = stopwatch.Elapsed,
                    StatusCode = (int)res.StatusCode
                });

                // Ownership of the response passes to the caller, which disposes it.
                return res;
            }
        }
        catch (Exception ex)
        {
            if (!failureReported)
            {
                _observer.OnRequestFailed(new BusinessCentralErrorInfo
                {
                    Method = method.Method,
                    Url = url,
                    Exception = ex
                });
            }

            throw;
        }
    }

    /// <summary>
    /// Releases an abandoned attempt and builds a fresh request for the next one, so retries
    /// neither leak responses nor re-send content that has already been disposed.
    /// </summary>
    private static HttpRequestMessage Replace(
        HttpResponseMessage response,
        HttpRequestMessage request,
        Func<HttpRequestMessage> createRequest)
    {
        response.Dispose();
        request.Dispose();

        return createRequest();
    }

    /// <summary>
    /// Whether the send failed without any response arriving: a connection-level error, or
    /// the <see cref="HttpClient"/> timeout. A cancellation requested through the caller's
    /// token is not a network failure and propagates as-is.
    /// </summary>
    private static bool IsNetworkFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static string NetworkFailureMessage(Exception ex) =>
        ex is TaskCanceledException
            ? "The request timed out before Business Central responded."
            : $"The connection to Business Central failed: {ex.Message}";

    /// <summary>
    /// Whether this failure may be retried by replaying the same request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>429</c> was rejected before processing, so replaying is always safe regardless
    /// of method. The other transient statuses are ambiguous — the write may already have
    /// been applied — so the method decides.
    /// </para>
    /// <para>
    /// <c>GET</c>, <c>PUT</c> and <c>DELETE</c> are idempotent by HTTP semantics and always
    /// replayed. <c>POST</c> is not, and creates a duplicate record if replayed after a write
    /// that actually landed, so it is held back unless
    /// <see cref="BusinessCentralRetryOptions.RetryPostOnTransientFailures"/> is set.
    /// </para>
    /// <para>
    /// <c>PATCH</c> is <b>replayed</b>, which is a deliberate deviation from RFC 9110 — that
    /// spec does not guarantee PATCH is idempotent. It is safe here because this client only
    /// ever sends a JSON merge of absolute field values, so applying it twice converges on
    /// the same state rather than compounding. A <c>PATCH</c> body containing relative
    /// operations would break that assumption; disable retries or pass a real
    /// <c>If-Match</c> ETag instead of <c>*</c> if you need one.
    /// </para>
    /// </remarks>
    private static bool IsSafeToReplay(
        BusinessCentralException failure,
        HttpMethod method,
        BusinessCentralRetryOptions retry)
    {
        if (!failure.IsTransient)
            return false;

        if (failure.StatusCode == HttpStatusCode.TooManyRequests)
            return true;

        if (method == HttpMethod.Post && !retry.RetryPostOnTransientFailures)
            return false;

        return true;
    }

    /// <summary>
    /// A server-supplied <c>Retry-After</c> wins over computed backoff; otherwise the delay
    /// doubles per attempt. Both are capped by <see cref="BusinessCentralRetryOptions.MaxDelay"/>.
    /// </summary>
    private static TimeSpan ComputeDelay(
        BusinessCentralRetryOptions retry,
        TimeSpan? retryAfter,
        int attempt)
    {
        var max = Floor(retry.MaxDelay);

        if (retry.HonorRetryAfter && retryAfter is { } requested)
            return Clamp(requested, max);

        var milliseconds = Floor(retry.BaseDelay).TotalMilliseconds * Math.Pow(2, attempt - 1);

        // A large BaseDelay or a high attempt count overflows to a value TimeSpan cannot
        // represent — or to Infinity — and TimeSpan.FromMilliseconds throws on both. Compare
        // in double space first so a transient failure never becomes a crash.
        if (double.IsNaN(milliseconds) || milliseconds >= max.TotalMilliseconds)
            return max;

        return Clamp(TimeSpan.FromMilliseconds(milliseconds), max);
    }

    private static TimeSpan Floor(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static TimeSpan Clamp(TimeSpan value, TimeSpan max)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;

        return value > max ? max : value;
    }

    private static async Task<string?> ReadBodySafeAsync(
        HttpResponseMessage res,
        CancellationToken cancellationToken)
    {
        try
        {
            return await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never mask the original failure.
            return null;
        }
    }

    private async Task<T> DeserializeAsync<T>(
        HttpResponseMessage res,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return Deserialize<T>(json, res, errorMessage);
    }

    private T Deserialize<T>(string json, HttpResponseMessage res, string errorMessage)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                   ?? throw new JsonException("Response was null.");
        }
        catch (JsonException ex)
        {
            _observer.OnDeserializationFailed(new BusinessCentralErrorInfo
            {
                Method = res.RequestMessage!.Method.Method,
                Url = res.RequestMessage!.RequestUri!.ToString(),
                StatusCode = (int)res.StatusCode,
                ResponseBody = json,
                Exception = ex
            });

            throw new BusinessCentralServerException(
                errorMessage,
                res.StatusCode,
                res.RequestMessage!.Method.Method,
                res.RequestMessage!.RequestUri!.ToString(),
                json,
                null,
                null,
                ex);
        }
    }
}
