using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;

namespace Dynamics365.BusinessCentral.Tests;

public abstract class TestBase
{
    public static BusinessCentralClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        IBusinessCentralObserver? observer = null)
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
            TokenEndpoint = "https://auth/{TenantId}"
        };

        return new BusinessCentralClient(http, options, observer);
    }
}
