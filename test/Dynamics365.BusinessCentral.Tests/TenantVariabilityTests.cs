using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Tests.Utils;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins the escape hatches for behaviour the package inferred from a single tenant.
/// </summary>
/// <remarks>
/// Business Central deployments differ in ways no one consumer's feedback can reveal — schema
/// version, gateway URL limits, whether entity classes are per-use projections or broad shared
/// types. Where a default encodes one deployment's measurement, a consumer on a different one
/// must be able to override it at registration rather than at every call site. These tests
/// exist so that stays true.
/// </remarks>
public class TenantVariabilityTests : TestBase
{
    #region DeriveSelect

    [Fact]
    public async Task DeriveSelect_False_Sends_No_Select_At_All()
    {
        string? url = null;

        var client = CreateClient(
            WithToken(req =>
            {
                url = req.RequestUri!.AbsoluteUri;
                return Json("""{"value":[]}""");
            }),
            configure: o => o.DeriveSelect = false);

        await client.Query<SalesOrder>().ToListAsync();

        Assert.NotNull(url);
        Assert.DoesNotContain("$select", url!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeriveSelect_Defaults_To_On()
    {
        string? url = null;

        var client = CreateClient(WithToken(req =>
        {
            url = req.RequestUri!.AbsoluteUri;
            return Json("""{"value":[]}""");
        }));

        await client.Query<SalesOrder>().ToListAsync();

        Assert.Contains("$select", url!, StringComparison.Ordinal);
    }

    /// <summary>Turning derivation off must not disarm an explicit projection.</summary>
    [Fact]
    public async Task Explicit_Select_Still_Wins_When_Derivation_Is_Off()
    {
        string? url = null;

        var client = CreateClient(
            WithToken(req =>
            {
                url = req.RequestUri!.AbsoluteUri;
                return Json("""{"value":[]}""");
            }),
            configure: o => o.DeriveSelect = false);

        await client.Query<SalesOrder>().Select(x => x.No).ToListAsync();

        Assert.Contains("$select=no", url!, StringComparison.Ordinal);
    }

    /// <summary>
    /// With derivation off there is no derived projection to blame, so a 400 must not be
    /// decorated with the hint that explains one.
    /// </summary>
    [Fact]
    public async Task No_Derived_Select_Hint_When_Derivation_Is_Off()
    {
        var client = CreateClient(
            WithToken(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":{"code":"BadRequest","message":"Invalid filter expression."}}""")
            }),
            configure: o => o.DeriveSelect = false);

        var ex = await Assert.ThrowsAsync<Errors.BusinessCentralValidationException>(() =>
            client.Query<SalesOrder>().ToListAsync());

        Assert.DoesNotContain("$select derived", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Filter.In rendering

    [Fact]
    public void In_Defaults_To_The_Or_Chain()
    {
        var filter = Filter.In("no", ["A", "B", "C"]);

        Assert.Equal("(no eq 'A') or (no eq 'B') or (no eq 'C')", filter.Value);
    }

    [Fact]
    public void In_Native_Emits_The_OData_In_Operator()
    {
        var filter = Filter.In("no", ["A", "B", "C"], ODataInStyle.Native);

        Assert.Equal("no in ('A','B','C')", filter.Value);
    }

    /// <summary>
    /// The reason the escape hatch exists — and a pin on the size claim the package makes in
    /// docs and in the URL-guard exception message.
    /// </summary>
    /// <remarks>
    /// Measured on the percent-encoded form, since that is what counts against
    /// <c>MaxQueryStringLength</c>: 25 keys render to 942 encoded characters as an or-chain versus 438
    /// natively, about 2.2× per key. An earlier estimate of "four times" undercounted the
    /// native form by ignoring that each quote encodes to <c>%27</c>. If this ratio moves, the
    /// prose has to move with it.
    /// </remarks>
    [Fact]
    public void Or_Chain_Is_Roughly_Twice_The_Encoded_Width_Of_Native()
    {
        object[] keys = [.. Enumerable.Range(0, 25).Select(i => (object)$"EBH{i:D5}")];

        var orChain = Uri.EscapeDataString(Filter.In("no", keys).Value).Length;
        var native = Uri.EscapeDataString(Filter.In("no", keys, ODataInStyle.Native).Value).Length;

        var ratio = (double)orChain / native;

        Assert.InRange(ratio, 2.0, 2.5);
    }

    [Fact]
    public void In_Native_Typed_Overload_Resolves_The_Field_Name()
    {
        var filter = Filter.In<SalesOrder>(x => x.CustomerNo, ["C1", "C2"], ODataInStyle.Native);

        Assert.Equal("Sell_to_Customer_No in ('C1','C2')", filter.Value);
    }

    [Fact]
    public void In_Native_Collapses_A_Single_Value_To_Eq()
    {
        Assert.Equal("no eq 'A'", Filter.In("no", ["A"], ODataInStyle.Native).Value);
    }

    [Fact]
    public void In_Native_Matches_Nothing_For_An_Empty_Collection()
    {
        Assert.Equal(Filter.None.Value, Filter.In("no", [], ODataInStyle.Native).Value);
    }

    /// <summary>Explicitly asking for the default must render the default.</summary>
    [Fact]
    public void In_OrChain_Style_Matches_The_Parameterless_Form()
    {
        Assert.Equal(
            Filter.In("no", ["A", "B"]).Value,
            Filter.In("no", ["A", "B"], ODataInStyle.OrChain).Value);
    }

    #endregion

    #region $schemaversion

    /// <summary>
    /// Native <c>in</c> is useless without this. Microsoft documents the operator as working
    /// only in <c>$schemaversion=2.1</c>, so shipping <see cref="ODataInStyle.Native"/> without
    /// a way to request that version would be an escape hatch onto a wall.
    /// </summary>
    [Fact]
    public async Task SchemaVersion_Is_Sent_When_Configured()
    {
        string? url = null;

        var client = CreateClient(
            WithToken(req =>
            {
                url = req.RequestUri!.AbsoluteUri;
                return Json("""{"value":[]}""");
            }),
            configure: o => o.SchemaVersion = "2.1");

        await client.Query<SalesOrder>()
            .Where(Filter.In<SalesOrder>(x => x.No, ["A", "B"], ODataInStyle.Native))
            .ToListAsync();

        Assert.Contains("$schemaversion=2.1", url!, StringComparison.Ordinal);

        // "no in ('A','B')" once percent-encoded — parentheses become %28/%29.
        Assert.Contains("no%20in%20%28", url!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_SchemaVersion_Is_Sent_By_Default()
    {
        string? url = null;

        var client = CreateClient(WithToken(req =>
        {
            url = req.RequestUri!.AbsoluteUri;
            return Json("""{"value":[]}""");
        }));

        await client.Query<SalesOrder>().ToListAsync();

        Assert.DoesNotContain("$schemaversion", url!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_SchemaVersion_Is_Treated_As_Unset(string value)
    {
        string? url = null;

        var client = CreateClient(
            WithToken(req =>
            {
                url = req.RequestUri!.AbsoluteUri;
                return Json("""{"value":[]}""");
            }),
            configure: o => o.SchemaVersion = value);

        await client.Query<SalesOrder>().ToListAsync();

        Assert.DoesNotContain("$schemaversion", url!, StringComparison.Ordinal);
    }

    /// <summary>
    /// S2: a schema version that reached only list queries would leave reads-by-key, writes,
    /// the company list, raw queries and <c>$metadata</c> running under a different contract
    /// from everything else — silently, and differently per method.
    /// </summary>
    [Theory]
    [InlineData("list")]
    [InlineData("get-by-key")]
    [InlineData("get-by-key-with-select")]
    [InlineData("patch")]
    [InlineData("delete")]
    [InlineData("companies")]
    [InlineData("raw")]
    [InlineData("metadata")]
    public async Task SchemaVersion_Reaches_Every_Url_Builder(string operation)
    {
        var urls = new List<string>();

        var client = CreateClient(
            WithToken(req =>
            {
                urls.Add(req.RequestUri!.AbsoluteUri);
                return Json("""{"value":[],"no":"X"}""");
            }),
            configure: o => o.SchemaVersion = "2.1");

        await Invoke(client, operation);

        var url = Assert.Single(urls);
        Assert.Contains("$schemaversion=2.1", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("get-by-key")]
    [InlineData("get-by-key-with-select")]
    [InlineData("patch")]
    [InlineData("delete")]
    [InlineData("companies")]
    [InlineData("raw")]
    [InlineData("metadata")]
    public async Task No_SchemaVersion_On_Any_Builder_When_Unset(string operation)
    {
        var urls = new List<string>();

        var client = CreateClient(WithToken(req =>
        {
            urls.Add(req.RequestUri!.AbsoluteUri);
            return Json("""{"value":[],"no":"X"}""");
        }));

        await Invoke(client, operation);

        Assert.DoesNotContain("$schemaversion", Assert.Single(urls), StringComparison.Ordinal);
    }

    /// <summary>A caller who stated their own version in a raw URL keeps it.</summary>
    [Fact]
    public async Task Raw_Url_Keeps_A_Caller_Supplied_SchemaVersion()
    {
        string? url = null;

        var client = CreateClient(
            WithToken(req =>
            {
                url = req.RequestUri!.AbsoluteUri;
                return Json("""{"value":[]}""");
            }),
            configure: o => o.SchemaVersion = "2.1");

        await client.QueryRawAsync<TestRawResponse>("salesOrders?$schemaversion=2.0");

        Assert.Contains("$schemaversion=2.0", url!, StringComparison.Ordinal);
        Assert.DoesNotContain("2.1", url!, StringComparison.Ordinal);
    }

    private static async Task Invoke(
        Dynamics365.BusinessCentral.Client.BusinessCentralClient client,
        string operation)
    {
        switch (operation)
        {
            case "list": await client.Query<SalesOrder>().ToListAsync(); break;
            case "get-by-key": await client.GetAsync<SalesOrder>("salesOrders", "X"); break;
            case "get-by-key-with-select":
                await client.GetAsync<SalesOrder>("salesOrders", "X", ["no"]); break;
            case "patch":
                await client.PatchAsync("salesOrders", "X", new TestEntity { Name = "N" }); break;
            case "delete": await client.DeleteAsync("salesOrders", "X"); break;
            case "companies": await client.GetCompaniesAsync(); break;
            case "raw": await client.QueryRawAsync<TestRawResponse>("salesOrders?$top=1"); break;
            default: await client.GetMetadataAsync(); break;
        }
    }

    #endregion

    #region Query-string ceiling (S4)

    /// <summary>
    /// The gateway limits the query string, not the URL. Measured across two environments: the
    /// query-string ceiling held still at 8,099 while the full URL moved with the prefix, which
    /// varies by environment name, company name and entity-set path. A full-URL limit is
    /// therefore too strict on long prefixes and too loose on short ones.
    /// </summary>
    [Fact]
    public async Task Guard_Measures_The_Query_String_Not_The_Prefix()
    {
        var observer = new TestObserver();

        // Same query, wildly different prefix lengths.
        var shortPrefix = CreateClient(WithToken(_ => Json("""{"value":[]}""")), observer,
            o => { o.Company = "A"; o.QueryStringLengthWarningThreshold = 200; });

        var longPrefix = CreateClient(WithToken(_ => Json("""{"value":[]}""")), observer,
            o =>
            {
                o.Company = new string('X', 400);
                o.QueryStringLengthWarningThreshold = 200;
            });

        await shortPrefix.Query<SalesOrder>().Where(f => f.In(x => x.No, Keys(12))).ToListAsync();
        await longPrefix.Query<SalesOrder>().Where(f => f.In(x => x.No, Keys(12))).ToListAsync();

        Assert.Equal(2, observer.UrlWarnings.Count);

        // Identical query strings despite a ~400-character difference in URL length.
        Assert.Equal(
            observer.UrlWarnings[0].QueryStringLength,
            observer.UrlWarnings[1].QueryStringLength);

        Assert.True(
            observer.UrlWarnings[1].UrlLength - observer.UrlWarnings[0].UrlLength > 300,
            "expected the prefixes to differ substantially");
    }

    /// <summary>A long prefix must not consume the caller's query-string budget.</summary>
    [Fact]
    public async Task A_Long_Company_Name_Does_Not_Trip_The_Limit()
    {
        var client = CreateClient(
            WithToken(_ => Json("""{"value":[]}""")),
            configure: o =>
            {
                o.Company = new string('X', 900);
                o.MaxQueryStringLength = 400;
            });

        // URL is ~1,000 characters; the query string is short, so this must be sent.
        await client.Query<SalesOrder>().ToListAsync();
    }

    private static object[] Keys(int count) =>
        [.. Enumerable.Range(0, count).Select(i => (object)$"EBH{i:D5}")];

    #endregion

    #region Auto in-style resolution

    private static async Task<string> UrlFor(
        Action<Dynamics365.BusinessCentral.Options.BusinessCentralOptions>? configure,
        Func<Dynamics365.BusinessCentral.Client.BusinessCentralClient, Task> run)
    {
        string? url = null;

        var client = CreateClient(
            WithToken(req =>
            {
                url ??= req.RequestUri!.AbsoluteUri;
                return Json("""{"value":[]}""");
            }),
            configure: configure);

        await run(client);

        return Uri.UnescapeDataString(url!);
    }

    [Fact]
    public async Task Auto_Renders_The_Or_Chain_Without_A_Schema_Version()
    {
        var url = await UrlFor(null, c =>
            c.Query<SalesOrder>().Where(f => f.In(x => x.No, ["A", "B"])).ToListAsync());

        Assert.Contains("(no eq 'A') or (no eq 'B')", url, StringComparison.Ordinal);
    }

    /// <summary>The point of the change: setting 2.1 is enough, no call site edits.</summary>
    [Fact]
    public async Task Auto_Renders_Native_In_At_Schema_Version_2_1()
    {
        var url = await UrlFor(o => o.SchemaVersion = "2.1", c =>
            c.Query<SalesOrder>().Where(f => f.In(x => x.No, ["A", "B"])).ToListAsync());

        Assert.Contains("no in ('A','B')", url, StringComparison.Ordinal);
        Assert.DoesNotContain(" or ", url, StringComparison.Ordinal);
    }

    /// <summary>2.0 was measured to still reject `in`, so it must not flip.</summary>
    [Fact]
    public async Task Auto_Stays_Or_Chained_At_Schema_Version_2_0()
    {
        var url = await UrlFor(o => o.SchemaVersion = "2.0", c =>
            c.Query<SalesOrder>().Where(f => f.In(x => x.No, ["A", "B"])).ToListAsync());

        Assert.Contains("(no eq 'A') or (no eq 'B')", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2.1", true)]
    [InlineData("2.10", true)]
    [InlineData("3.0", true)]
    [InlineData("2.0", false)]
    [InlineData("1.0", false)]
    [InlineData("banana", false)]
    public async Task Schema_Version_Parsing_Decides_The_Rendering(string version, bool native)
    {
        var url = await UrlFor(o => o.SchemaVersion = version, c =>
            c.Query<SalesOrder>().Where(f => f.In(x => x.No, ["A", "B"])).ToListAsync());

        Assert.Equal(native, url.Contains("no in (", StringComparison.Ordinal));
    }

    /// <summary>
    /// The composition case, and the one that decides whether this feature is worth anything:
    /// a chunked key lookup is almost always <c>.And(...)</c>-ed with something else, so
    /// freezing the rendering on composition would mean the automatic form never applies where
    /// it actually matters.
    /// </summary>
    [Fact]
    public async Task Composition_Preserves_The_Deferred_Rendering()
    {
        var url = await UrlFor(o => o.SchemaVersion = "2.1", c =>
            c.Query<SalesOrder>()
                .Where(f => f.In(x => x.No, ["A", "B"]).And(f.Equals(x => x.Status, "Open")))
                .ToListAsync());

        Assert.Contains("no in ('A','B')", url, StringComparison.Ordinal);
        Assert.Contains("status eq 'Open'", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Negation_Preserves_The_Deferred_Rendering()
    {
        var url = await UrlFor(o => o.SchemaVersion = "2.1", c =>
            c.Query<SalesOrder>().Where(Filter.In<SalesOrder>(x => x.No, ["A", "B"]).Not()).ToListAsync());

        Assert.Contains("not (no in ('A','B'))", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_OrChain_Is_Not_Flipped_By_Schema_Version()
    {
        var url = await UrlFor(o => o.SchemaVersion = "2.1", c =>
            c.Query<SalesOrder>()
                .Where(Filter.In<SalesOrder>(x => x.No, ["A", "B"], ODataInStyle.OrChain))
                .ToListAsync());

        Assert.Contains("(no eq 'A') or (no eq 'B')", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_Native_Survives_Without_A_Schema_Version()
    {
        var url = await UrlFor(null, c =>
            c.Query<SalesOrder>()
                .Where(Filter.In<SalesOrder>(x => x.No, ["A", "B"], ODataInStyle.Native))
                .ToListAsync());

        Assert.Contains("no in ('A','B')", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InStyle_Option_Overrides_The_Schema_Version()
    {
        var orChained = await UrlFor(
            o => { o.SchemaVersion = "2.1"; o.InStyle = ODataInStyle.OrChain; },
            c => c.Query<SalesOrder>().Where(f => f.In(x => x.No, ["A", "B"])).ToListAsync());

        Assert.Contains("(no eq 'A') or (no eq 'B')", orChained, StringComparison.Ordinal);

        var forced = await UrlFor(
            o => o.InStyle = ODataInStyle.Native,
            c => c.Query<SalesOrder>().Where(f => f.In(x => x.No, ["A", "B"])).ToListAsync());

        Assert.Contains("no in ('A','B')", forced, StringComparison.Ordinal);
    }

    /// <summary>The path-based API resolves it too, not just the fluent builder.</summary>
    [Fact]
    public async Task Path_Based_Query_Also_Resolves_Auto()
    {
        var url = await UrlFor(o => o.SchemaVersion = "2.1", c =>
            c.QueryAsync<SalesOrder>("salesOrders", Filter.In<SalesOrder>(x => x.No, ["A", "B"])));

        Assert.Contains("no in ('A','B')", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bare value has no endpoint to ask, so it stays portable. Documented, and worth pinning
    /// because it is the one place the wire form and <c>Value</c> legitimately differ.
    /// </summary>
    [Fact]
    public void Value_Stays_The_Portable_Or_Chain()
    {
        Assert.Equal(
            "(no eq 'A') or (no eq 'B')",
            Filter.In("no", ["A", "B"]).Value);
    }

    #endregion
}
