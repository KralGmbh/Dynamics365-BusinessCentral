using Dynamics365.BusinessCentral.Errors;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

public class ObserverTests
{
    [Fact]
    public async Task Observer_Receives_Success_Events()
    {
        var observer = new TestObserver();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        }, observer);

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Contains("start:GET", observer.Events);
        Assert.Contains("success:200", observer.Events);
    }

    [Fact]
    public async Task Observer_Tracks_Token_Lifecycle()
    {
        var observer = new TestObserver();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        }, observer);

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Contains("token-requested", observer.Events);
        Assert.Contains("token-refreshed", observer.Events);
    }

    [Fact]
    public async Task Observer_Receives_Request_Failure_Event()
    {
        var observer = new TestObserver();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad")
            };
        }, observer);

        await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Contains(observer.Events, e => e.StartsWith("fail:"));
    }

    [Fact]
    public async Task Observer_Receives_DeserializationFailure_Event()
    {
        var observer = new TestObserver();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not json")
            };
        }, observer);

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Contains("deserialization-failed", observer.Events);
    }

    [Fact]
    public async Task Observer_Reports_Cached_Token_Usage()
    {
        var observer = new TestObserver();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
        }, observer);

        await client.QueryAsync<TestEntity>("orders", "true");
        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Contains("token-cached", observer.Events);
    }



}
