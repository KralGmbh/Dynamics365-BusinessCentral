using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

public class QueryBuilderTests
{
    private static (Dynamics365.BusinessCentral.Client.BusinessCentralClient Client, List<string> Urls) Capturing(
        params string[] bodies)
    {
        var urls = new List<string>();
        var call = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            urls.Add(req.RequestUri!.AbsoluteUri);

            var body = bodies.Length == 0
                ? "{\"value\":[]}"
                : bodies[Math.Min(call, bodies.Length - 1)];

            call++;

            return TestBase.Json(body);
        }));

        return (client, urls);
    }

    private static string Query(string url) => new Uri(url).Query;

    private static string Decode(string url) => Uri.UnescapeDataString(url);

    #region Path inference

    [Fact]
    public async Task Path_Comes_From_Entity_Attribute()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Contains("Company('Test')/salesOrders", urls[0]);
    }

    [Fact]
    public void Unannotated_Entity_Gives_An_Actionable_Error()
    {
        var (client, _) = Capturing();

        var ex = Assert.Throws<InvalidOperationException>(() => client.Query<UnannotatedEntity>());

        Assert.Contains("BusinessCentralEntity", ex.Message);
        Assert.Contains("UnannotatedEntity", ex.Message);
    }

    [Fact]
    public async Task Explicit_Path_Overrides_Inference()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>("customPage").ToListAsync();

        Assert.Contains("Company('Test')/customPage", urls[0]);
    }

    #endregion

    #region Typed field names

    [Fact]
    public async Task Selectors_Use_The_Json_Naming_Policy()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>()
            .Where(Filter.Equals<SalesOrder>(o => o.Status, "Open"))
            .Select(o => o.No, o => o.Amount)
            .ToListAsync();

        var decoded = Decode(urls[0]);

        // camelCase, matching how the entity is deserialized.
        Assert.Contains("$filter=status eq 'Open'", decoded);
        Assert.Contains("$select=no,amount", decoded);
    }

    [Fact]
    public async Task Selectors_Honour_JsonPropertyName()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>()
            .Where(Filter.Equals<SalesOrder>(o => o.CustomerNo, "C0001"))
            .ToListAsync();

        Assert.Contains("$filter=Sell_to_Customer_No eq 'C0001'", Decode(urls[0]));
    }

    [Fact]
    public async Task Selectors_Support_Nested_Navigation_Paths()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>()
            .Where(Filter.Equals<SalesOrder>(o => o.Customer!.Name, "ACME"))
            .ToListAsync();

        Assert.Contains("$filter=customer/name eq 'ACME'", Decode(urls[0]));
    }

    [Fact]
    public void Non_Property_Selectors_Are_Rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Filter.Equals<SalesOrder>(o => o.No.ToUpperInvariant(), "X"));

        Assert.Contains("property selector", ex.Message);
    }

    #endregion

    #region Ordering

    [Fact]
    public async Task Multi_Field_Ordering_Is_Preserved()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>()
            .OrderByDescending(o => o.Amount)
            .ThenBy(o => o.No)
            .ToListAsync();

        Assert.Contains("$orderby=amount desc,no asc", Decode(urls[0]));
    }

    [Fact]
    public async Task OrderBy_Resets_But_ThenBy_Appends()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>()
            .OrderBy(o => o.Amount)
            .OrderBy(o => o.No)
            .ThenByDescending(o => o.Status)
            .ToListAsync();

        Assert.Contains("$orderby=no asc,status desc", Decode(urls[0]));
    }

    [Fact]
    public void QueryOptions_ThenBy_Appends_Instead_Of_Overwriting()
    {
        var options = new QueryOptions()
            .OrderByAsc("a")
            .ThenByDesc("b")
            .ThenByAsc("c");

        Assert.Equal("a asc,b desc,c asc", options.OrderBy);
    }

    #endregion

    #region Expand and count

    [Fact]
    public async Task Expand_Emits_Navigation_Properties()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>().Expand(o => o.Lines).ToListAsync();

        Assert.Contains("$expand=lines", Decode(urls[0]));
    }

    [Fact]
    public async Task Expand_Accepts_Raw_Nested_Syntax()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>().Expand("lines($select=lineNo)").ToListAsync();

        Assert.Contains("$expand=lines($select=lineNo)", Decode(urls[0]));
    }

    // Expand encoding is selective: structural characters survive, everything unsafe is
    // escaped. Previously only spaces were handled, so an '&' inside a nested filter
    // truncated the query string.
    [Fact]
    public async Task Expand_Escapes_Unsafe_Characters_Inside_Nested_Filters()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>().Expand("lines($filter=code eq 'A&B')").ToListAsync();

        Assert.Contains("$expand=lines($filter=code%20eq%20'A%26B')", urls[0]);
    }

    [Fact]
    public async Task ToPageAsync_Returns_Items_And_Total()
    {
        var (client, urls) = Capturing("{\"@odata.count\":42,\"value\":[{\"no\":\"1\"},{\"no\":\"2\"}]}");

        var page = await client.Query<SalesOrder>().Top(2).ToPageAsync();

        Assert.Contains("$count=true", Decode(urls[0]));
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(42, page.TotalCount);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task CountAsync_Uses_Server_Count_Without_Fetching_Rows()
    {
        var (client, urls) = Capturing("{\"@odata.count\":137,\"value\":[]}");

        var count = await client.Query<SalesOrder>().CountAsync();

        Assert.Equal(137, count);
        Assert.Single(urls);
        Assert.Contains("$top=0", Decode(urls[0]));
    }

    #endregion

    #region Paging and streaming

    [Fact]
    public async Task StreamAsync_Stops_Fetching_When_Enumeration_Stops()
    {
        var requests = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
        {
            requests++;
            return TestBase.Json("{\"value\":[{\"no\":\"a\"},{\"no\":\"b\"}]}");
        }));

        var seen = new List<string>();

        await foreach (var order in client.Query<SalesOrder>().PageSize(2).StreamAsync())
        {
            seen.Add(order.No);
            if (seen.Count == 3)
                break;
        }

        Assert.Equal(3, seen.Count);

        // Two pages of two is enough for three items — the third page is never requested.
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task Top_Caps_Results_Across_Pages()
    {
        var client = TestBase.CreateClient(TestBase.WithToken(_ =>
            TestBase.Json("{\"value\":[{\"no\":\"a\"},{\"no\":\"b\"}]}")));

        var all = await client.Query<SalesOrder>().PageSize(2).Top(3).ToAllAsync();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task StreamAsync_Follows_NextLink()
    {
        var urls = new List<string>();
        var call = 0;

        var client = TestBase.CreateClient(TestBase.WithToken(req =>
        {
            urls.Add(req.RequestUri!.AbsoluteUri);

            var body = call++ == 0
                ? "{\"value\":[{\"no\":\"a\"}],\"@odata.nextLink\":\"https://test/next\"}"
                : "{\"value\":[{\"no\":\"b\"}]}";

            return TestBase.Json(body);
        }));

        var all = await client.Query<SalesOrder>().ToAllAsync();

        Assert.Equal(["a", "b"], all.Select(o => o.No));
        Assert.Equal("https://test/next", urls[1]);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Requests_A_Single_Row()
    {
        var (client, urls) = Capturing("{\"value\":[{\"no\":\"a\"}]}");

        var first = await client.Query<SalesOrder>().FirstOrDefaultAsync();

        Assert.Equal("a", first!.No);
        Assert.Contains("$top=1", Decode(urls[0]));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_Returns_Null_When_Empty()
    {
        var (client, _) = Capturing("{\"value\":[]}");

        Assert.Null(await client.Query<SalesOrder>().FirstOrDefaultAsync());
    }

    [Fact]
    public async Task Where_Combines_With_And()
    {
        var (client, urls) = Capturing();

        await client.Query<SalesOrder>()
            .Where(Filter.Equals<SalesOrder>(o => o.Status, "Open"))
            .Where(Filter.GreaterThan<SalesOrder>(o => o.Amount, 100))
            .ToListAsync();

        Assert.Contains("$filter=(status eq 'Open') and (amount gt 100)", Decode(urls[0]));
    }

    #endregion

    #region Multi-company

    [Fact]
    public async Task ForCompany_Scopes_Urls_Without_Reauthenticating()
    {
        var tokenCalls = 0;
        var urls = new List<string>();

        var client = TestBase.CreateClient(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("auth"))
            {
                tokenCalls++;
                return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":3600}");
            }

            urls.Add(req.RequestUri!.AbsoluteUri);
            return TestBase.Json("{\"value\":[]}");
        });

        await client.Query<SalesOrder>().ToListAsync();
        await client.ForCompany("KRAL AG").Query<SalesOrder>().ToListAsync();

        Assert.Contains("Company('Test')", urls[0]);
        Assert.Contains("Company('KRAL%20AG')", urls[1]);

        // The token cache is shared with the scoped client.
        Assert.Equal(1, tokenCalls);
    }

    [Fact]
    public void ForCompany_Returns_Same_Instance_For_Same_Company()
    {
        var (client, _) = Capturing();

        Assert.Same(client, client.ForCompany("Test"));
        Assert.NotSame(client, client.ForCompany("Other"));
    }

    [Fact]
    public async Task GetCompaniesAsync_Queries_The_Service_Root()
    {
        var (client, urls) = Capturing(
            "{\"value\":[{\"Name\":\"CRONUS AG\",\"Display_Name\":\"CRONUS\"},{\"Name\":\"KRAL AG\"}]}");

        var companies = await client.GetCompaniesAsync();

        // No Company('...') segment — the company list is tenant-level.
        Assert.DoesNotContain("Company(", urls[0]);
        Assert.EndsWith("/Company", urls[0]);

        Assert.Equal(["CRONUS AG", "KRAL AG"], companies.Select(c => c.Name));
        Assert.Equal("CRONUS", companies[0].DisplayName);
        Assert.Null(companies[1].DisplayName);
    }

    #endregion
}
