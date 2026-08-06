using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;
using System.Text.Json;

namespace Dynamics365.BusinessCentral.Tests;

public class TokenProviderTests
{
    private static BusinessCentralTokenProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        TestObserver? observer = null)
    {
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

        return new BusinessCentralTokenProvider(
            new HttpClient(new FakeHttpHandler(handler)),
            Microsoft.Extensions.Options.Options.Create(options),
            observer);
    }

    private static HttpResponseMessage Token(string value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"access_token\":\"{value}\",\"expires_in\":3600}}")
        };

    // A straggler whose 401 was for an already-replaced token must not clear the fresh
    // one — otherwise N concurrent 401s cascade into N refreshes.
    [Fact]
    public async Task Invalidate_With_A_Stale_Token_Keeps_The_Fresh_One()
    {
        var tokenRequests = 0;

        var provider = CreateProvider(_ =>
        {
            tokenRequests++;
            return Token($"token-{tokenRequests}");
        });

        var first = await provider.GetTokenAsync(CancellationToken.None);
        Assert.Equal("token-1", first);

        // The rejected request used some older token, not the cached one.
        await provider.InvalidateAsync("some-older-token", CancellationToken.None);

        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", second);
        Assert.Equal(1, tokenRequests);
    }

    [Fact]
    public async Task Invalidate_With_The_Cached_Token_Clears_It()
    {
        var tokenRequests = 0;

        var provider = CreateProvider(_ =>
        {
            tokenRequests++;
            return Token($"token-{tokenRequests}");
        });

        var first = await provider.GetTokenAsync(CancellationToken.None);

        await provider.InvalidateAsync(first, CancellationToken.None);

        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", second);
        Assert.Equal(2, tokenRequests);
    }

    #region A 200 from the token endpoint is not automatically a token

    /// <summary>
    /// Malformed JSON threw a raw <c>JsonException</c>, straight through the documented
    /// <c>catch (BusinessCentralException)</c> contract.
    /// </summary>
    [Fact]
    public async Task Malformed_Token_Response_Throws_Inside_The_Exception_Contract()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not json")
        });

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(
            () => provider.GetTokenAsync(CancellationToken.None));

        Assert.True(ex.IsTokenAcquisitionFailure);
        Assert.Equal("https://auth/tenant", ex.RequestUrl);
        Assert.Null(ex.ResponseBody);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    /// <summary>
    /// A well-formed body with no <c>access_token</c> used to be cached as an empty string and
    /// then sent as a bare <c>Bearer</c> header, surfacing as a 401 loop that blamed Business
    /// Central for what the token endpoint did.
    /// </summary>
    [Theory]
    [InlineData("{\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"   \",\"expires_in\":3600}")]
    [InlineData("null")]
    public async Task Token_Response_Without_An_Access_Token_Is_Rejected(string body)
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        });

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(
            () => provider.GetTokenAsync(CancellationToken.None));

        Assert.True(ex.IsTokenAcquisitionFailure);
        Assert.False(ex.IsTransient);
        Assert.Null(ex.ResponseBody);
    }

    /// <summary>
    /// A malformed success can still contain an access token. Neither the exception nor the
    /// observer may copy identity-provider bodies into data that is commonly logged.
    /// </summary>
    [Fact]
    public async Task Rejected_Token_Response_Is_Redacted_From_Diagnostics()
    {
        const string secret = "credential-that-must-not-be-logged";
        var observer = new TestObserver();

        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"access_token\":\"{secret}\",not-json")
        }, observer);

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(
            () => provider.GetTokenAsync(CancellationToken.None));

        Assert.Null(ex.ResponseBody);
        Assert.DoesNotContain(secret, ex.ToString(), StringComparison.Ordinal);

        var diagnostic = Assert.Single(observer.DeserializationFailures);
        Assert.Null(diagnostic.ResponseBody);
        Assert.DoesNotContain(secret, diagnostic.Exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A malformed response is never cached, so the next call retries the endpoint.</summary>
    [Fact]
    public async Task A_Rejected_Token_Response_Is_Not_Cached()
    {
        var calls = 0;

        var provider = CreateProvider(_ =>
            ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
                : Token("good"));

        await Assert.ThrowsAsync<BusinessCentralServerException>(
            () => provider.GetTokenAsync(CancellationToken.None));

        Assert.Equal("good", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(2, calls);
    }

    #endregion

    #region Token failures are distinguishable from answers about the entity

    /// <summary>
    /// <c>GetAsync</c> swallows a 404 as "no such entity". Its try also spans token
    /// acquisition, and the token provider reports through the same hierarchy — so a
    /// misconfigured TokenEndpoint answering 404 was returned as a null entity, leaving every
    /// read silently empty with nothing thrown to say authentication never happened.
    /// </summary>
    [Fact]
    public async Task Token_Endpoint_404_Is_Not_Swallowed_As_A_Missing_Entity()
    {
        var client = TestBase.CreateClient(req =>
            req.RequestUri!.AbsoluteUri.Contains("auth")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"NotFound\"}}")
                }
                : TestBase.Json("{\"id\":1}"));

        var ex = await Assert.ThrowsAsync<BusinessCentralNotFoundException>(
            () => client.GetAsync<SalesOrder>("salesOrders", "1"));

        Assert.True(ex.IsTokenAcquisitionFailure);
    }

    /// <summary>The entity's own 404 is still a null, which is the whole point of the catch.</summary>
    [Fact]
    public async Task Entity_404_Is_Still_Returned_As_Null()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"code\":\"NotFound\"}}")
            }));

        Assert.Null(await client.GetAsync<SalesOrder>("salesOrders", "1"));
    }

    #endregion
}
