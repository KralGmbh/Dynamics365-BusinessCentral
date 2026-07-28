using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;

namespace Dynamics365.BusinessCentral.Tests;

public abstract class TestBase
{
    public static BusinessCentralClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        IBusinessCentralObserver? observer = null,
        Action<BusinessCentralOptions>? configure = null)
    {
        var http = new HttpClient(new FakeHttpHandler(handler));

        var options = new BusinessCentralOptions
        {
            BaseUrl = "https://test",
            Company = "Test",
            TenantId = "tenant",
            ClientId = "client",
            ClientSecret = "secret",
            Scope = "scope",
            TokenEndpoint = "https://auth/{TenantId}",

            // Keep retries instant so tests do not sleep.
            Retry = new BusinessCentralRetryOptions
            {
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            }
        };

        configure?.Invoke(options);

        return new BusinessCentralClient(http, Microsoft.Extensions.Options.Options.Create(options), observer);
    }

    /// <summary>
    /// Handler that answers the token request and delegates everything else, so tests do
    /// not repeat the auth branch.
    /// </summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> WithToken(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        => req => req.RequestUri!.AbsoluteUri.Contains("auth")
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
            }
            : handler(req);

    /// <summary>Shorthand for a JSON 200 response.</summary>
    public static HttpResponseMessage Json(string body) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };
}
