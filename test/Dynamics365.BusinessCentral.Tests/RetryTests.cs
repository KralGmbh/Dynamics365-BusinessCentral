using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

public class RetryTests
{
    private static HttpResponseMessage Throttled(TimeSpan? retryAfter = null)
    {
        var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":{\"code\":\"TooManyRequests\",\"message\":\"slow down\"}}")
        };

        if (retryAfter is { } delay)
            res.Headers.Add("Retry-After", ((int)delay.TotalSeconds).ToString());

        return res;
    }

    [Fact]
    public async Task Throttled_Request_Is_Retried_And_Succeeds()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            dataCalls++;
            return dataCalls == 1 ? Throttled() : TestBase.Json("{\"value\":[]}");
        }));

        var result = await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Empty(result);
        Assert.Equal(2, dataCalls);
    }

    [Fact]
    public async Task Retry_Gives_Up_After_MaxAttempts_And_Throws_Throttled()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                dataCalls++;
                return Throttled();
            }),
            configure: o => o.Retry = new BusinessCentralRetryOptions
            {
                MaxAttempts = 3,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            });

        var ex = await Assert.ThrowsAsync<BusinessCentralThrottledException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Equal(3, dataCalls);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task Retry_Can_Be_Disabled()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                dataCalls++;
                return Throttled();
            }),
            configure: o => o.Retry = new BusinessCentralRetryOptions { Enabled = false });

        await Assert.ThrowsAsync<BusinessCentralThrottledException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Equal(1, dataCalls);
    }

    [Fact]
    public async Task Validation_Errors_Are_Not_Retried()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            dataCalls++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("nope")
            };
        }));

        var ex = await Assert.ThrowsAsync<BusinessCentralValidationException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Equal(1, dataCalls);
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public async Task ServiceUnavailable_Is_Transient_But_NotFound_Is_Not()
    {
        var client503 = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") }));

        var ex503 = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client503.QueryAsync<TestEntity>("orders", "true"));

        Assert.True(ex503.IsTransient);

        var client404 = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("gone") }));

        var ex404 = await Assert.ThrowsAsync<BusinessCentralNotFoundException>(() =>
            client404.QueryAsync<TestEntity>("orders", "true"));

        Assert.False(ex404.IsTransient);
    }

    [Fact]
    public async Task RetryAfter_Header_Is_Surfaced_On_The_Exception()
    {
        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => Throttled(TimeSpan.FromSeconds(7))),
            configure: o => o.Retry = new BusinessCentralRetryOptions { Enabled = false });

        var ex = await Assert.ThrowsAsync<BusinessCentralThrottledException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
    }

    [Fact]
    public async Task Observer_Sees_Retry_With_Server_Requested_Delay()
    {
        var observer = new RecordingObserver();
        var dataCalls = 0;

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                dataCalls++;
                return dataCalls == 1 ? Throttled(TimeSpan.FromSeconds(5)) : TestBase.Json("{\"value\":[]}");
            }),
            observer);

        await client.QueryAsync<TestEntity>("orders", "true");

        var retry = Assert.Single(observer.Retries);

        Assert.Equal(429, retry.StatusCode);
        Assert.Equal(1, retry.Attempt);
        Assert.True(retry.FromRetryAfter);

        // MaxDelay is zero in tests, so the 5s request is capped rather than slept off.
        Assert.Equal(TimeSpan.Zero, retry.Delay);
    }

    [Fact]
    public async Task Backoff_Doubles_When_No_RetryAfter_Header()
    {
        var observer = new RecordingObserver();

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => Throttled()),
            observer,
            o => o.Retry = new BusinessCentralRetryOptions
            {
                MaxAttempts = 4,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromSeconds(10)
            });

        await Assert.ThrowsAsync<BusinessCentralThrottledException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Equal(
            [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(4)],
            observer.Retries.Select(r => r.Delay));

        Assert.All(observer.Retries, r => Assert.False(r.FromRetryAfter));
    }

    #region Replay safety

    // 429 is rejected before processing, so replaying it can never duplicate a write.
    [Fact]
    public async Task Post_Is_Retried_On_429()
    {
        var posts = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            posts++;
            return posts == 1 ? Throttled() : TestBase.Json("{\"id\":\"1\",\"name\":\"x\"}");
        }));

        await client.PostAsync("ldatSummary", new TestPatchEntity { Id = "1", Name = "x" });

        Assert.Equal(2, posts);
    }

    // 408/502/503/504 are ambiguous — the row may already exist. Replaying a POST would
    // orphan a duplicate, so it must surface instead.
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Post_Is_Not_Replayed_On_Ambiguous_Transient_Failures(HttpStatusCode status)
    {
        var posts = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            posts++;
            return new HttpResponseMessage(status) { Content = new StringContent("x") };
        }));

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.PostAsync("ldatSummary", new TestPatchEntity()));

        Assert.Equal(1, posts);

        // Still reported as transient so the caller can decide for itself.
        Assert.True(ex.IsTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Idempotent_Methods_Are_Still_Replayed(HttpStatusCode status)
    {
        var gets = 0;
        var puts = 0;
        var deletes = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            if (req.Method == HttpMethod.Get) gets++;
            if (req.Method == HttpMethod.Put) puts++;
            if (req.Method == HttpMethod.Delete) deletes++;

            return new HttpResponseMessage(status) { Content = new StringContent("x") };
        }));

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.PutAsync("orders", "1", new TestPatchEntity()));

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.DeleteAsync("orders", "1"));

        Assert.Equal(3, gets);
        Assert.Equal(3, puts);
        Assert.Equal(3, deletes);
    }

    [Fact]
    public async Task Post_Replay_Can_Be_Opted_Into()
    {
        var posts = 0;

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                posts++;
                return new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
                {
                    Content = new StringContent("x")
                };
            }),
            configure: o => o.Retry = new BusinessCentralRetryOptions
            {
                MaxAttempts = 3,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                RetryPostOnTransientFailures = true
            });

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.PostAsync("ldatSummary", new TestPatchEntity()));

        Assert.Equal(3, posts);
    }

    [Fact]
    public async Task Post_Not_Replayed_Reports_A_Single_Failure_And_No_Retry_Event()
    {
        var observer = new RecordingObserver();

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
            {
                Content = new StringContent("x")
            }),
            observer);

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.PostAsync("ldatSummary", new TestPatchEntity()));

        Assert.Empty(observer.Retries);
    }

    #endregion

    private sealed class RecordingObserver : IBusinessCentralObserver
    {
        public readonly List<BusinessCentralRetryInfo> Retries = [];

        public void OnRequestStarting(BusinessCentralRequestInfo request) { }
        public void OnRequestSucceeded(BusinessCentralRequestInfo request) { }
        public void OnRequestFailed(BusinessCentralErrorInfo error) { }
        public void OnTokenRequested() { }
        public void OnTokenRefreshed(BusinessCentralTokenInfo token) { }
        public void OnDeserializationFailed(BusinessCentralErrorInfo error) { }

        public void OnRequestRetrying(BusinessCentralRetryInfo retry) => Retries.Add(retry);
    }
}
