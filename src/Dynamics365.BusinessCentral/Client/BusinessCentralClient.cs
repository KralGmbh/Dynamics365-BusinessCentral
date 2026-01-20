using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Client;

public sealed class BusinessCentralClient : IBusinessCentralClient
{
    private readonly HttpClient _http;
    private readonly BusinessCentralOptions _options;
    private readonly BusinessCentralUrlBuilder _urlBuilder;
    private readonly IBusinessCentralObserver _observer;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private CachedAccessToken? _token;

    private const string BearerScheme = "Bearer";

    private static readonly JsonSerializerOptions _jsonOptions = BusinessCentralJson.Options;

    public BusinessCentralClient(
        HttpClient http,
        BusinessCentralOptions options,
        IBusinessCentralObserver? observer = null)
    {
        _http = http;
        _options = options;

        _observer = observer ?? new NullBusinessCentralObserver();

        _http.Timeout = TimeSpan.FromSeconds(100);

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Dynamics365.BusinessCentral.Client/1.0");

        _urlBuilder = new BusinessCentralUrlBuilder(
            options.BaseUrl,
            options.Company);
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

        var res = await SendAsync(path, filter, queryOptions, select, cancellationToken);
        var wrapper = await DeserializeAsync<ODataWrapper<TEntity>>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken);

        return wrapper.Value;
    }

    public async Task<TResponse> QueryRawAsync<TResponse>(
        string path,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        var url = _urlBuilder.BuildEntityUrl(path);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.AddJsonHeaders();

        return await SendWithRetryAndDeserializeAsync<TResponse>(req, cancellationToken);
    }

    public async Task<List<TEntity>> QueryAllAsync<TEntity>(
        string path,
        ODataFilter? filter = null,
        Action<QueryOptions>? options = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        var all = new List<TEntity>();
        var skip = 0;

        var baseOptions = new QueryOptions();
        options?.Invoke(baseOptions);

        var pageSize = baseOptions.Top ?? 1000;

        while (true)
        {
            var page = await QueryAsync<TEntity>(
                path,
                filter,
                o =>
                {
                    o.WithTop(pageSize).WithSkip(skip);
                    if (baseOptions.OrderBy != null)
                        o.OrderBy = baseOptions.OrderBy;
                },
                select,
                cancellationToken);

            all.AddRange(page);

            if (page.Count < pageSize)
                break;

            skip += page.Count;
        }

        return all;
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

        var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.AddJsonHeaders();

        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, _jsonOptions),
            System.Text.Encoding.UTF8,
            "application/json");

        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        if (res.StatusCode == HttpStatusCode.NoContent)
        {
            throw new BusinessCentralServerException(
                "PATCH returned 204 NoContent – no entity was returned.",
                res.StatusCode,
                req.Method.Method,
                req.RequestUri!.ToString(),
                null,
                null,
                null);
        }

        return await DeserializeAsync<T>(
            res,
            "Failed to deserialize PATCH response.",
            cancellationToken);
    }

    private async Task<T> SendWithRetryAndDeserializeAsync<T>(
        HttpRequestMessage req,
        CancellationToken cancellationToken)
        where T : class
    {
        var res = await SendWithAuthRetryAsync(req, cancellationToken);

        return await DeserializeAsync<T>(
            res,
            "Failed to deserialize Business Central response.",
            cancellationToken);
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

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await GetTokenAsync(cancellationToken);

                var req = originalRequest.Clone();
                req.Headers.Authorization =
                    new AuthenticationHeaderValue(BearerScheme, token);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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
                        Exception = new UnauthorizedAccessException("Unauthorized – retrying with refreshed token")
                    });

                    await InvalidateTokenAsync(cancellationToken);
                    continue;
                }

                if (!res.IsSuccessStatusCode)
                {
                    _observer.OnRequestFailed(new BusinessCentralErrorInfo
                    {
                        Method = req.Method.Method,
                        Url = req.RequestUri!.ToString(),
                        Duration = stopwatch.Elapsed,
                        StatusCode = (int)res.StatusCode,
                        Exception = new HttpRequestException($"HTTP {(int)res.StatusCode}")
                    });

                    throw await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken);
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
            _observer.OnRequestFailed(new BusinessCentralErrorInfo
            {
                Method = originalRequest.Method.Method,
                Url = originalRequest.RequestUri!.ToString(),
                Exception = ex
            });

            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        CancellationToken cancellationToken)
    {
        var url = _urlBuilder.BuildQueryUrl(path, filter, options, select);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.AddJsonHeaders();

        return await SendWithAuthRetryAsync(req, cancellationToken);
    }

    private async Task InvalidateTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try { _token = null; }
        finally { _tokenLock.Release(); }
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var current = _token;
        if (current != null && !current.IsExpired)
        {
            NotifyTokenRefreshed(current.ExpiresAt, true);

            return current.Token;
        }

        await _tokenLock.WaitAsync(cancellationToken);

        try
        {
            current = _token;
            if (current != null && !current.IsExpired)
            {
                NotifyTokenRefreshed(current.ExpiresAt, true);

                return current.Token;
            }

            var endpoint = _options.TokenEndpoint.Replace("{TenantId}", _options.TenantId);

            var form = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = _options.Scope,
                ["grant_type"] = "client_credentials"
            };

            var body = new FormUrlEncodedContent(form);

            _observer.OnTokenRequested();

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = body };
            req.AddJsonHeaders();

            var res = await _http.SendAsync(req, cancellationToken);
            res.RequestMessage ??= req;

            if (!res.IsSuccessStatusCode)
                throw await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken);

            var json = await res.Content.ReadAsStringAsync(cancellationToken);

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json, _jsonOptions)
                                ?? throw new JsonException("Token response was null.");

            var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);

            _token = new CachedAccessToken
            {
                Token = tokenResponse.AccessToken,
                ExpiresAt = expiresAt
            };

            NotifyTokenRefreshed(expiresAt, false);

            return _token.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<T> DeserializeAsync<T>(
        HttpResponseMessage res,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var json = await res.Content.ReadAsStringAsync(cancellationToken);

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

    private void NotifyTokenRefreshed(DateTime expiresAt, bool fromCache)
    {
        _observer.OnTokenRefreshed(new BusinessCentralTokenInfo
        {
            ExpiresAt = expiresAt,
            FromCache = fromCache
        });
    }

    private sealed class ODataWrapper<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = [];
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class CachedAccessToken
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }
}
