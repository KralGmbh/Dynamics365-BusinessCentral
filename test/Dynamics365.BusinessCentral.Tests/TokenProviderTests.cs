using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

public class TokenProviderTests
{
    private static BusinessCentralTokenProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
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
            Microsoft.Extensions.Options.Options.Create(options));
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
}
