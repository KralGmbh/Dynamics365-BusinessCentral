using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins what actually reaches the wire for the parts of the OData surface that were only ever
/// asserted through <c>ODataFilter.Value</c> or through the fake's return value — the gap that
/// let <c>$filter=false</c> and <c>(true) and (…)</c> ship.
/// </summary>
/// <remarks>
/// Every test here reads the request URL the handler received. A test that inspects a filter's
/// <c>Value</c> proves nothing about the request: the two legitimately differ for a deferred
/// <c>Filter.In</c>, and they differed for the boolean constants until this round.
/// </remarks>
public class ODataWireContractTests : TestBase
{
    /// <summary>Captures every non-token request URL the client sends.</summary>
    private static (Func<HttpRequestMessage, HttpResponseMessage> Handler, List<string> Urls) Recorder(
        string body = """{"value":[]}""")
    {
        var urls = new List<string>();

        return (WithToken(req =>
        {
            urls.Add(Uri.UnescapeDataString(req.RequestUri!.AbsoluteUri));
            return Json(body);
        }), urls);
    }

    #region Boolean-literal filters never reach the wire

    /// <summary>
    /// The case that produces this in practice: a key set that came back empty upstream.
    /// Business Central has no boolean-literal filter, so the empty result is answered here.
    /// </summary>
    [Fact]
    public async Task Empty_Filter_In_Sends_No_Request_At_All()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        var rows = await client.Query<SalesOrder>()
            .Where(f => f.In(x => x.No, Array.Empty<object>()))
            .ToListAsync();

