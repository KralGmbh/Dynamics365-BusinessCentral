using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Testing;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins alpha.6's two features: F1 (builder-inferred filters — must render identically to
/// the static <c>Filter.X&lt;T&gt;</c> form) and F2 (the fluent builder derives
/// <c>$select</c> from the entity type unless told otherwise).
/// </summary>
public class FluentSelectAndFilterBuilderTests
{
    #region F2 — derived $select

    /// <summary>Exercises every inclusion rule at once.</summary>
    private sealed class ProjectionEntity
    {
        public string No { get; set; } = "";                       // settable scalar → in
        public decimal? Amount { get; set; }                       // nullable scalar → in
        public DateOnly PostingDate { get; init; }                 // init counts as settable → in

        [JsonPropertyName("ccoSpecial")]
        public string Renamed { get; set; } = "";                  // attribute wins → "ccoSpecial"

        [JsonPropertyName("@odata.etag")]
        public string ETag { get; set; } = "";                     // annotation → out

        [JsonIgnore]
        public string ClientOnly { get; set; } = "";               // ignored → out

        public bool IsOpen => No.Length > 0;                       // get-only computed → out

        public SalesOrderCustomer? Customer { get; set; }          // navigation class → out

        public List<SalesOrderLine> Lines { get; set; } = [];      // collection → out
    }

    [Fact]
    public void Derived_Select_Includes_Only_Settable_Scalar_Columns_Sorted_Ordinally()
    {
        // Uppercase sorts before lowercase ordinally, so the attribute-renamed and
        // policy-named fields interleave deterministically.
        Assert.Equal(
            ["amount", "ccoSpecial", "no", "postingDate"],
            EntitySelect.For<ProjectionEntity>());
    }

    [Fact]
    public async Task Fluent_Query_Derives_Select_From_The_Entity_By_Default()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage<SalesOrder>();

        await bc.Client.Query<SalesOrder>().ToListAsync();

        Assert.Contains(
            "$select=Sell_to_Customer_No,amount,no,status",
            bc.Requests.Single().DecodedPathAndQuery);
    }

    [Fact]
    public async Task Explicit_Select_Overrides_The_Derived_Projection()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage<SalesOrder>();

        await bc.Client.Query<SalesOrder>().Select(o => o.No).ToListAsync();

        var query = bc.Requests.Single().DecodedPathAndQuery;
        Assert.Contains("$select=no", query);
        Assert.DoesNotContain("amount", query);
    }

    [Fact]
    public async Task SelectAll_Suppresses_The_Derived_Projection()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage<SalesOrder>();

        await bc.Client.Query<SalesOrder>().SelectAll().ToListAsync();

        Assert.DoesNotContain("$select", bc.Requests.Single().DecodedPathAndQuery);
    }

    [Fact]
    public async Task Derived_Select_Rides_The_First_Request_And_Continuations_Stay_Verbatim()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage([new SalesOrder { No = "1" }], nextLink: "page2")
          .EnqueuePage([new SalesOrder { No = "2" }]);

        var all = await bc.Client.Query<SalesOrder>().ToAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains("$select=", bc.Requests[0].PathAndQuery);

        // The server's cursor is followed untouched — it carries $select server-side.
        Assert.Equal("/page2", bc.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task CountAsync_Sends_No_Select()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueueJson("{\"@odata.count\":7,\"value\":[]}");

        var count = await bc.Client.Query<SalesOrder>().CountAsync();

        Assert.Equal(7, count);
        Assert.DoesNotContain("$select", bc.Requests.Single().PathAndQuery);
    }

    #endregion

    #region F1 — builder-inferred filters

    // The F1 acceptance criterion: a builder-composed filter renders identically to the
    // equivalent static Filter.X<TEntity>(...) chain, operator by operator.
    [Fact]
    public void Builder_Operators_Render_Identically_To_The_Static_Form()
    {
        IFilterBuilder<SalesOrder> f = GetBuilder<SalesOrder>();

        Assert.Equal(Filter.Equals<SalesOrder>(x => x.Status, "Open").Value, f.Equals(x => x.Status, "Open").Value);
        Assert.Equal(Filter.NotEquals<SalesOrder>(x => x.Status, "Open").Value, f.NotEquals(x => x.Status, "Open").Value);
        Assert.Equal(Filter.GreaterThan<SalesOrder>(x => x.Amount, 100).Value, f.GreaterThan(x => x.Amount, 100).Value);
        Assert.Equal(Filter.GreaterOrEqual<SalesOrder>(x => x.Amount, 100).Value, f.GreaterOrEqual(x => x.Amount, 100).Value);
        Assert.Equal(Filter.LessThan<SalesOrder>(x => x.Amount, 100).Value, f.LessThan(x => x.Amount, 100).Value);
        Assert.Equal(Filter.LessOrEqual<SalesOrder>(x => x.Amount, 100).Value, f.LessOrEqual(x => x.Amount, 100).Value);
        Assert.Equal(Filter.Contains<SalesOrder>(x => x.No, "10").Value, f.Contains(x => x.No, "10").Value);
        Assert.Equal(Filter.StartsWith<SalesOrder>(x => x.No, "10").Value, f.StartsWith(x => x.No, "10").Value);
        Assert.Equal(Filter.EndsWith<SalesOrder>(x => x.No, "10").Value, f.EndsWith(x => x.No, "10").Value);
        Assert.Equal(Filter.In<SalesOrder>(x => x.No, "a", "b").Value, f.In(x => x.No, "a", "b").Value);
        Assert.Equal(Filter.IsNull<SalesOrder>(x => x.Status).Value, f.IsNull(x => x.Status).Value);
        Assert.Equal(Filter.IsNotNull<SalesOrder>(x => x.Status).Value, f.IsNotNull(x => x.Status).Value);
        Assert.Equal(Filter.All.Value, f.All.Value);
        Assert.Equal(Filter.None.Value, f.None.Value);
    }

    [Fact]
    public async Task Where_With_Builder_Composes_And_Renders_End_To_End()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage<SalesOrder>();

        await bc.Client.Query<SalesOrder>()
            .Where(f => f.Equals(x => x.Status, "Open")
                         .And(f.GreaterThan(x => x.Amount, 100)))
            .ToListAsync();

        Assert.Contains(
            "$filter=(status eq 'Open') and (amount gt 100)",
            bc.Requests.Single().DecodedPathAndQuery);
    }

    [Fact]
    public async Task Builder_Where_Combines_With_Existing_Filters_Using_And()
    {
        using var bc = new FakeBusinessCentral();
        bc.EnqueuePage<SalesOrder>();

        await bc.Client.Query<SalesOrder>()
            .Where(Filter.Equals<SalesOrder>(x => x.Status, "Open"))
            .Where(f => f.GreaterThan(x => x.Amount, 100))
            .ToListAsync();

        Assert.Contains(
            "$filter=(status eq 'Open') and (amount gt 100)",
            bc.Requests.Single().DecodedPathAndQuery);
    }

    private static IFilterBuilder<TEntity> GetBuilder<TEntity>()
    {
        IFilterBuilder<TEntity>? captured = null;

        // The builder instance is only reachable through Where — capture it the way a
        // consumer's lambda sees it.
        using var bc = new FakeBusinessCentral();
        bc.Client.Query<TEntity>("x").Where(f =>
        {
            captured = f;
            return Filter.All;
        });

        return captured!;
    }

    #endregion
}
