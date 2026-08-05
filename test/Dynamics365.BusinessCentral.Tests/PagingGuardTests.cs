using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Reflection;
using System.Net;
using Dynamics365.BusinessCentral.Errors;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Boundary cover for paging values. Paging is server-driven (nextLink continuation), so
/// a bad page size can no longer cause a non-terminating loop — these pin the boundary
/// validation and that non-positive internal values degrade to "no preference" rather
/// than reaching the server.
/// </summary>
public class PagingGuardTests
{
    /// <summary>Fails fast rather than hanging the suite if a loop stops terminating.</summary>
    private static (Dynamics365.BusinessCentral.Client.BusinessCentralClient Client, Func<int> Requests) Bounded(
        int limit = 25)
    {
        var requests = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            if (++requests > limit)
                throw new InvalidOperationException("Paging loop did not terminate.");

            return TestBase.Json("{\"value\":[]}");
        }));

        return (client, () => requests);
    }

    #region Values are rejected at the boundary

    [Fact]
    public void PageSize_Must_Be_Positive()
    {
        var (client, _) = Bounded();

        Assert.Throws<ArgumentOutOfRangeException>(() => client.Query<SalesOrder>().PageSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.Query<SalesOrder>().PageSize(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryOptions().WithPageSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryOptions().WithPageSize(-5));
    }

    [Fact]
    public void Top_And_Skip_Must_Not_Be_Negative()
    {
        var (client, _) = Bounded();

        Assert.Throws<ArgumentOutOfRangeException>(() => client.Query<SalesOrder>().Top(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.Query<SalesOrder>().Skip(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryOptions().WithTop(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryOptions().WithSkip(-1));
    }

    // $top=0 stays legal — it is how a count-only query is expressed.
    [Fact]
    public void Top_Zero_Is_Allowed()
    {
        var (client, _) = Bounded();

        var query = client.Query<SalesOrder>().Top(0);
        var options = new QueryOptions().WithTop(0);

        Assert.NotNull(query);
        Assert.Equal(0, options.Top);
    }

    #endregion

    #region Zero-row requests terminate instead of looping

    [Fact]
    public async Task Fluent_Top_Zero_Returns_Empty_Without_Paging()
    {
        var (client, requests) = Bounded();

        var result = await client.Query<SalesOrder>().Top(0).ToAllAsync();

        Assert.Empty(result);
        Assert.Equal(0, requests());
    }

    [Fact]
    public async Task Fluent_Top_Zero_Stream_Terminates()
    {
        var (client, requests) = Bounded();

        await foreach (var _ in client.Query<SalesOrder>().Top(0).StreamAsync())
            Assert.Fail("no rows were requested");

        Assert.Equal(0, requests());
    }

    [Fact]
    public async Task Path_Based_Top_Zero_Returns_Empty_Without_Paging()
    {
        var (client, requests) = Bounded();

        var result = await client.QueryAllAsync<TestEntity>("orders", options: o => o.WithTop(0));

        Assert.Empty(result);
        Assert.Equal(0, requests());
    }

    [Fact]
    public async Task Path_Based_Stream_Top_Zero_Terminates()
    {
        var (client, requests) = Bounded();

        await foreach (var _ in client.QueryStreamAsync<TestEntity>("orders", options: o => o.WithTop(0)))
            Assert.Fail("no rows were requested");

        Assert.Equal(0, requests());
    }

    // The internal setters bypass the public guards; a non-positive value must degrade
    // to "no preference" instead of sending the server odata.maxpagesize=0.
    [Fact]
    public async Task Non_Positive_Page_Size_Set_Internally_Sends_No_Preference()
    {
        string? preferHeader = null;

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            preferHeader = req.Headers.TryGetValues("Prefer", out var v) ? string.Join(",", v) : null;
            return TestBase.Json("{\"value\":[]}");
        }));

        var result = await client.QueryAllAsync<TestEntity>(
            "orders",
            options: o => typeof(QueryOptions).GetProperty(nameof(QueryOptions.PageSize))!.SetValue(o, 0));

        Assert.Empty(result);
        Assert.Null(preferHeader);
    }

    #endregion

    #region Normal paging is unaffected

    [Fact]
    public async Task Positive_Page_Size_Still_Pages()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            dataCalls++;

            return dataCalls <= 2
                ? TestBase.Json($"{{\"value\":[{{\"no\":\"a{dataCalls}\"}},{{\"no\":\"b{dataCalls}\"}}],\"@odata.nextLink\":\"https://test/p{dataCalls + 1}\"}}")
                : TestBase.Json("{\"value\":[{\"no\":\"c\"}]}");
        }));

        var all = await client.Query<SalesOrder>().PageSize(2).ToAllAsync();

        Assert.Equal(5, all.Count);
        Assert.Equal(3, dataCalls);
    }

    #endregion

    #region Continuations are only trusted as far as they advance

    /// <summary>
    /// A continuation is the one URL the client sends that it did not build, and it carries
    /// the bearer token. One pointing at another origin is not followed: doing so would
    /// disclose the token to whoever answers there and make the client fetch any host the
    /// response named.
    /// </summary>
    [Fact]
    public async Task Continuation_To_A_Foreign_Origin_Is_Refused()
    {
        var hosts = new List<string>();

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            hosts.Add(req.RequestUri!.Host);

            return TestBase.Json(
                "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://attacker.example/page2\"}");
        }));

        var ex = await Assert.ThrowsAsync<BusinessCentralProtocolException>(
            () => client.Query<SalesOrder>().ToAllAsync());

        Assert.Equal("https://attacker.example/page2", ex.RequestUrl);
        Assert.True(ex.IsProtocolViolation);
        Assert.False(ex.IsTransient);

        // The point of the guard: the foreign host was never contacted, so the token
        // never left the configured service origin.
        Assert.DoesNotContain("attacker.example", hosts);
    }

    /// <summary>
    /// The rejection message must name the origin that was compared, not <c>BaseUrl</c>. A real
    /// BaseUrl carries a path (<c>…/v2.0/{tenant}/{environment}/ODataV4</c>) that this check
    /// deliberately ignores, so printing it would send a reader comparing the one part that is
    /// allowed to differ.
    /// </summary>
    [Fact]
    public async Task Foreign_Origin_Message_Names_The_Origin_Not_The_Base_Url()
    {
        var client = TestBase.CreateClient(
            TestBase.WithToken(_ => TestBase.Json(
                "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://attacker.example/p2\"}")),
            configure: o => o.BaseUrl = "https://test/v2.0/tenant/Production/ODataV4");

        var ex = await Assert.ThrowsAsync<BusinessCentralProtocolException>(
            () => client.Query<SalesOrder>().ToAllAsync());

        Assert.Contains("https://test)", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ODataV4", ex.Message, StringComparison.Ordinal);

        // And it says which components were compared, so "but the paths differ" is not the
        // conclusion a reader reaches.
        Assert.Contains("scheme, host and port", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The non-advancing-cursor message must not send the caller after a correlation ID: this
    /// is thrown on a successful page, and the client only ever parses one out of an OData
    /// error body, so there is none to find.
    /// </summary>
    [Fact]
    public async Task Non_Advancing_Cursor_Message_Offers_Only_Actionable_Diagnostics()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ => TestBase.Json(
            "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://test/stuck\"}")));

        var ex = await Assert.ThrowsAsync<BusinessCentralProtocolException>(
            () => client.Query<SalesOrder>().ToAllAsync());

        Assert.DoesNotContain("correlation", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ex.CorrelationId);
        Assert.Equal("https://test/stuck", ex.RequestUrl);
    }

    /// <summary>
    /// Same origin, different path, is the normal shape of a continuation and stays allowed —
    /// the check compares scheme, host and port, not the path.
    /// </summary>
    [Fact]
    public async Task Continuation_On_The_Service_Origin_Is_Followed()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json(++dataCalls == 1
                ? "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://test/anything?$skiptoken=x\"}"
                : "{\"value\":[{\"no\":\"b\"}]}")));

        var all = await client.Query<SalesOrder>().ToAllAsync();

        Assert.Equal(2, all.Count);
    }

    /// <summary>
    /// A plaintext continuation off an https base is a different origin by scheme, so it is
    /// refused — that is what stops the token being talked down to http.
    /// </summary>
    [Fact]
    public async Task Continuation_Downgraded_To_Http_Is_Refused()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json("{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"http://test/p2\"}")));

        await Assert.ThrowsAsync<BusinessCentralProtocolException>(
            () => client.Query<SalesOrder>().ToAllAsync());
    }

    /// <summary>
    /// A cursor pointing at itself while still returning rows: following it replays the rows
    /// already emitted, so an uncapped stream would repeat them forever. The guard used to
    /// require an empty page, which this case never produces.
    /// </summary>
    [Fact]
    public async Task Continuation_Cursor_That_Never_Advances_Throws()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            if (++dataCalls > 25)
                throw new InvalidOperationException("Paging loop did not terminate.");

            return TestBase.Json(
                "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://test/stuck\"}");
        }));

        var ex = await Assert.ThrowsAsync<BusinessCentralProtocolException>(
            () => client.Query<SalesOrder>().ToAllAsync());

        Assert.Equal("https://test/stuck", ex.RequestUrl);

        // First page, then the one fetch of /stuck; its repeat of the same cursor throws.
        Assert.Equal(2, dataCalls);
    }

    /// <summary>A → B → A. Only the full visited set catches a cycle longer than one.</summary>
    [Fact]
    public async Task Cycling_Continuation_Cursors_Throw()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            if (++dataCalls > 25)
                throw new InvalidOperationException("Paging loop did not terminate.");

            // p1 -> p2 -> p1 -> ...
            return TestBase.Json(dataCalls % 2 == 1
                ? "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://test/p2\"}"
                : "{\"value\":[{\"no\":\"b\"}],\"@odata.nextLink\":\"https://test/p1\"}");
        }));

        await Assert.ThrowsAsync<BusinessCentralProtocolException>(
            () => client.Query<SalesOrder>().ToAllAsync());
    }

    /// <summary>
    /// A nextLink asserts that continuation remains. Even when the repeated page is empty,
    /// stopping quietly would claim the result is complete without evidence; the repeated
    /// cursor is therefore the same protocol violation as one carrying rows.
    /// </summary>
    [Fact]
    public async Task Empty_Page_Repeating_Its_Cursor_Throws()
    {
        var dataCalls = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            if (++dataCalls > 25)
                throw new InvalidOperationException("Paging loop did not terminate.");

            return TestBase.Json(dataCalls == 1
                ? "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://test/stuck\"}"
                : "{\"value\":[],\"@odata.nextLink\":\"https://test/stuck\"}");
        }));

        await Assert.ThrowsAsync<BusinessCentralProtocolException>(
            () => client.Query<SalesOrder>().ToAllAsync());

        Assert.Equal(2, dataCalls);
    }

    /// <summary>The path-based surface shares QueryPager, so it inherits both guards.</summary>
    [Fact]
    public async Task Path_Based_Stream_Refuses_A_Foreign_Continuation()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json(
                "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://attacker.example/p2\"}")));

        await Assert.ThrowsAsync<BusinessCentralProtocolException>(async () =>
        {
            await foreach (var _ in client.QueryStreamAsync<SalesOrder>("salesOrders"))
            {
            }
        });
    }

    #endregion

    #region Token lifetime and user agent

    [Theory]
    [InlineData(3600, 3540)]   // normal: full 60s margin
    [InlineData(120, 60)]      // margin still fits
    [InlineData(60, 30)]       // margin equals lifetime -> half
    [InlineData(30, 15)]       // margin exceeds lifetime -> half
    [InlineData(1, 0)]         // sub-second lifetimes floor to zero, never negative
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void Token_Cache_Lifetime_Is_Never_Negative(int expiresIn, int expectedSeconds)
    {
        var lifetime = BusinessCentralTokenProvider.CacheLifetime(expiresIn);

        Assert.True(lifetime >= TimeSpan.Zero, "cache lifetime must never be negative");
        Assert.Equal(expectedSeconds, (int)lifetime.TotalSeconds);
    }

    [Fact]
    public async Task Short_Lived_Token_Is_Still_Cached()
    {
        var tokenCalls = 0;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;
                // 30s lifetime is shorter than the 60s margin; the token must not be
                // treated as expired on arrival.
                return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":30}");
            }

            return TestBase.Json("{\"value\":[]}");
        });

        await client.QueryAsync<TestEntity>("orders", "true");
        await client.QueryAsync<TestEntity>("orders", "true");
        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Equal(1, tokenCalls);
    }

    [Fact]
    public async Task User_Agent_Reports_The_Assembly_Version()
    {
        string? userAgent = null;

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            userAgent = req.Headers.UserAgent.ToString();
            return TestBase.Json("{\"value\":[]}");
        }));

        await client.QueryAsync<TestEntity>("orders", "true");

        var expected = typeof(IBusinessCentralClient).Assembly.GetName().Version!.ToString(3);

        Assert.NotNull(userAgent);
        Assert.Contains($"Dynamics365.BusinessCentral.Client/{expected}", userAgent);

        // Guards against the version drifting out of the string again.
        Assert.StartsWith("2.", expected);
    }

    [Fact]
    public async Task Token_Response_Is_Disposed_After_Acquisition()
    {
        var disposed = false;

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new DisposeTrackingContent(
                        "{\"access_token\":\"abc\",\"expires_in\":3600}",
                        () => disposed = true)
                };

            return TestBase.Json("{\"value\":[]}");
        });

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.True(disposed, "the token response should be released once the token is read");
    }

    [Fact]
    public async Task Delete_Error_Message_States_The_Real_Contract()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("?") }));

        var ex = await Assert.ThrowsAsync<BusinessCentralServerException>(() =>
            client.DeleteAsync("orders", "1"));

        // 200 is accepted too, so the message must not claim 204 is the only success.
        Assert.Contains("200", ex.Message);
        Assert.Contains("204", ex.Message);
        Assert.Contains("202", ex.Message);
    }

    private sealed class DisposeTrackingContent : StringContent
    {
        private readonly Action _onDispose;

        public DisposeTrackingContent(string content, Action onDispose) : base(content)
            => _onDispose = onDispose;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _onDispose();

            base.Dispose(disposing);
        }
    }

    #endregion
}
