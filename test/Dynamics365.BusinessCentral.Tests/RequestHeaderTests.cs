using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins the registration-level request headers from Microsoft's OData client-performance
/// guidance: <c>Data-Access-Intent</c> and <c>Accept-Language</c>.
/// </summary>
public class RequestHeaderTests : TestBase
{
    private static string? Header(HttpRequestMessage req, string name) =>
        req.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    #region Data-Access-Intent

    [Fact]
    public async Task ReadOnly_Intent_Is_Sent_On_Get()
    {
        string? intent = null;

        var client = CreateClient(
            WithToken(req =>
            {
                intent = Header(req, "Data-Access-Intent");
                return Json("""{"value":[]}""");
            }),
            configure: o => o.DataAccessIntent = BusinessCentralDataAccessIntent.ReadOnly);

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Equal("ReadOnly", intent);
    }

    [Fact]
    public async Task No_Intent_Header_By_Default()
    {
        string? intent = "sentinel";

        var client = CreateClient(WithToken(req =>
        {
            intent = Header(req, "Data-Access-Intent");
            return Json("""{"value":[]}""");
        }));

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Null(intent);
    }

    /// <summary>
    /// The constraint that makes GET-only mandatory rather than tidy: Microsoft documents that
    /// modification requests reject <c>ReadOnly</c>, so sending it on a write would turn every
    /// working write into an error.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Intent_Is_Never_Sent_On_A_Write(string method)
    {
        string? intent = "sentinel";

        var client = CreateClient(
            WithToken(req =>
            {
                intent = Header(req, "Data-Access-Intent");
                return Json("""{"no":"X"}""");
            }),
            configure: o => o.DataAccessIntent = BusinessCentralDataAccessIntent.ReadOnly);

        var payload = new TestEntity { Name = "X" };

        _ = method switch
        {
            "POST" => await client.PostAsync("items", payload),
            "PATCH" => await client.PatchAsync("items", "id", payload),
            "PUT" => await client.PutAsync("items", "id", payload),
            _ => await DeleteAndEcho(client, payload)
        };

        Assert.Null(intent);
    }

    private static async Task<TestEntity> DeleteAndEcho(
        Dynamics365.BusinessCentral.Client.BusinessCentralClient client,
        TestEntity payload)
    {
        await client.DeleteAsync("items", "id");
        return payload;
    }

    [Fact]
    public async Task ReadWrite_Intent_Is_Sent_On_Get()
    {
        string? intent = null;

        var client = CreateClient(
            WithToken(req =>
            {
                intent = Header(req, "Data-Access-Intent");
                return Json("""{"value":[]}""");
            }),
            configure: o => o.DataAccessIntent = BusinessCentralDataAccessIntent.ReadWrite);

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Equal("ReadWrite", intent);
    }

    /// <summary>Streaming continuations are GETs too, so the hint must survive paging.</summary>
    [Fact]
    public async Task Intent_Is_Sent_On_Every_Page_Of_A_Stream()
    {
        var intents = new List<string?>();
        var page = 0;

        var client = CreateClient(
            WithToken(req =>
            {
                intents.Add(Header(req, "Data-Access-Intent"));
                page++;

                return page == 1
                    ? Json("""{"value":[{"No":"A"}],"@odata.nextLink":"https://test/next"}""")
                    : Json("""{"value":[{"No":"B"}]}""");
            }),
            configure: o => o.DataAccessIntent = BusinessCentralDataAccessIntent.ReadOnly);

        await client.Query<SalesOrder>().ToAllAsync();

        Assert.Equal(2, intents.Count);
        Assert.All(intents, i => Assert.Equal("ReadOnly", i));
    }

    /// <summary>`$metadata` is a GET and benefits from the replica like any other read.</summary>
    [Fact]
    public async Task Intent_Is_Sent_On_The_Metadata_Request()
    {
        string? intent = null;

        var client = CreateClient(
            WithToken(req =>
            {
                intent = Header(req, "Data-Access-Intent");
                return Json("<edmx/>");
            }),
            configure: o => o.DataAccessIntent = BusinessCentralDataAccessIntent.ReadOnly);

        await client.GetMetadataAsync();

        Assert.Equal("ReadOnly", intent);
    }

    #endregion

    #region Accept-Language

    [Fact]
    public async Task Accept_Language_Is_Sent_When_Configured()
    {
        string? language = null;

        var client = CreateClient(
            WithToken(req =>
            {
                language = Header(req, "Accept-Language");
                return Json("""{"value":[]}""");
            }),
            configure: o => o.AcceptLanguage = "de-DE");

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Equal("de-DE", language);
    }

    /// <summary>Unlike the intent header, this one is meaningful on writes too.</summary>
    [Fact]
    public async Task Accept_Language_Is_Sent_On_Writes()
    {
        string? language = null;

        var client = CreateClient(
            WithToken(req =>
            {
                language = Header(req, "Accept-Language");
                return Json("""{"name":"X"}""");
            }),
            configure: o => o.AcceptLanguage = "en-US");

        await client.PostAsync("items", new TestEntity { Name = "X" });

        Assert.Equal("en-US", language);
    }

    [Fact]
    public async Task No_Accept_Language_By_Default()
    {
        string? language = "sentinel";

        var client = CreateClient(WithToken(req =>
        {
            language = Header(req, "Accept-Language");
            return Json("""{"value":[]}""");
        }));

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Null(language);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_Accept_Language_Is_Treated_As_Unset(string value)
    {
        string? language = "sentinel";

        var client = CreateClient(
            WithToken(req =>
            {
                language = Header(req, "Accept-Language");
                return Json("""{"value":[]}""");
            }),
            configure: o => o.AcceptLanguage = value);

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Null(language);
    }

    #endregion

    #region The token request must stay clean

    /// <summary>
    /// These headers are Business Central's, not the identity provider's. A manually
    /// constructed client shares one <see cref="HttpClient"/> between token and data traffic,
    /// so the separation has to come from request construction rather than from the client.
    /// </summary>
    [Fact]
    public async Task Token_Request_Carries_Neither_Header()
    {
        string? tokenIntent = "sentinel";
        string? tokenLanguage = "sentinel";

        var client = CreateClient(
            req =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("auth", StringComparison.Ordinal))
                {
                    tokenIntent = Header(req, "Data-Access-Intent");
                    tokenLanguage = Header(req, "Accept-Language");

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"access_token":"abc","expires_in":3600}""")
                    };
                }

                return Json("""{"value":[]}""");
            },
            configure: o =>
            {
                o.DataAccessIntent = BusinessCentralDataAccessIntent.ReadOnly;
                o.AcceptLanguage = "de-DE";
            });

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Null(tokenIntent);
        Assert.Null(tokenLanguage);
    }

    #endregion
}
