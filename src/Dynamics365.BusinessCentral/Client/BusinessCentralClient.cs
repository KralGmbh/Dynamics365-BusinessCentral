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

    private static readonly JsonSerializerOptions _jsonOptions = BusinessCentralJson.Options;

    /// <summary>Creates a client for the company configured in <paramref name="options"/>.</summary>
    /// <remarks>
    /// Prefer registration via <c>AddBusinessCentral</c>: with this constructor the client
    /// creates a <b>private</b> token cache, so every manually constructed instance
    /// re-authenticates independently — construct once and reuse, don't new one up per
    /// call. Token requests also share <paramref name="http"/> with data traffic here,
    /// instead of the separate named client the DI path uses.
    /// </remarks>
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
        _observer = SafeBusinessCentralObserver.Wrap(observer);
        _company = _options.Company;

        // No mutation of the supplied HttpClient: it may be pooled or shared, and setting
        // Timeout/DefaultRequestHeaders after first use throws. Per-request headers are
        // applied in HttpRequestExtensions.AddJsonHeaders instead.
        _tokenProvider = tokenProvider ?? new BusinessCentralTokenProvider(http, options, _observer);

        _urlBuilder = CreateUrlBuilder(_options, _company, _observer);
    }

    /// <summary>Copy constructor used by <see cref="ForCompany"/>.</summary>
    private BusinessCentralClient(BusinessCentralClient source, string company)
    {
        _http = source._http;
        _options = source._options;
        _observer = source._observer;
        _tokenProvider = source._tokenProvider;
        _company = company;

        _urlBuilder = CreateUrlBuilder(source._options, company, source._observer);
    }

    private static BusinessCentralUrlBuilder CreateUrlBuilder(
        BusinessCentralOptions options,
        string company,
        IBusinessCentralObserver observer) =>
        new(options.ResolvedBaseUrl,
            company,
            options.MaxQueryStringLength,
            options.QueryStringLengthWarningThreshold,
            observer,
            options.SchemaVersion);

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
    public async Task<string> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        // $metadata is EDMX XML at the service root, so it needs neither the company segment
        // nor the JSON Accept header — and its '$' must survive unencoded.
        var url = _urlBuilder.BuildMetadataUrl();

        using var res = await SendWithAuthRetryAsync(
            () => CreateMetadataRequest(url), cancellationToken).ConfigureAwait(false);

        return await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateMetadataRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.AddMetadataHeaders();
        ApplyRequestOptions(req);
        return req;
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

        // Single page: no odata.maxpagesize preference — it would silently truncate a
        // one-shot request to the first server page.
        var page = await FetchPageAsync<TEntity>(path, filter, queryOptions, select, null, cancellationToken)
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

        var filterValue = filter?.Value ?? string.Empty;

        // Top is a result cap, exactly as documented on WithTop. Paging is server-driven:
        // the per-query PageSize (else the registration-level MaxPageSize, else nothing)
        // is sent as Prefer: odata.maxpagesize, and the server pages via @odata.nextLink.
        // baseOptions.Skip is honoured as the starting offset of the first request.
        var maxPageSize = baseOptions.PageSize ?? _options.MaxPageSize;

        var stream = QueryPager.StreamAsync(
            baseOptions.Top,
            baseOptions.Skip ?? 0,
            (top, skip, ct) => FetchPageAsync<TEntity>(
                path, filterValue, PageOptions(baseOptions, top, skip), select, maxPageSize, ct),
            (link, ct) => FetchNextPageAsync<TEntity>(link, maxPageSize, ct),
            cancellationToken);

        await foreach (var entity in stream.ConfigureAwait(false))
            yield return entity;
    }

    /// <summary>
    /// Options for the first request of a stream: the caller's cap and starting offset,
    /// plus everything shareable from the base options. Continuations use the server's
    /// nextLink verbatim instead.
    /// </summary>
    private static QueryOptions PageOptions(QueryOptions baseOptions, int? top, int skip)
    {
        var options = new QueryOptions
        {
            Top = top,
            Skip = skip == 0 ? null : skip,
            IncludeCount = baseOptions.IncludeCount
        };

        if (baseOptions.OrderBy != null)
            options.OrderBy = baseOptions.OrderBy;

        if (baseOptions.Expand.Count > 0)
            options.WithExpand([.. baseOptions.Expand]);

        return options;
    }

    int? IBusinessCentralQueryExecutor.DefaultMaxPageSize => _options.MaxPageSize;

    bool IBusinessCentralQueryExecutor.DeriveSelect => _options.DeriveSelect;

    async Task<ODataResponse<TEntity>> IBusinessCentralQueryExecutor.FetchPageAsync<TEntity>(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        int? maxPageSize,
        CancellationToken cancellationToken)
        => await FetchPageAsync<TEntity>(path, filter, options, select, maxPageSize, cancellationToken).ConfigureAwait(false);

    async Task<ODataResponse<TEntity>> IBusinessCentralQueryExecutor.FetchNextPageAsync<TEntity>(
        string absoluteUrl,
        int? maxPageSize,
        CancellationToken cancellationToken)
        => await FetchNextPageAsync<TEntity>(absoluteUrl, maxPageSize, cancellationToken).ConfigureAwait(false);

    private async Task<ODataResponse<TEntity>> FetchPageAsync<TEntity>(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        int? maxPageSize,
        CancellationToken cancellationToken)
    {
        var url = _urlBuilder.BuildQueryUrl(path, filter, options, select);

        using var res = await SendWithAuthRetryAsync(
            () => CreatePageRequest(url, maxPageSize), cancellationToken).ConfigureAwait(false);

        return await DeserializeAsync<ODataResponse<TEntity>>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ODataResponse<TEntity>> FetchNextPageAsync<TEntity>(
        string absoluteUrl,
        int? maxPageSize,
        CancellationToken cancellationToken)
    {
        // The nextLink is sent verbatim — it arrives pre-encoded and carries an opaque
        // $skiptoken; rebuilding it would corrupt the cursor. The maxpagesize preference
        // is re-sent because it applies per request, not per cursor.
        using var res = await SendWithAuthRetryAsync(
            () => CreatePageRequest(absoluteUrl, maxPageSize), cancellationToken).ConfigureAwait(false);

        return await DeserializeAsync<ODataResponse<TEntity>>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreatePageRequest(string url, int? maxPageSize)
    {
        var req = CreateJsonRequest(HttpMethod.Get, url);

        // The public setters reject non-positive sizes; internal ones fall back to "no
        // preference" rather than sending the server a nonsense value.
        if (maxPageSize is { } size && size > 0)
            req.AddMaxPageSizePreference(size);

        return req;
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, object? payload = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.AddJsonHeaders();
        ApplyRequestOptions(req);

        if (payload != null)
        {
            req.Content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                System.Text.Encoding.UTF8,
                "application/json");
        }

        return req;
    }

    /// <summary>
    /// Applies the registration-level headers that are not specific to one call: the
    /// read-replica hint and the response language. Both are opt-in and absent by default.
    /// </summary>
    private void ApplyRequestOptions(HttpRequestMessage request)
    {
        request.AddDataAccessIntent(_options.DataAccessIntent);
        request.AddAcceptLanguage(_options.AcceptLanguage);
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
                catch (Exception ex) when (RetryHelper.IsNetworkFailure(ex, cancellationToken))
                {
                    stopwatch.Stop();

                    // No response was received, so there is no status code to map; wrap so
                    // the caller still only has BusinessCentralException to catch. Whether
                    // the request reached the server is as ambiguous as a 502/504, so the
                    // same replay rules apply (IsSafeToReplay holds a POST back).
                    var failure = new BusinessCentralConnectionException(
                        RetryHelper.NetworkFailureMessage(ex, "Business Central"), method.Method, url, ex);

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

                        var delay = RetryHelper.ComputeDelay(retry, null, transientAttempt);

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

                    // Pass the token this attempt actually used: a straggler must not clear
                    // a token that was already refreshed by someone else.
                    await _tokenProvider.InvalidateAsync(token, cancellationToken).ConfigureAwait(false);

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
                        var delay = RetryHelper.ComputeDelay(retry, failure.RetryAfter, transientAttempt);

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
            // A cancellation the caller asked for is not a failure — reporting it would
            // put noise in every consumer's error metrics on ordinary shutdowns.
            var callerCancelled =
                ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

            if (!failureReported && !callerCancelled)
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
