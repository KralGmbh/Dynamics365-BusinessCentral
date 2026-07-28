using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Covers request construction across retries.
/// </summary>
/// <remarks>
/// <see cref="FakeHttpHandler"/> never disposes anything, so it cannot reproduce the real
/// <see cref="HttpClient"/> contract: request content is disposed once a send completes.
/// Replaying a previously sent request therefore throws <see cref="ObjectDisposedException"/>
/// for anything carrying a body. These tests use a handler that disposes content the way a
/// real send does, so a regression to request-reuse fails here instead of in production on
/// the first token expiry during a write.
/// </remarks>
public class RequestReplayTests
{
    /// <summary>Mimics HttpClient, which disposes request content once a send completes.</summary>
    private sealed class ContentDisposingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public ContentDisposingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            => _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Read the body the way a real send would, then release it.
            if (request.Content != null)
                await request.Content.ReadAsByteArrayAsync(cancellationToken);

            var response = _handler(request);

            request.Content?.Dispose();

            return response;
        }
    }

    private static BusinessCentralClient RealisticClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new ContentDisposingHandler(handler));

        var options = new BusinessCentralOptions
        {
            BaseUrl = "https://test",
            Company = "Test",
            TenantId = "tenant",
            ClientId = "client",
            ClientSecret = "secret",
            Scope = "scope",
            TokenEndpoint = "https://auth/{TenantId}",
            Retry = new BusinessCentralRetryOptions
            {
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            }
        };

        return new BusinessCentralClient(http, Microsoft.Extensions.Options.Options.Create(options));
    }

    [Fact]
    public async Task Post_With_Body_Survives_A_Throttled_Retry()
    {
        var calls = 0;

        var client = RealisticClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":3600}");

            calls++;

            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("slow down")
                }
                : TestBase.Json("{\"id\":\"1\",\"name\":\"x\"}");
        });

        var result = await client.PostAsync("orders", new TestPatchEntity { Id = "1", Name = "x" });

        Assert.Equal("x", result.Name);
        Assert.Equal(2, calls);
    }

    // The most likely real-world trigger: a token expires mid-write.
    [Fact]
    public async Task Patch_With_Body_Survives_A_401_Retry()
    {
        var dataCalls = 0;
        var bodies = new List<string>();

        var client = RealisticClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":3600}");

            bodies.Add(req.Content!.ReadAsStringAsync().Result);
            dataCalls++;

            return dataCalls == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("expired")
                }
                : TestBase.Json("{\"id\":\"1\",\"name\":\"Updated\"}");
        });

        var result = await client.PatchAsync("orders", "1", new TestPatchEntity { Id = "1", Name = "Updated" });

        Assert.Equal("Updated", result.Name);
        Assert.Equal(2, dataCalls);

        // The replayed attempt must carry the same body, not an empty one.
        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains("Updated", bodies[1]);
    }

    [Fact]
    public async Task Put_With_Body_Survives_A_Transient_Retry()
    {
        var calls = 0;

        var client = RealisticClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":3600}");

            calls++;

            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("down")
                }
                : TestBase.Json("{\"id\":\"1\",\"name\":\"x\"}");
        });

        await client.PutAsync("orders", "1", new TestPatchEntity { Id = "1", Name = "x" });

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Headers_Are_Rebuilt_On_Every_Attempt()
    {
        var ifMatch = new List<string>();
        var prefer = new List<string>();
        var dataCalls = 0;

        var client = RealisticClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":3600}");

            ifMatch.Add(string.Join(",", req.Headers.GetValues("If-Match")));
            prefer.Add(string.Join(",", req.Headers.GetValues("Prefer")));
            dataCalls++;

            return dataCalls == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("x") }
                : TestBase.Json("{\"id\":\"1\",\"name\":\"x\"}");
        });

        await client.PatchAsync("orders", "1", new TestPatchEntity(), "W/\"etag-1\"");

        Assert.Equal(["W/\"etag-1\"", "W/\"etag-1\""], ifMatch);
        Assert.All(prefer, p => Assert.Contains("return=representation", p));
    }

    [Fact]
    public async Task Get_Survives_Repeated_Transient_Retries()
    {
        var calls = 0;

        var client = RealisticClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":3600}");

            calls++;

            return calls < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("down")
                }
                : TestBase.Json("{\"value\":[]}");
        });

        var result = await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Empty(result);
        Assert.Equal(3, calls);
    }

    #region Caller-supplied $skip

    [Fact]
    public async Task QueryAllAsync_Honours_A_Caller_Supplied_Skip()
    {
        var urls = new List<string>();

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            urls.Add(Uri.UnescapeDataString(req.RequestUri!.AbsoluteUri));
            return TestBase.Json("{\"value\":[]}");
        }));

        await client.QueryAllAsync<TestEntity>("orders", options: o => o.WithSkip(100));

        Assert.Contains("$skip=100", urls[0]);
    }

    [Fact]
    public async Task QueryStreamAsync_Continues_Paging_From_The_Supplied_Skip()
    {
        var skips = new List<string>();
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            var query = new Uri(req.RequestUri!.AbsoluteUri).Query;
            skips.Add(Uri.UnescapeDataString(query));

            dataCalls++;

            return dataCalls == 1
                ? TestBase.Json("{\"value\":[{\"id\":1},{\"id\":2}]}")
                : TestBase.Json("{\"value\":[]}");
        }));

        var all = await client.QueryAllAsync<TestEntity>(
            "orders", options: o => o.WithPageSize(2).WithSkip(10));

        Assert.Equal(2, all.Count);
        Assert.Contains("$skip=10", skips[0]);

        // Second page continues from the offset rather than restarting.
        Assert.Contains("$skip=12", skips[1]);
    }

    #endregion
}
