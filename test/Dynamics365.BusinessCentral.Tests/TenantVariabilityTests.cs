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
    /// <c>MaxUrlLength</c>: 25 keys render to 942 encoded characters as an or-chain versus 438
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

    #endregion
}
