using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.Tests.Utils;
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

    /// <summary>Throws from every callback — diagnostics must never break the pipeline.</summary>
    private sealed class ThrowingObserver : IBusinessCentralObserver
    {
        public void OnRequestStarting(BusinessCentralRequestInfo request) => throw new InvalidOperationException("observer bug");
        public void OnRequestSucceeded(BusinessCentralRequestInfo request) => throw new InvalidOperationException("observer bug");
        public void OnRequestFailed(BusinessCentralErrorInfo error) => throw new InvalidOperationException("observer bug");
        public void OnRequestRetrying(BusinessCentralRetryInfo retry) => throw new InvalidOperationException("observer bug");
        public void OnDeserializationFailed(BusinessCentralErrorInfo error) => throw new InvalidOperationException("observer bug");
        public void OnTokenRequested() => throw new InvalidOperationException("observer bug");
        public void OnTokenRefreshed(BusinessCentralTokenInfo token) => throw new InvalidOperationException("observer bug");
        public void OnTokenServedFromCache(BusinessCentralTokenInfo token) => throw new InvalidOperationException("observer bug");
    }

    // Observers are best-effort diagnostics: a bug in one must not turn a successful
    // request into a failure, and a real server failure must surface as the
    // BusinessCentralException — not as the observer's own exception.
    [Fact]
    public async Task Throwing_Observer_Does_Not_Break_Successful_Requests()
    {
        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => TestBase.Json("{\"value\":[{\"id\":1}]}")),
            new ThrowingObserver());

        var result = await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Single(result);
    }

    [Fact]
    public async Task Throwing_Observer_Does_Not_Mask_The_Real_Failure()
    {
        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("nope")
            }),
            new ThrowingObserver());

        await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));
    }

    // A cancellation the caller asked for is not a failure and must not be reported as one.
    [Fact]
    public async Task Caller_Cancellation_Is_Not_Reported_As_A_Failure()
    {
        var observer = new TestObserver();
        using var cts = new CancellationTokenSource();

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                cts.Cancel();
                throw new TaskCanceledException();
            }),
            observer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.QueryAsync<TestEntity>("orders", "true", cancellationToken: cts.Token));

        Assert.Empty(observer.Failures);
    }
}
