using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Client;

public sealed class BusinessCentralClient : IBusinessCentralClient
{
    private readonly HttpClient _http;
    private readonly BusinessCentralOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private CachedAccessToken? _token;

    private const string BearerScheme = "Bearer";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public BusinessCentralClient(HttpClient http, BusinessCentralOptions options)
    {
        _http = http;
        _options = options;
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
        var json = await res.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var wrapper = JsonSerializer.Deserialize<ODataWrapper<TEntity>>(json, _jsonOptions)
                          ?? throw new JsonException("Response was null.");

            return wrapper.Value;
        }
        catch (JsonException ex)
        {
            throw new BusinessCentralServerException(
                "Failed to deserialize Business Central response.",
                res.StatusCode,
                res.RequestMessage!.Method.Method,
                res.RequestMessage!.RequestUri!.ToString(),
                json,
                null,
                null,
                ex);
        }
    }

    public async Task<TResponse> QueryRawAsync<TResponse>(
        string path,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        var token = await GetTokenAsync(cancellationToken);

        var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.BaseUrl}/Company('{_options.Company}')/{path}");

        req.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);

        var res = await _http.SendAsync(req, cancellationToken);
        res.RequestMessage ??= req;

        if (!res.IsSuccessStatusCode)
            throw await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken);

        var json = await res.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<TResponse>(json, _jsonOptions)
                   ?? throw new JsonException("Response was null.");
        }
        catch (JsonException ex)
        {
            throw new BusinessCentralServerException(
                "Failed to deserialize Business Central response.",
                res.StatusCode,
                req.Method.Method,
                req.RequestUri!.ToString(),
                json,
                null,
                null,
                ex);
        }
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

            if (page.Count == 0)
                break;

            all.AddRange(page);
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
        var token = await GetTokenAsync(cancellationToken);

        var url = $"{_options.BaseUrl}/Company('{_options.Company}')/{path}({systemId})";
        var req = new HttpRequestMessage(HttpMethod.Patch, url);

        req.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        var res = await _http.SendAsync(req, cancellationToken);
        res.RequestMessage ??= req;

        if (!res.IsSuccessStatusCode)
            throw await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken);

        if (res.StatusCode == HttpStatusCode.NoContent)
            return default!;

        var json = await res.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                   ?? throw new JsonException("Response was null.");
        }
        catch (JsonException ex)
        {
            throw new BusinessCentralServerException(
                "Failed to deserialize PATCH response.",
                res.StatusCode,
                req.Method.Method,
                req.RequestUri!.ToString(),
                json,
                null,
                null,
                ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 1;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var isRetry = attempt > 0;

            if (isRetry)
                await InvalidateTokenAsync(cancellationToken);

            var req = await CreateRequestAsync(path, filter, options, select, cancellationToken);
            var res = await _http.SendAsync(req, cancellationToken);

            res.RequestMessage ??= req;

            if (res.StatusCode == HttpStatusCode.Unauthorized && !isRetry)
                continue;

            if (!res.IsSuccessStatusCode)
                throw await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken);

            return res;
        }

        throw new InvalidOperationException("Unexpected state in SendAsync");
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        var url = BuildUrl(path, filter, options, select);

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);

        return req;
    }

    private string BuildUrl(
        string path,
        string filter,
        QueryOptions options,
        IEnumerable<string>? select)
    {
        var url = $"{_options.BaseUrl}/Company('{_options.Company}')/{path}";

        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(filter) && filter != "true")
            query.Add("$filter=" + Uri.EscapeDataString(filter));

        if (select != null)
            query.Add("$select=" + string.Join(",", select.Select(Uri.EscapeDataString)));

        if (options.Top != null)
            query.Add("$top=" + options.Top);

        if (options.Skip != null)
            query.Add("$skip=" + options.Skip);

        if (options.OrderBy != null)
            query.Add("$orderby=" + Uri.EscapeDataString(options.OrderBy));

        if (query.Count > 0)
            url += "?" + string.Join("&", query);

        return url;
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
            return current.Token;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            current = _token;
            if (current != null && !current.IsExpired)
                return current.Token;

            var endpoint = _options.TokenEndpoint.Replace("{TenantId}", _options.TenantId);

            var body = new StringContent(
                $"client_id={_options.ClientId}&client_secret={_options.ClientSecret}&scope={_options.Scope}&grant_type=client_credentials",
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = body };

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

            return _token.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
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
