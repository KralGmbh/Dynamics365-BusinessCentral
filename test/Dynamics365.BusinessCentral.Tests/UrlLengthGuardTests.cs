using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Tests.Utils;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins the URL-length guard (N4 from the pre-stable review, sharpened by L2 in the
/// live-tenant round): a soft threshold that reports and a hard limit that refuses, with the
/// gap between them as the measurement window.
/// </summary>
public class UrlLengthGuardTests : TestBase
{
    /// <summary>Enough keys to blow past any sane limit once or-chained and encoded.</summary>
    private static object[] ManyKeys(int count) =>
        [.. Enumerable.Range(0, count).Select(i => (object)$"EBH{i:D5}")];

    private static Func<HttpRequestMessage, HttpResponseMessage> AlwaysEmpty() =>
        WithToken(_ => Json("""{"value":[]}"""));

    #region Hard limit

    [Fact]
    public async Task Url_Past_MaxUrlLength_Throws_Before_Sending()
    {
        var dataRequests = 0;

        var client = CreateClient(
            WithToken(_ =>
            {
                dataRequests++;
                return Json("""{"value":[]}""");
            }),
            configure: o => o.MaxUrlLength = 500);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.Query<SalesOrder>()
                .Where(f => f.In(x => x.No, ManyKeys(60)))
                .ToListAsync());

        Assert.Contains("MaxUrlLength", ex.Message, StringComparison.Ordinal);
        Assert.Contains("500", ex.Message, StringComparison.Ordinal);

        // The URL is built before the token is acquired, so nothing at all left the process.
        Assert.Equal(0, dataRequests);
    }

    [Fact]
    public async Task Too_Long_Message_Names_The_Actual_Length()
    {
        var client = CreateClient(AlwaysEmpty(), configure: o => o.MaxUrlLength = 400);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.Query<SalesOrder>()
                .Where(f => f.In(x => x.No, ManyKeys(60)))
                .ToListAsync());

        // The length is rendered with a thousands separator, so match the digits either way.
        Assert.Matches(@"produced a [\d,]+-character URL", ex.Message);
    }

    /// <summary>L2: the or-chain is the dominant cause and its cost is the least obvious.</summary>
    [Fact]
    public async Task Or_Chained_Filter_Message_Blames_Filter_In()
    {
        var client = CreateClient(AlwaysEmpty(), configure: o => o.MaxUrlLength = 400);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.Query<SalesOrder>()
                .Where(f => f.In(x => x.No, ManyKeys(60)))
                .ToListAsync());

        Assert.Contains("'or' clauses", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Filter.In", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Chunk the values", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A long URL with no or-chain gets the length message without the false lead.</summary>
    [Fact]
    public async Task Non_Or_Chained_Filter_Message_Omits_The_In_Advice()
    {
        var client = CreateClient(AlwaysEmpty(), configure: o => o.MaxUrlLength = 200);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.Query<SalesOrder>()
                .Where(f => f.Equals(x => x.No, new string('X', 300)))
                .ToListAsync());

        Assert.Contains("-character URL", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Filter.In", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Null_MaxUrlLength_Disables_The_Limit()
    {
        string? seen = null;

        var client = CreateClient(
            WithToken(req =>
            {
                seen = req.RequestUri!.AbsoluteUri;
                return Json("""{"value":[]}""");
            }),
            configure: o =>
            {
                o.MaxUrlLength = null;
                o.UrlLengthWarningThreshold = null;
            });

        await client.Query<SalesOrder>()
            .Where(f => f.In(x => x.No, ManyKeys(60)))
            .ToListAsync();

        Assert.NotNull(seen);
        Assert.True(seen!.Length > 2000, $"expected a long URL, got {seen.Length} characters");
    }

    #endregion

    #region Warning threshold

    [Fact]
    public async Task Url_Between_Threshold_And_Limit_Warns_And_Is_Still_Sent()
    {
        var observer = new TestObserver();
        var sentData = false;

        var client = CreateClient(
            WithToken(_ =>
            {
                sentData = true;
                return Json("""{"value":[]}""");
            }),
            observer,
            o =>
            {
                o.UrlLengthWarningThreshold = 300;
                o.MaxUrlLength = 100_000;
            });

        await client.Query<SalesOrder>()
            .Where(f => f.In(x => x.No, ManyKeys(20)))
            .ToListAsync();

        Assert.True(sentData, "the request should still have been sent");

        var warning = Assert.Single(observer.UrlWarnings);
        Assert.False(warning.ExceedsLimit);
        Assert.Equal(300, warning.Threshold);
        Assert.Equal(100_000, warning.Limit);
        Assert.True(warning.Length >= 300);
        Assert.Equal(warning.Length, warning.Url.Length);
        Assert.True(warning.OrClauseCount >= 19, $"expected an or-chain, counted {warning.OrClauseCount}");
    }

    /// <summary>
    /// The rejected outliers are the most interesting data points, so the warning fires
    /// before the throw rather than instead of it.
    /// </summary>
    [Fact]
    public async Task Url_Past_The_Limit_Warns_As_Well_As_Throwing()
    {
        var observer = new TestObserver();

        var client = CreateClient(
            AlwaysEmpty(),
            observer,
            o =>
            {
                o.UrlLengthWarningThreshold = 300;
                o.MaxUrlLength = 500;
            });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.Query<SalesOrder>()
                .Where(f => f.In(x => x.No, ManyKeys(60)))
                .ToListAsync());

        var warning = Assert.Single(observer.UrlWarnings);
        Assert.True(warning.ExceedsLimit);
        Assert.Equal(500, warning.Limit);
    }

    [Fact]
    public async Task Short_Url_Raises_No_Warning()
    {
        var observer = new TestObserver();

        var client = CreateClient(AlwaysEmpty(), observer);

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Empty(observer.UrlWarnings);
    }

    [Fact]
    public async Task Null_Threshold_Disables_The_Warning_But_Not_The_Limit()
    {
        var observer = new TestObserver();

        var client = CreateClient(
            AlwaysEmpty(),
            observer,
            o =>
            {
                o.UrlLengthWarningThreshold = null;
                o.MaxUrlLength = 500;
            });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.Query<SalesOrder>()
                .Where(f => f.In(x => x.No, ManyKeys(60)))
                .ToListAsync());

        Assert.Empty(observer.UrlWarnings);
    }

    /// <summary>A throwing observer must not become the failure the caller sees.</summary>
    [Fact]
    public async Task Throwing_Observer_Does_Not_Break_The_Request()
    {
        var client = CreateClient(
            AlwaysEmpty(),
            new ThrowingUrlObserver(),
            o => o.UrlLengthWarningThreshold = 100);

        var rows = await client.Query<SalesOrder>()
            .Where(f => f.In(x => x.No, ManyKeys(20)))
            .ToListAsync();

        Assert.Empty(rows);
    }

    private sealed class ThrowingUrlObserver : Diagnostics.IBusinessCentralObserver
    {
        public void OnRequestStarting(Diagnostics.BusinessCentralRequestInfo request) { }
        public void OnRequestSucceeded(Diagnostics.BusinessCentralRequestInfo request) { }
        public void OnRequestFailed(Diagnostics.BusinessCentralErrorInfo error) { }
        public void OnTokenRequested() { }
        public void OnTokenRefreshed(Diagnostics.BusinessCentralTokenInfo token) { }
        public void OnDeserializationFailed(Diagnostics.BusinessCentralErrorInfo error) { }

        public void OnUrlLengthWarning(Diagnostics.BusinessCentralUrlLengthInfo url) =>
            throw new InvalidOperationException("observer is broken");
    }

    #endregion

    #region Server-issued continuations

    /// <summary>
    /// A <c>@odata.nextLink</c> is the server's own URL: it already passed whatever limits
    /// the deployment enforces, and rejecting it client-side would strand a paged read
    /// halfway through. Continuations bypass the builder entirely.
    /// </summary>
    [Fact]
    public async Task NextLink_Longer_Than_The_Limit_Is_Still_Followed()
    {
        var longCursor = new string('t', 600);
        var page = 0;

        var client = CreateClient(
            WithToken(_ =>
            {
                page++;

                return page == 1
                    ? Json($$"""
                        {"value":[{"No":"A"}],
                         "@odata.nextLink":"https://test/Company('Test')/salesOrders?$skiptoken={{longCursor}}"}
                        """)
                    : Json("""{"value":[{"No":"B"}]}""");
            }),
            configure: o => o.MaxUrlLength = 400);

        var all = await client.Query<SalesOrder>().ToAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(2, page);
    }

    #endregion

    #region Path-based API

    /// <summary>The guard sits in the URL builder, so it covers the path-based surface too.</summary>
    [Fact]
    public async Task Path_Based_Query_Is_Guarded_Too()
    {
        var client = CreateClient(AlwaysEmpty(), configure: o => o.MaxUrlLength = 300);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.QueryAsync<SalesOrder>(
                "salesOrders",
                Filter.In<SalesOrder>(x => x.No, ManyKeys(60))));
    }

    #endregion
}
