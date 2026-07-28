using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Client;

public sealed class BusinessCentralClient : IBusinessCentralClient
{
    private readonly HttpClient _http;
    private readonly BusinessCentralUrlBuilder _urlBuilder;
    private readonly BusinessCentralTokenProvider _tokenProvider;
    private readonly IBusinessCentralObserver _observer;

    private const string BearerScheme = "Bearer";

    /// <summary>Default page size used by <see cref="QueryAllAsync{TEntity}"/>.</summary>
    private const int DefaultPageSize = 1000;

    private static readonly JsonSerializerOptions _jsonOptions = BusinessCentralJson.Options;

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

        var resolvedOptions = options.Value;

        _observer = observer ?? new NullBusinessCentralObserver();

        // No mutation of the supplied HttpClient: it may be pooled or shared, and
        // setting Timeout/DefaultRequestHeaders after first use throws. Per-request
        // headers are applied in HttpRequestExtensions.AddJsonHeaders instead.
        _tokenProvider = tokenProvider ?? new BusinessCentralTokenProvider(http, options, _observer);

        _urlBuilder = new BusinessCentralUrlBuilder(
            resolvedOptions.BaseUrl,
            resolvedOptions.Company);
    }


    public Task<List<TEntity>> QueryAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
        => QueryAsync<TEntity>(path, filter?.Value ?? string.Empty, options, select, cancellationToken);

    public async Task<List<TEntity>> QueryAsync<TEntity>(
        string path,
        string filter,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var queryOptions = new QueryOptions();
        options?.Invoke(queryOptions);

        var page = await QueryPageAsync<TEntity>(path, filter, queryOptions, select, cancellationToken);

        return page.Value;
    }

    public async Task<TResponse> QueryRawAsync<TResponse>(
        string path,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        // BuildRawUrl (not BuildEntityUrl) so a caller-supplied query string such as
        // "salesOrders?$top=5" survives instead of being percent-encoded into the path.
        var url = _urlBuilder.BuildRawUrl(path);

        var req = CreateJsonRequest(HttpMethod.Get, url);

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        return await DeserializeAsync<TResponse>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken);
    }

    public async Task<List<TEntity>> QueryAllAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var all = new List<TEntity>();

        var baseOptions = new QueryOptions();
        options?.Invoke(baseOptions);

        var pageSize = baseOptions.Top ?? DefaultPageSize;
        var filterValue = filter?.Value ?? string.Empty;
        var skip = 0;

        var page = await QueryPageAsync<TEntity>(
            path, filterValue, BuildPageOptions(baseOptions, pageSize, skip), select, cancellationToken);

        while (true)
        {
            all.AddRange(page.Value);

            // Server-driven paging wins: when Business Central returns @odata.nextLink
            // it decides the page size, so a short page is not the end of the collection.
            if (!string.IsNullOrWhiteSpace(page.NextLink))
            {
                page = await QueryNextPageAsync<TEntity>(page.NextLink!, cancellationToken);
                continue;
            }

            // No nextLink and a short page means the collection is exhausted.
            if (page.Value.Count < pageSize)
                break;

            skip += page.Value.Count;

            page = await QueryPageAsync<TEntity>(
                path, filterValue, BuildPageOptions(baseOptions, pageSize, skip), select, cancellationToken);
        }

        return all;
    }

    private static QueryOptions BuildPageOptions(QueryOptions baseOptions, int pageSize, int skip)
    {
        var options = new QueryOptions()
            .WithTop(pageSize)
            .WithSkip(skip);

        if (baseOptions.OrderBy != null)
            options.OrderBy = baseOptions.OrderBy;

        return options;
    }

    private async Task<ODataWrapper<TEntity>> QueryPageAsync<TEntity>(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        CancellationToken cancellationToken)
    {
        var url = _urlBuilder.BuildQueryUrl(path, filter, options, select);

        var req = CreateJsonRequest(HttpMethod.Get, url);

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        return await DeserializeAsync<ODataWrapper<TEntity>>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken);
    }

    private async Task<ODataWrapper<TEntity>> QueryNextPageAsync<TEntity>(
        string absoluteUrl,
        CancellationToken cancellationToken)
    {
        var req = CreateJsonRequest(HttpMethod.Get, absoluteUrl);

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        return await DeserializeAsync<ODataWrapper<TEntity>>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken);
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

    public async Task<T> PatchAsync<T>(
        string path,
        string systemId,
        T payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where T : class
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        var req = CreateJsonRequest(HttpMethod.Patch, url, payload);

        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        req.AddReturnRepresentationPreference();

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        return await ReadEntityOrEchoAsync(
            res,
            payload,
            "Failed to deserialize PATCH response.",
            cancellationToken);
    }

    public async Task<T> PostAsync<T>(
        string path,
        T payload,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var url = _urlBuilder.BuildEntityUrl(path);

        var req = CreateJsonRequest(HttpMethod.Post, url, payload);

        req.AddReturnRepresentationPreference();

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        return await ReadEntityOrEchoAsync(
            res,
            payload,
            "Failed to deserialize POST response.",
            cancellationToken);
    }

    public async Task<T> PutAsync<T>(
        string path,
        string systemId,
        T payload,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
        where T : class
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        var req = CreateJsonRequest(HttpMethod.Put, url, payload);

        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        req.AddReturnRepresentationPreference();

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        return await ReadEntityOrEchoAsync(
            res,
            payload,
            "Failed to deserialize PUT response.",
            cancellationToken);
    }

    public async Task DeleteAsync(
        string path,
        string systemId,
        string ifMatch = "*",
        CancellationToken cancellationToken = default)
    {
        var url = _urlBuilder.BuildEntityUrl(path, systemId);

        var req = CreateJsonRequest(HttpMethod.Delete, url);

        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        if (res.StatusCode != HttpStatusCode.NoContent &&
            res.StatusCode != HttpStatusCode.OK)
        {
            throw new BusinessCentralServerException(
                $"DELETE expected 204 NoContent but got {(int)res.StatusCode}.",
                res.StatusCode,
                req.Method.Method,
                req.RequestUri!.ToString(),
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

        var json = await res.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            return payload;

        return Deserialize<T>(json, res, errorMessage);
    }

    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(
        HttpRequestMessage originalRequest,
        CancellationToken cancellationToken)
    {
        var requestInfo = new BusinessCentralRequestInfo
        {
            Method = originalRequest.Method.Method,
            Url = originalRequest.RequestUri!.ToString()
        };

        _observer.OnRequestStarting(requestInfo);

        // Guards against reporting the same failure twice: once at the throw site and
        // again in the catch-all below.
        var failureReported = false;

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await _tokenProvider.GetTokenAsync(cancellationToken);

                var req = originalRequest.Clone();
                req.Headers.Authorization =
                    new AuthenticationHeaderValue(BearerScheme, token);

                var stopwatch = Stopwatch.StartNew();

                var res = await _http.SendAsync(req, cancellationToken);
                res.RequestMessage ??= req;

                stopwatch.Stop();

                if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _observer.OnRequestFailed(new BusinessCentralErrorInfo
                    {
                        Method = req.Method.Method,
                        Url = req.RequestUri!.ToString(),
                        Duration = stopwatch.Elapsed,
                        StatusCode = (int)res.StatusCode,
                        ResponseBody = await ReadBodySafeAsync(res, cancellationToken),
                        Exception = new UnauthorizedAccessException("Unauthorized – retrying with refreshed token")
                    });

                    await _tokenProvider.InvalidateAsync(cancellationToken);
                    continue;
                }

                if (!res.IsSuccessStatusCode)
                {
                    var failure = await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken);

                    _observer.OnRequestFailed(new BusinessCentralErrorInfo
                    {
                        Method = req.Method.Method,
                        Url = req.RequestUri!.ToString(),
                        Duration = stopwatch.Elapsed,
                        StatusCode = (int)res.StatusCode,
                        ResponseBody = failure.ResponseBody,
                        Exception = failure
                    });

                    failureReported = true;
                    throw failure;
                }

                _observer.OnRequestSucceeded(new BusinessCentralRequestInfo
                {
                    Method = req.Method.Method,
                    Url = req.RequestUri!.ToString(),
                    Duration = stopwatch.Elapsed,
                    StatusCode = (int)res.StatusCode
                });

                return res;
            }

            throw new InvalidOperationException("Unexpected state in SendWithAuthRetryAsync");
        }
        catch (Exception ex)
        {
            if (!failureReported)
            {
                _observer.OnRequestFailed(new BusinessCentralErrorInfo
                {
                    Method = originalRequest.Method.Method,
                    Url = originalRequest.RequestUri!.ToString(),
                    Exception = ex
                });
            }

            throw;
        }
    }

    private static async Task<string?> ReadBodySafeAsync(
        HttpResponseMessage res,
        CancellationToken cancellationToken)
    {
        try
        {
            return await res.Content.ReadAsStringAsync(cancellationToken);
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
        var json = await res.Content.ReadAsStringAsync(cancellationToken);

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

    private sealed class ODataWrapper<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = [];

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }
}
