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
                MaxDelay = TimeSpan.FromSeconds(10),

                // This test asserts exact delays; jitter would smear them.
                JitterFactor = 0
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

    // PATCH is deliberately replayed even though RFC 9110 does not guarantee it is
    // idempotent: this client only sends absolute field values, so a replay converges.
    // Pinned because it is a behavioural contract, documented in the README retry table.
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Patch_Is_Replayed_On_Ambiguous_Transient_Failures(HttpStatusCode status)
    {
        var patches = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            patches++;
            return new HttpResponseMessage(status) { Content = new StringContent("x") };
        }));

        await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.PatchAsync("prodOrders", "id-1", new TestPatchEntity()));

        Assert.Equal(3, patches);
    }

    #region Token acquisition

    // The client_credentials grant has no side effects, so token requests are retried on
    // transient failures under the same budget as data requests — a blip at
    // login.microsoftonline.com must not fail every in-flight request at once.
    [Fact]
    public async Task Token_Request_Is_Retried_On_Transient_Failure()
    {
        var tokenCalls = 0;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;

                return tokenCalls == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("AADSTS transient")
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                    };
            }

            return TestBase.Json("{\"value\":[]}");
        });

        var result = await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Empty(result);
        Assert.Equal(2, tokenCalls);
    }

    [Fact]
    public async Task Token_Request_Is_Retried_On_Network_Failure()
    {
        var tokenCalls = 0;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;

                if (tokenCalls == 1)
                    throw new HttpRequestException("connection reset");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };
            }

            return TestBase.Json("{\"value\":[]}");
        });

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Equal(2, tokenCalls);
    }

    // Bad credentials are not transient: retrying a 400/401 from the token endpoint would
    // just hammer the identity provider with the same wrong secret.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, typeof(BusinessCentralValidationException))]
    [InlineData(HttpStatusCode.Unauthorized, typeof(BusinessCentralAuthException))]
    public async Task Token_Request_Is_Not_Retried_On_Credential_Failures(
        HttpStatusCode status, Type expectedException)
    {
        var tokenCalls = 0;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent("{\"error\":\"invalid_client\"}")
                };
            }

            return TestBase.Json("{\"value\":[]}");
        });

        var ex = await Assert.ThrowsAnyAsync<BusinessCentralException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.IsType(expectedException, ex);
        Assert.Equal(1, tokenCalls);
    }

    [Fact]
    public async Task Token_Retries_Are_Reported_To_The_Observer()
    {
        var observer = new RecordingObserver();
        var tokenCalls = 0;

        var client = TestBase.CreateClient(
            req =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                {
                    tokenCalls++;

                    return tokenCalls == 1
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent("down")
                        }
                        : new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                        };
                }

                return TestBase.Json("{\"value\":[]}");
            },
            observer);

        await client.QueryAsync<TestEntity>("orders", "true");

        var retry = Assert.Single(observer.Retries);
        Assert.Equal(503, retry.StatusCode);
        Assert.Contains("auth", retry.Url);
    }

    #endregion

    #region Network failures

    // No response at all — connection reset, DNS failure, client-side timeout — is as
    // ambiguous as a 502/504: the request may have reached the server. Idempotent methods
    // are retried; failures surface as BusinessCentralConnectionException so the
    // "everything derives from BusinessCentralException" contract holds.
    [Fact]
    public async Task Get_Is_Retried_On_Connection_Failure()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            dataCalls++;

            if (dataCalls == 1)
                throw new HttpRequestException("connection reset");

            return TestBase.Json("{\"value\":[]}");
        }));

        var result = await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Empty(result);
        Assert.Equal(2, dataCalls);
    }

    [Fact]
    public async Task Connection_Failure_Surfaces_As_ConnectionException_After_MaxAttempts()
    {
        var observer = new RecordingObserver();
        var dataCalls = 0;

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                dataCalls++;
                throw new HttpRequestException("connection reset");
            }),
            observer);

        var ex = await Assert.ThrowsAsync<BusinessCentralConnectionException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.Equal(3, dataCalls);
        Assert.True(ex.IsTransient);
        Assert.Equal(0, (int)ex.StatusCode);
        Assert.IsType<HttpRequestException>(ex.InnerException);

        Assert.Equal(2, observer.Retries.Count);
        Assert.All(observer.Retries, r => Assert.Equal(0, r.StatusCode));
    }

    [Fact]
    public async Task Client_Timeout_Is_Treated_As_Transient()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            dataCalls++;

            // What HttpClient throws when its Timeout elapses: a TaskCanceledException
            // while the caller's token is NOT cancelled.
            if (dataCalls == 1)
                throw new TaskCanceledException("timed out", new TimeoutException());

            return TestBase.Json("{\"value\":[]}");
        }));

        var result = await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Empty(result);
        Assert.Equal(2, dataCalls);
    }

    [Fact]
    public async Task Post_Is_Not_Replayed_On_Connection_Failure()
    {
        var posts = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            posts++;
            throw new HttpRequestException("connection reset");
        }));

        await Assert.ThrowsAsync<BusinessCentralConnectionException>(() =>
            client.PostAsync("ldatSummary", new TestPatchEntity()));

        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task Post_Replay_On_Connection_Failure_Can_Be_Opted_Into()
    {
        var posts = 0;

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                posts++;
                throw new HttpRequestException("connection reset");
            }),
            configure: o => o.Retry = new BusinessCentralRetryOptions
            {
                MaxAttempts = 3,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
                RetryPostOnTransientFailures = true
            });

        await Assert.ThrowsAsync<BusinessCentralConnectionException>(() =>
            client.PostAsync("ldatSummary", new TestPatchEntity()));

        Assert.Equal(3, posts);
    }

    // Cancellation requested by the caller must propagate as-is: no wrapping, no retry.
    [Fact]
    public async Task User_Cancellation_Is_Not_Wrapped_Or_Retried()
    {
        using var cts = new CancellationTokenSource();
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            dataCalls++;
            cts.Cancel();
            throw new TaskCanceledException();
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.QueryAsync<TestEntity>("orders", "true", cancellationToken: cts.Token));

        Assert.Equal(1, dataCalls);
    }

    #endregion

    #region Backoff bounds

    // A large BaseDelay with a high attempt count overflows TimeSpan, and
    // TimeSpan.FromMilliseconds throws on both overflow and Infinity — which would turn a
    // transient failure into an unrelated crash.
    [Fact]
    public async Task Huge_Backoff_Is_Clamped_Instead_Of_Overflowing()
    {
        var observer = new RecordingObserver();

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => Throttled()),
            observer,
            o => o.Retry = new BusinessCentralRetryOptions
            {
                MaxAttempts = 40,
                BaseDelay = TimeSpan.FromHours(1),
                MaxDelay = TimeSpan.Zero,
                HonorRetryAfter = false
            });

        var ex = await Record.ExceptionAsync(() => client.QueryAsync<TestEntity>("orders", "true"));

        Assert.IsType<BusinessCentralThrottledException>(ex);
        Assert.Equal(39, observer.Retries.Count);
        Assert.All(observer.Retries, r => Assert.Equal(TimeSpan.Zero, r.Delay));
    }

    [Fact]
    public async Task Negative_Delays_Are_Floored_At_Zero()
    {
        var observer = new RecordingObserver();

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => Throttled()),
            observer,
            o => o.Retry = new BusinessCentralRetryOptions
            {
                MaxAttempts = 3,
                BaseDelay = TimeSpan.FromSeconds(-5),
                MaxDelay = TimeSpan.FromSeconds(-1),
                HonorRetryAfter = false
            });

        await Assert.ThrowsAsync<BusinessCentralThrottledException>(() =>
            client.QueryAsync<TestEntity>("orders", "true"));

        Assert.All(observer.Retries, r => Assert.True(r.Delay >= TimeSpan.Zero));
    }

    #endregion

    #region Resource release

    /// <summary>Records when its content is disposed, relative to a stopwatch.</summary>
    private sealed class TrackingContent : StringContent
    {
        private readonly Action _onDispose;

        public TrackingContent(string content, Action onDispose) : base(content)
            => _onDispose = onDispose;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _onDispose();

            base.Dispose(disposing);
        }
    }

    // The failed response must be released before the backoff sleep, not after: under
    // throttling the sleep is exactly when buffered responses would pile up.
    [Fact]
    public async Task Failed_Response_Is_Released_Before_The_Backoff_Sleep()
    {
        var backoff = TimeSpan.FromMilliseconds(600);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        long? disposedAtMs = null;
        var dataCalls = 0;

        var client = TestBase.CreateClient(
            TestBase.WithToken(_ =>
            {
                dataCalls++;

                if (dataCalls > 1)
                    return TestBase.Json("{\"value\":[]}");

                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new TrackingContent(
                        "slow down",
                        () => disposedAtMs ??= clock.ElapsedMilliseconds)
                };
            }),
            configure: o => o.Retry = new BusinessCentralRetryOptions
            {
                MaxAttempts = 2,
                BaseDelay = backoff,
                MaxDelay = backoff,
                HonorRetryAfter = false
            });

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Equal(2, dataCalls);
        Assert.NotNull(disposedAtMs);

        // Disposed near the start of the window rather than after it elapsed. Generous
        // margin so this does not turn flaky on a loaded CI box.
        Assert.True(
            disposedAtMs < backoff.TotalMilliseconds * 0.75,
            $"response was released after {disposedAtMs}ms, expected before the {backoff.TotalMilliseconds}ms sleep");
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
