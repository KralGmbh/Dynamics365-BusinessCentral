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
