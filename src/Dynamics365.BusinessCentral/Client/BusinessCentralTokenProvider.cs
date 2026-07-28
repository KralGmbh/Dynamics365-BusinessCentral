using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Client;

/// <summary>
/// Acquires and caches the OAuth2 client-credentials access token.
/// <para>
/// This is registered as a <b>singleton</b> so the cache is shared by every
/// <see cref="BusinessCentralClient"/> instance. Typed HTTP clients are transient,
/// so keeping the cache on the client itself would mean a fresh token request for
/// every injection.
/// </para>
/// </summary>
internal sealed class BusinessCentralTokenProvider : IDisposable
{
    private readonly HttpClient _http;
    private readonly BusinessCentralOptions _options;
    private readonly IBusinessCentralObserver _observer;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private CachedAccessToken? _token;

    /// <summary>Subtracted from the advertised lifetime so a token is never used at the edge of expiry.</summary>
    private const int ExpirySafetyMarginSeconds = 60;

    private static readonly JsonSerializerOptions _jsonOptions = BusinessCentralJson.Options;

    public BusinessCentralTokenProvider(
        HttpClient http,
        IOptions<BusinessCentralOptions> options,
        IBusinessCentralObserver? observer = null)
    {
        _http = http;
        _options = options.Value;
        _observer = observer ?? new NullBusinessCentralObserver();
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var current = _token;
        if (current is { IsExpired: false })
        {
            NotifyServedFromCache(current.ExpiresAt);
            return current.Token;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            current = _token;
            if (current is { IsExpired: false })
            {
                NotifyServedFromCache(current.ExpiresAt);
                return current.Token;
            }

            var endpoint = _options.ResolvedTokenEndpoint;

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

            var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            res.RequestMessage ??= req;

            if (!res.IsSuccessStatusCode)
                throw await BusinessCentralExceptionFactory.CreateAsync(res, cancellationToken).ConfigureAwait(false);

            var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json, _jsonOptions)
                                ?? throw new JsonException("Token response was null.");

            var expiresAt = DateTime.UtcNow + CacheLifetime(tokenResponse.ExpiresIn);

            _token = new CachedAccessToken
            {
                Token = tokenResponse.AccessToken,
                ExpiresAt = expiresAt
            };

            _observer.OnTokenRefreshed(new BusinessCentralTokenInfo
            {
                ExpiresAt = expiresAt,
                FromCache = false
            });

            return _token.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _token = null; }
        finally { _tokenLock.Release(); }
    }

    /// <summary>
    /// How long a freshly issued token may be cached: its advertised lifetime less a safety
    /// margin, so it is never used right at the edge of expiry.
    /// </summary>
    /// <remarks>
    /// Subtracting a fixed margin would put the expiry in the past for any token whose
    /// lifetime is shorter than the margin, marking it expired on arrival and forcing a
    /// fresh request on every single call. For those, half the lifetime is used instead.
    /// </remarks>
    internal static TimeSpan CacheLifetime(int expiresInSeconds)
    {
        if (expiresInSeconds <= 0)
            return TimeSpan.Zero;

        var lifetime = TimeSpan.FromSeconds(expiresInSeconds);
        var margin = TimeSpan.FromSeconds(ExpirySafetyMarginSeconds);

        if (margin >= lifetime)
            margin = TimeSpan.FromTicks(lifetime.Ticks / 2);

        return lifetime - margin;
    }

    private void NotifyServedFromCache(DateTime expiresAt)
    {
        _observer.OnTokenServedFromCache(new BusinessCentralTokenInfo
        {
            ExpiresAt = expiresAt,
            FromCache = true
        });
    }

    public void Dispose() => _tokenLock.Dispose();

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
