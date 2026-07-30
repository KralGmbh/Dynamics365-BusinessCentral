using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dynamics365.BusinessCentral.Testing;

/// <summary>
/// A scripted Business Central for tests: a <b>real</b> <see cref="BusinessCentralClient"/>
/// wired over a fake transport, so URL building, filter rendering, paging, retry and
/// deserialization all run exactly as in production — only the HTTP responses are yours.
/// </summary>
/// <remarks>
/// <para>
/// Script responses with the <c>Enqueue*</c> methods (consumed in order, one per request),
/// run the code under test against <see cref="Client"/>, then assert on
/// <see cref="Requests"/> — including the exact OData URL that was produced:
/// </para>
/// <code>
/// using var bc = new FakeBusinessCentral();
/// bc.EnqueuePage(new Item { No = "X", Description = "Pump" });
///
/// var items = await bc.Client.QueryAsync&lt;Item&gt;("items",
///     Filter.Equals("no", "X"), select: ["no", "description"]);
///
/// Assert.Equal(
///     "/Company('TEST')/items?$filter=no eq 'X'&amp;$select=no,description",
///     bc.Requests.Single().DecodedPathAndQuery);
/// </code>
/// <para>
/// Token acquisition is answered automatically and counted in
/// <see cref="TokenRequestCount"/> rather than recorded, so tests never script auth. An
/// unscripted data request throws instead of guessing — a test that forgot to enqueue gets
/// a message naming the request, not a silently empty result. Retries are instant
/// (<c>BaseDelay</c>/<c>MaxDelay</c> zero, no jitter) unless reconfigured.
/// </para>
/// </remarks>
public sealed class FakeBusinessCentral : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _scripted = new();
    private readonly List<RecordedBusinessCentralRequest> _requests = [];
    private readonly HttpClient _http;
    private readonly string _baseAuthority;

    private int _tokenRequestCount;

    private const string DefaultBaseUrl = "https://bc.test";

    /// <summary>Creates a fake with test defaults; <paramref name="configure"/> overrides them.</summary>
    /// <param name="configure">
    /// Optional adjustments — e.g. a different <c>Company</c> to assert company-segment
    /// encoding, or non-zero retry delays.
    /// </param>
    public FakeBusinessCentral(Action<BusinessCentralOptions>? configure = null)
    {
        var options = new BusinessCentralOptions
        {
            TenantId = "00000000-0000-0000-0000-000000000001",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            Company = "TEST",
            BaseUrl = DefaultBaseUrl,
            TokenEndpoint = "https://login.test/token",

            // Instant, deterministic retries so failure-path tests never sleep.
            Retry = new BusinessCentralRetryOptions
            {
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                JitterFactor = 0
            }
        };

        configure?.Invoke(options);

        // Relative nextLink values resolve against the configured base, so overriding
        // BaseUrl keeps request capture on one consistent host. Placeholders can only
        // appear in the path, so the authority parses regardless.
        _baseAuthority = Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            ? baseUri.GetLeftPart(UriPartial.Authority)
            : DefaultBaseUrl;

        Handler = new ScriptedHandler(this);
        _http = new HttpClient(Handler);

        Client = new BusinessCentralClient(_http, Microsoft.Extensions.Options.Options.Create(options));
    }

    /// <summary>The client under test. Behaves exactly like the production client.</summary>
    public IBusinessCentralClient Client { get; }

    /// <summary>
    /// The underlying handler, for wiring the fake into a DI container instead of using
    /// <see cref="Client"/> — e.g.
    /// <c>services.AddHttpClient(BusinessCentralHttpClients.Client).ConfigurePrimaryHttpMessageHandler(() => fake.Handler)</c>.
    /// </summary>
    public HttpMessageHandler Handler { get; }

    /// <summary>Every data request sent so far, in order. Token requests are excluded.</summary>
    public IReadOnlyList<RecordedBusinessCentralRequest> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    /// <summary>How many times a token was requested (auto-answered, not recorded).</summary>
    public int TokenRequestCount => Volatile.Read(ref _tokenRequestCount);

    /// <summary>Scripts a collection page containing <paramref name="entities"/>.</summary>
    public FakeBusinessCentral EnqueuePage<TEntity>(params TEntity[] entities) =>
        EnqueuePage(entities.AsEnumerable());

    /// <summary>
    /// Scripts a collection page, optionally with a server-driven continuation and a total
    /// count — the shape Business Central sends for <c>@odata.nextLink</c> paging.
    /// </summary>
    /// <param name="entities">Rows in this page, serialized with the client's JSON options.</param>
    /// <param name="nextLink">
    /// Continuation URL for <c>@odata.nextLink</c>. A relative value is made absolute
    /// against the configured base URL's host, so <c>"page2"</c> works.
    /// </param>
    /// <param name="totalCount">Value for <c>@odata.count</c>, when the page carries one.</param>
    public FakeBusinessCentral EnqueuePage<TEntity>(
        IEnumerable<TEntity> entities,
        string? nextLink = null,
        long? totalCount = null)
    {
        var root = new JsonObject
        {
            ["value"] = JsonSerializer.SerializeToNode(entities.ToList(), BusinessCentralJson.Options)
        };

        if (nextLink != null)
            root["@odata.nextLink"] = MakeAbsolute(nextLink);

        if (totalCount != null)
            root["@odata.count"] = totalCount;

        return EnqueueJson(root.ToJsonString());
    }

    /// <summary>Scripts a single-entity response (the shape of a GET by key or a write echo).</summary>
    public FakeBusinessCentral EnqueueEntity<TEntity>(TEntity entity) =>
        EnqueueJson(JsonSerializer.Serialize(entity, BusinessCentralJson.Options));

    /// <summary>
    /// Scripts a <c>204 No Content</c> — how Business Central answers a write when it
    /// declines to return a representation.
    /// </summary>
    public FakeBusinessCentral EnqueueNoContent() =>
        Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

    /// <summary>Scripts a raw JSON response.</summary>
    public FakeBusinessCentral EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    /// <summary>
    /// Scripts a failure in Business Central's OData error envelope, so the client raises
    /// the matching <see cref="Dynamics365.BusinessCentral.Errors.BusinessCentralException"/>
    /// subtype — no constructing exception types by hand to test a <c>catch</c> branch.
    /// </summary>
    /// <param name="statusCode">HTTP status, e.g. <see cref="HttpStatusCode.TooManyRequests"/>.</param>
    /// <param name="odataCode">OData error code; defaults to <c>Test_Error</c>.</param>
    /// <param name="message">Error message; defaults to a generic one.</param>
    /// <param name="retryAfter">Optional <c>Retry-After</c> delay to send with the response.</param>
    public FakeBusinessCentral EnqueueError(
        HttpStatusCode statusCode,
        string? odataCode = null,
        string? message = null,
        TimeSpan? retryAfter = null)
    {
        var body = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["code"] = odataCode ?? "Test_Error",
                ["message"] = message ?? "Scripted failure."
            }
        }.ToJsonString();

        return Enqueue(_ =>
        {
            var res = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (retryAfter is { } delay)
                res.Headers.Add("Retry-After", ((int)delay.TotalSeconds).ToString());

            return res;
        });
    }

    /// <summary>
    /// Scripts a network-level failure — the request never gets a response. The client
    /// surfaces it as
    /// <see cref="Dynamics365.BusinessCentral.Errors.BusinessCentralConnectionException"/>
    /// (after retries, where its rules allow them).
    /// </summary>
    public FakeBusinessCentral EnqueueNetworkFailure(string message = "Scripted connection failure.") =>
        Enqueue(_ => throw new HttpRequestException(message));

    /// <summary>Scripts an arbitrary response built from the incoming request.</summary>
    public FakeBusinessCentral Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        lock (_gate)
            _scripted.Enqueue(respond);

        return this;
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private string MakeAbsolute(string nextLink) =>
        Uri.TryCreate(nextLink, UriKind.Absolute, out _)
            ? nextLink
            : $"{_baseAuthority}/{nextLink.TrimStart('/')}";

    /// <summary>
    /// The client sends exactly one form-urlencoded POST: the client-credentials grant.
    /// Data writes are always <c>application/json</c>, so a JSON payload that merely
    /// <i>contains</i> the grant string cannot be misclassified.
    /// </summary>
    private static bool IsTokenRequest(HttpRequestMessage request, string? body) =>
        request.Method == HttpMethod.Post &&
        string.Equals(
            request.Content?.Headers.ContentType?.MediaType,
            "application/x-www-form-urlencoded",
            StringComparison.OrdinalIgnoreCase) &&
        body?.Contains("grant_type=client_credentials") == true;

    private HttpResponseMessage Respond(HttpRequestMessage request, string? body)
    {
        // The token request is infrastructure, not behaviour under test: answer it
        // automatically so no test ever scripts auth.
        if (IsTokenRequest(request, body))
        {
            Interlocked.Increment(ref _tokenRequestCount);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"test-token\",\"expires_in\":3600}",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        Func<HttpRequestMessage, HttpResponseMessage> respond;

        lock (_gate)
        {
            _requests.Add(new RecordedBusinessCentralRequest(
                request.Method.Method, request.RequestUri!, body));

            if (_scripted.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No response scripted for {request.Method} {request.RequestUri}. " +
                    "Enqueue one before the call — EnqueuePage for collections, " +
                    "EnqueueEntity for single entities, EnqueueError for failures, " +
                    "EnqueueNoContent for bare writes.");
            }

            respond = _scripted.Dequeue();
        }

        return respond(request);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly FakeBusinessCentral _owner;

        public ScriptedHandler(FakeBusinessCentral owner) => _owner = owner;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return _owner.Respond(request, body);
        }
    }
}