        Assert.Empty(rows);
        Assert.Empty(urls);
    }

    [Fact]
    public async Task Filter_None_Sends_No_Request_On_The_Path_Based_Api()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        var rows = await client.QueryAsync<TestEntity>("items", Filter.None);

        Assert.Empty(rows);
        Assert.Empty(urls);
    }

    [Fact]
    public async Task Filter_None_Answers_Count_As_Zero_Without_A_Request()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        var count = await client.Query<SalesOrder>()
            .Where(f => f.In(x => x.No, Array.Empty<object>()))
            .CountAsync();

        Assert.Equal(0, count);

        // Notably it must not fall through to the walk-the-collection fallback, which would
        // page the whole entity set to arrive at the same zero.
        Assert.Empty(urls);
    }

    [Fact]
    public async Task Filter_None_Answers_ToPageAsync_With_An_Empty_Page()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        var page = await client.Query<SalesOrder>()
            .Where(Filter.None)
            .ToPageAsync();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Null(page.NextLink);
        Assert.False(page.HasMore);
        Assert.Empty(urls);
    }

    [Fact]
    public async Task Filter_None_Streams_Nothing_Without_A_Request()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        var seen = 0;

        await foreach (var _ in client.Query<SalesOrder>().Where(Filter.None).StreamAsync())
            seen++;

        Assert.Equal(0, seen);
        Assert.Empty(urls);
    }

    /// <summary>
    /// <c>Filter.All</c> alone was always omitted; composed, it used to be parenthesised into
    /// the expression as <c>(true) and (…)</c>, which is not a filter Business Central accepts.
    /// </summary>
    [Fact]
    public async Task Composed_Filter_All_Does_Not_Emit_A_Boolean_Literal()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        await client.Query<SalesOrder>()
            .Where(Filter.All)
            .Where(Filter.Equals("status", "Open"))
            .ToListAsync();

        var url = Assert.Single(urls);

        Assert.Contains("$filter=status eq 'Open'", url, StringComparison.Ordinal);
        Assert.DoesNotContain("true", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_Reduces_The_Constants_Away()
    {
        var real = Filter.Equals("status", "Open");

        Assert.Equal("status eq 'Open'", Filter.All.And(real).Value);
        Assert.Equal("status eq 'Open'", real.And(Filter.All).Value);
        Assert.Equal("status eq 'Open'", Filter.None.Or(real).Value);
        Assert.Equal("status eq 'Open'", real.Or(Filter.None).Value);

        Assert.Equal(ODataFilter.MatchNone, Filter.None.And(real).Value);
        Assert.Equal(ODataFilter.MatchAll, Filter.All.Or(real).Value);
    }

    [Fact]
    public void Negating_A_Constant_Yields_The_Other_Constant()
    {
        Assert.Equal(ODataFilter.MatchNone, Filter.All.Not().Value);
        Assert.Equal(ODataFilter.MatchAll, Filter.None.Not().Value);
    }

    /// <summary>
    /// Dropping a constant must not cost a composed <c>Filter.In</c> its deferred rendering —
    /// that deferral is the whole point of composing being safe.
    /// </summary>
    [Fact]
    public async Task Reducing_Filter_All_Preserves_A_Deferred_In()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler, configure: o => o.SchemaVersion = "2.1");

        await client.Query<SalesOrder>()
            .Where(Filter.All.And(Filter.In("no", "A", "B")))
            .ToListAsync();

        var url = Assert.Single(urls);

        Assert.Contains("no in ('A','B')", url, StringComparison.Ordinal);
        Assert.DoesNotContain("true", url, StringComparison.Ordinal);
    }

    #endregion

    #region Projection, expand and ordering

    /// <summary>
    /// Since the projection is derived by default, <c>$select</c> + <c>$expand</c> is the
    /// ordinary shape of every expanded query — not an exotic one. Nothing asserted it.
    /// </summary>
    [Fact]
    public async Task Expand_Is_Sent_Alongside_The_Derived_Select()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        await client.Query<SalesOrder>()
            .Expand(o => o.Lines)
            .ToListAsync();

        var url = Assert.Single(urls);

        Assert.Contains("$select=", url, StringComparison.Ordinal);
        Assert.Contains("$expand=lines", url, StringComparison.Ordinal);

        // The derived projection covers the scalar columns only; the navigation property is
        // carried by $expand, not by $select.
        Assert.DoesNotContain("$select=lines", url, StringComparison.Ordinal);
        Assert.DoesNotContain(",lines", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nested_Navigation_Path_Is_Sent_In_OrderBy()
    {
        var (handler, urls) = Recorder();
        var client = CreateClient(handler);

        await client.Query<SalesOrder>()
            .OrderBy(o => o.Customer!.Name)
            .ThenByDescending(o => o.No)
            .ToListAsync();

        var url = Assert.Single(urls);

        Assert.Contains("$orderby=customer/name asc,no desc", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Count_Is_Sent_On_The_Path_Based_Api()
    {
        var (handler, urls) = Recorder("""{"value":[],"@odata.count":42}""");
        var client = CreateClient(handler);

        await client.QueryAsync<TestEntity>("items", options: o => o.WithCount());

        Assert.Contains("$count=true", Assert.Single(urls), StringComparison.Ordinal);
    }

    #endregion

    #region EntitySelect derivation

    private sealed class IncludedSetterEntity
    {
        public string No { get; set; } = string.Empty;

        /// <summary>
        /// System.Text.Json populates this because of [JsonInclude], so it is a real column —
        /// excluding it from $select would leave it empty on every row with a 200 response.
        /// </summary>
        [JsonInclude]
        public string Description { get; private set; } = string.Empty;

        /// <summary>No [JsonInclude], so STJ cannot fill it: correctly not a column.</summary>
        public string Ignored { get; private set; } = string.Empty;

        /// <summary>Get-only computed: cannot receive data.</summary>
        public string Computed => No + Description;
    }

    [Fact]
    public void JsonInclude_On_A_Private_Setter_Is_A_Column()
    {
        var derived = EntitySelect.For<IncludedSetterEntity>();

        Assert.Contains("description", derived);
        Assert.Contains("no", derived);

        Assert.DoesNotContain("ignored", derived);
        Assert.DoesNotContain("computed", derived);
    }

    private sealed class DuplicateNameEntity
    {
        public string No { get; set; } = string.Empty;

        [JsonPropertyName("no")]
        public string Number { get; set; } = string.Empty;
    }

    /// <summary>
    /// Explicit <c>Select(...)</c> de-duplicates, so the derived projection must too — else the
    /// same query emits <c>$select=no,no</c> one way and <c>no</c> the other.
    /// </summary>
    [Fact]
    public void Two_Properties_Resolving_To_One_Wire_Name_Are_Emitted_Once()
    {
        Assert.Equal(["no"], EntitySelect.For<DuplicateNameEntity>());
    }

    #endregion

    #region Paging

    /// <summary>
    /// A server that hands back the cursor just followed, on a page with no rows, cannot be
    /// advanced by following it again — so the pager stops instead of spinning forever.
    /// </summary>
    [Fact]
    public async Task Pager_Stops_When_The_Cursor_Stops_Advancing()
    {
        var requests = 0;

        var client = CreateClient(WithToken(_ =>
        {
            requests++;

            // First page carries a row; every page after it is empty but repeats the cursor.
            return Json(requests == 1
                ? """{"value":[{"no":"A"}],"@odata.nextLink":"https://test/next"}"""
                : """{"value":[],"@odata.nextLink":"https://test/next"}""");
        }));

        var rows = await client.Query<SalesOrder>().ToAllAsync();

        Assert.Single(rows);

        // First page, then one continuation that made no progress. Without the guard this
        // never returns.
        Assert.Equal(2, requests);
    }

    /// <summary>An empty page with a *new* cursor is legitimate and must keep paging.</summary>
    [Fact]
    public async Task Pager_Follows_An_Empty_Page_That_Advances_The_Cursor()
    {
        var requests = 0;

        var client = CreateClient(WithToken(_ =>
        {
            requests++;

            return Json(requests switch
            {
                1 => """{"value":[{"no":"A"}],"@odata.nextLink":"https://test/p2"}""",
                2 => """{"value":[],"@odata.nextLink":"https://test/p3"}""",
                _ => """{"value":[{"no":"B"}]}"""
            });
        }));

        var rows = await client.Query<SalesOrder>().ToAllAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, requests);
    }

    #endregion

    #region Headers

    /// <summary>
    /// QueryRawAsync builds its URL through a different path than every other read, and was the
    /// one GET not covered by the Data-Access-Intent tests.
    /// </summary>
    [Fact]
    public async Task Data_Access_Intent_Is_Sent_On_QueryRawAsync()
    {
        string? intent = null;

        var client = CreateClient(
            WithToken(req =>
            {
                intent = req.Headers.TryGetValues("Data-Access-Intent", out var v)
                    ? string.Join(",", v)
                    : null;

                return Json("""{"value":[]}""");
            }),
            configure: o => o.DataAccessIntent = BusinessCentralDataAccessIntent.ReadOnly);

        await client.QueryRawAsync<TestRawResponse>("salesOrders?$top=5");

        Assert.Equal("ReadOnly", intent);
    }

    #endregion
}
