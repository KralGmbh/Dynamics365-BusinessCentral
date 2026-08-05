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
        _observer = SafeBusinessCentralObserver.Wrap(observer);
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

            var retry = _options.Retry ?? new BusinessCentralRetryOptions();
            var maxAttempts = retry.Enabled ? Math.Max(1, retry.MaxAttempts) : 1;
            var transientAttempt = 0;

            // Retrying inside the lock is deliberate: every concurrent caller needs this
            // token, so they would all fail with the same transient error anyway. Backoff
            // here is backoff for all of them.
            while (true)
            {
                _observer.OnTokenRequested();

                BusinessCentralException failure;

                try
                {
                    // Both are fully consumed here — nothing escapes this method — so they
                    // can be scoped. Disposing the request also releases the
                    // FormUrlEncodedContent, which carries the client secret. The content
                    // must be rebuilt per attempt: a sent HttpRequestMessage cannot be
                    // replayed.
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new FormUrlEncodedContent(form)
                    };
                    req.AddJsonHeaders();

                    using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                    res.RequestMessage ??= req;

                    if (res.IsSuccessStatusCode)
                    {
                        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                        var tokenResponse = ReadTokenResponse(json, res, endpoint);

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

                    failure = await BusinessCentralExceptionFactory
                        .CreateAsync(res, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (RetryHelper.IsNetworkFailure(ex, cancellationToken))
                {
                    failure = new BusinessCentralConnectionException(
                        RetryHelper.NetworkFailureMessage(ex, "the token endpoint"),
                        HttpMethod.Post.Method, endpoint, ex);
                }

                // Both surfaces are the same exception hierarchy, so the caller — and
                // BusinessCentralClient.GetAsync, which swallows a 404 as "no such entity" —
                // needs to be able to tell a token failure from an answer about the entity.
                failure.IsTokenAcquisitionFailure = true;

                // The client_credentials grant has no side effects, so replay is
                // unconditionally safe — none of the POST-ambiguity reasoning from the data
                // pipeline applies. Bad credentials (400/401) are not transient and throw
                // immediately.
                if (!failure.IsTransient || transientAttempt + 1 >= maxAttempts)
                    throw failure;

                transientAttempt++;

                var fromRetryAfter = retry.HonorRetryAfter && failure.RetryAfter != null;
                var delay = RetryHelper.ComputeDelay(retry, failure.RetryAfter, transientAttempt);

                _observer.OnRequestRetrying(new BusinessCentralRetryInfo
                {
                    Method = HttpMethod.Post.Method,
                    Url = endpoint,
                    StatusCode = (int)failure.StatusCode,
                    Attempt = transientAttempt,
                    Delay = delay,
                    FromRetryAfter = fromRetryAfter
                });

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Parses a <c>200</c> from the token endpoint, failing inside the client's own exception
    /// contract rather than outside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways a success response can still be unusable. Malformed JSON threw a raw
    /// <see cref="JsonException"/>, which escaped <see cref="BusinessCentralException"/>
    /// entirely — a caller doing <c>catch (BusinessCentralException)</c> around every call,
    /// which is the documented contract, never saw it. And a well-formed body carrying no
    /// <c>access_token</c> was cached as an empty string, then sent as a bare <c>Bearer</c> on
    /// every subsequent request, surfacing as a <c>401</c> loop that blames Business Central
    /// for what the token endpoint did.
    /// </para>
    /// <para>
    /// Both become a <see cref="BusinessCentralServerException"/> carrying the endpoint, but
    /// deliberately not the raw body: even malformed token responses can contain an access,
    /// refresh or ID token, and exception/observer payloads routinely enter logs. The status
    /// is the response's own — a <c>200</c> here is exactly the surprise worth reporting.
    /// </para>
    /// <para>
    /// The redaction is scoped to the success path <b>on purpose</b>. A non-2xx from the token
    /// endpoint still carries its body, because an OAuth2 error response cannot contain a token
    /// by construction, and its <c>error</c>/<c>error_description</c> pair (the <c>AADSTS…</c>
    /// code, for Entra) is the entire diagnosis for a credential or tenant misconfiguration.
    /// Withholding that would cost the one thing worth having and protect nothing.
    /// </para>
    /// </remarks>
    private TokenResponse ReadTokenResponse(string json, HttpResponseMessage res, string endpoint)
    {
        TokenResponse? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<TokenResponse>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw TokenResponseFailure(
                "Could not parse the response from the token endpoint.", res, endpoint, ex);
        }

        if (parsed is null)
            throw TokenResponseFailure("The token endpoint returned an empty response.", res, endpoint);

        if (string.IsNullOrWhiteSpace(parsed.AccessToken))
        {
            throw TokenResponseFailure(
                "The token endpoint returned a response with no access_token.", res, endpoint);
        }

        return parsed;
    }

    private BusinessCentralServerException TokenResponseFailure(
        string message,
        HttpResponseMessage res,
        string endpoint,
        Exception? inner = null)
    {
        var failure = new BusinessCentralServerException(
            message, res.StatusCode, HttpMethod.Post.Method, endpoint, null, null, null, inner)
        {
            IsTokenAcquisitionFailure = true
        };

        // Reported as the failure itself, not as the inner JsonException: a missing
        // access_token has no inner exception at all, and BusinessCentralErrorInfo.Exception
        // is not nullable.
        _observer.OnDeserializationFailed(new BusinessCentralErrorInfo
        {
            Method = HttpMethod.Post.Method,
            Url = endpoint,
            StatusCode = (int)res.StatusCode,
            // Never expose an identity-provider response through diagnostics: malformed JSON
            // can still contain credential material that must not reach logs.
            ResponseBody = null,
            Exception = failure
        });

        return failure;
    }

    /// <summary>
    /// Clears the cached token, but only if it is still the one the caller found rejected.
    /// A caller racing behind a refresh — its <c>401</c> was for a token that has since been
    /// replaced — must not throw away the fresh token, or concurrent <c>401</c>s cascade
    /// into a refresh per caller.
    /// </summary>
    /// <param name="staleToken">The token the rejected request actually sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InvalidateAsync(string staleToken, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is { } current && current.Token == staleToken)
                _token = null;
        }
        finally
        {
            _tokenLock.Release();
        }
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
