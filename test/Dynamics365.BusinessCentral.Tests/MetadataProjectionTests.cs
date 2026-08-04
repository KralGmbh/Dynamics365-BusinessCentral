using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Testing;
using Dynamics365.BusinessCentral.Tests.Utils;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Pins M4: the <c>$metadata</c> projection validator.
/// </summary>
/// <remarks>
/// The fetch is the only part needing a tenant; the parse and diff carry the logic and are
/// exercised here against a canned EDMX fixture. That split is the reason this could ship in
/// 2.0.0 alongside the behaviour it protects rather than waiting for 2.1.
/// </remarks>
public class MetadataProjectionTests : TestBase
{
    #region Fixtures

    /// <summary>
    /// Trimmed to the shape that matters: namespaced schema, an entity container mapping sets
    /// to qualified type names, scalar properties plus a navigation property that must not be
    /// mistaken for a column.
    /// </summary>
    private const string Edmx =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
          <edmx:DataServices>
            <Schema Namespace="NAV" xmlns="http://docs.oasis-open.org/odata/ns/edm">
              <EntityType Name="LDATItems">
                <Key><PropertyRef Name="no"/></Key>
                <Property Name="no" Type="Edm.String"/>
                <Property Name="description" Type="Edm.String"/>
                <Property Name="unitPrice" Type="Edm.Decimal"/>
                <Property Name="systemId" Type="Edm.Guid"/>
                <NavigationProperty Name="salesLines" Type="Collection(NAV.LDATSalesLine)"/>
              </EntityType>
              <EntityType Name="LDATSalesLine">
                <Property Name="lineNo" Type="Edm.Int32"/>
              </EntityType>
              <EntityContainer Name="NAV">
                <EntitySet Name="LDATItems" EntityType="NAV.LDATItems"/>
                <EntitySet Name="LDATSalesLine" EntityType="NAV.LDATSalesLine"/>
              </EntityContainer>
            </Schema>
          </edmx:DataServices>
        </edmx:Edmx>
        """;

    private static BusinessCentralMetadataModel Model() => BusinessCentralMetadata.Parse(Edmx);

    /// <summary>Every derived name is a real column.</summary>
    [BusinessCentralEntity("LDATItems")]
    public sealed class CleanItem
    {
        public string No { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        public decimal UnitPrice { get; set; }
    }

    /// <summary>The shape F2 endangers: a settable scalar that is not a column.</summary>
    [BusinessCentralEntity("LDATItems")]
    public sealed class DriftedItem
    {
        public string No { get; set; } = "";

        public string Discontinued { get; set; } = "";

        public decimal ObsoleteMargin { get; set; }
    }

    /// <summary>Casing that disagrees with $metadata — harmless, and must not be reported.</summary>
    [BusinessCentralEntity("ldatitems")]
    public sealed class MiscasedItem
    {
        [JsonPropertyName("NO")]
        public string No { get; set; } = "";

        [JsonPropertyName("SystemId")]
        public Guid SystemId { get; set; }
    }

    [BusinessCentralEntity("NoSuchSet")]
    public sealed class MissingSetEntity
    {
        public string No { get; set; } = "";
    }

    [BusinessCentralEntity("LDATItems/salesLines")]
    public sealed class NavigationPathEntity
    {
        public int LineNo { get; set; }
    }

    #endregion

    #region Parse

    [Fact]
    public void Parse_Indexes_Entity_Sets_To_Their_Columns()
    {
        Assert.True(Model().TryGetColumns("LDATItems", out var columns));

        Assert.Contains("no", columns);
        Assert.Contains("description", columns);
        Assert.Contains("unitPrice", columns);
        Assert.Contains("systemId", columns);
    }

    /// <summary>Navigations belong to $expand; EntitySelect never derives them.</summary>
    [Fact]
    public void Parse_Excludes_Navigation_Properties()
    {
        Model().TryGetColumns("LDATItems", out var columns);

        Assert.DoesNotContain("salesLines", columns);
    }

    [Fact]
    public void Parse_Resolves_Namespace_Qualified_Entity_Type_References()
    {
        // The EntitySet references "NAV.LDATSalesLine"; the EntityType declares "LDATSalesLine".
        Assert.True(Model().TryGetColumns("LDATSalesLine", out var columns));
        Assert.Contains("lineNo", columns);
    }

    [Fact]
    public void Parse_Matches_Entity_Set_Names_Case_Insensitively()
    {
        Assert.True(Model().TryGetColumns("ldatitems", out _));
        Assert.True(Model().TryGetColumns("LDATITEMS", out _));
    }

    [Fact]
    public void Parse_Reports_Unknown_Sets_As_Missing()
    {
        Assert.False(Model().TryGetColumns("NoSuchSet", out _));
    }

    [Fact]
    public void Parse_Rejects_An_Empty_Document()
    {
        Assert.Throws<ArgumentException>(() => BusinessCentralMetadata.Parse("  "));
    }

    #endregion

    #region Validate

    [Fact]
    public void Clean_Projection_Reports_No_Problems()
    {
        var report = BusinessCentralMetadata.Validate(Model(), [typeof(CleanItem)]);

        Assert.True(report.IsValid);
        Assert.Empty(report.Problems);
        Assert.Single(report.Checked);
    }

    /// <summary>
    /// The point of the tool: every offending name at once, not the first one and a rerun.
    /// </summary>
    [Fact]
    public void Every_Offending_Column_Is_Reported_Together()
    {
        var report = BusinessCentralMetadata.Validate(Model(), [typeof(DriftedItem)]);

        Assert.False(report.IsValid);
        Assert.Equal(2, report.Problems.Count);

        Assert.Contains(report.Problems, p => p.Property == "discontinued");
        Assert.Contains(report.Problems, p => p.Property == "obsoleteMargin");
        Assert.All(report.Problems, p =>
            Assert.Equal(BusinessCentralProjectionProblemKind.UnknownColumn, p.Kind));
    }

    /// <summary>A report has to name the member to edit, not only the name on the wire.</summary>
    [Fact]
    public void Problem_Names_The_Clr_Property_Behind_The_Wire_Name()
    {
        var report = BusinessCentralMetadata.Validate(Model(), [typeof(DriftedItem)]);

        var problem = report.Problems.Single(p => p.Property == "obsoleteMargin");

        Assert.Equal("ObsoleteMargin", problem.DeclaringProperty);
        Assert.Contains("DriftedItem.ObsoleteMargin", problem.ToString(), StringComparison.Ordinal);
        Assert.Contains("[JsonIgnore]", problem.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression guard tied to M1: <c>$select</c> is case-insensitive on Business Central, so
    /// matching ordinally here would report working projections as broken. The 16 drifted wire
    /// names one consumer runs in production must come back clean.
    /// </summary>
    [Fact]
    public void Casing_Drift_Is_Not_Reported_As_A_Problem()
    {
        var report = BusinessCentralMetadata.Validate(Model(), [typeof(MiscasedItem)]);

        Assert.True(report.IsValid, report.Describe());
    }

    [Fact]
    public void Missing_Entity_Set_Is_Reported_Once_Not_Per_Column()
    {
        var report = BusinessCentralMetadata.Validate(Model(), [typeof(MissingSetEntity)]);

        var problem = Assert.Single(report.Problems);

        Assert.Equal(BusinessCentralProjectionProblemKind.UnknownEntitySet, problem.Kind);
        Assert.Null(problem.Property);
        Assert.Contains("not in $metadata", problem.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A navigation path names no single set, so it is skipped — visibly, not silently.</summary>
    [Fact]
    public void Navigation_Paths_Are_Skipped_And_Recorded()
    {
        var report = BusinessCentralMetadata.Validate(Model(), [typeof(NavigationPathEntity)]);

        Assert.True(report.IsValid);
        Assert.Empty(report.Checked);
        Assert.Single(report.Skipped);
        Assert.Contains("skipped", report.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Lists_Every_Problem_Across_Types()
    {
        var report = BusinessCentralMetadata.Validate(
            Model(), [typeof(CleanItem), typeof(DriftedItem), typeof(MissingSetEntity)]);

        var text = report.Describe();

        Assert.Contains("3 projection problems", text, StringComparison.Ordinal);
        Assert.Contains("discontinued", text, StringComparison.Ordinal);
        Assert.Contains("obsoleteMargin", text, StringComparison.Ordinal);
        Assert.Contains("NoSuchSet", text, StringComparison.Ordinal);
    }

    #endregion

    #region End to end over the fake transport

    private static FakeBusinessCentral FakeReturning(string metadata)
    {
        var fake = new FakeBusinessCentral();
        fake.EnqueueJson(metadata);
        return fake;
    }

    [Fact]
    public async Task AssertProjectionsResolve_Throws_Listing_Every_Problem()
    {
        using var fake = FakeReturning(Edmx);

        var ex = await Assert.ThrowsAsync<BusinessCentralProjectionException>(() =>
            BusinessCentralMetadata.AssertProjectionsResolveAsync(
                fake.Client, [typeof(DriftedItem), typeof(MissingSetEntity)]));

        Assert.Equal(3, ex.Report.Problems.Count);
        Assert.Contains("discontinued", ex.Message, StringComparison.Ordinal);
        Assert.Contains("obsoleteMargin", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NoSuchSet", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssertProjectionsResolve_Passes_On_A_Clean_Projection()
    {
        using var fake = FakeReturning(Edmx);

        await BusinessCentralMetadata.AssertProjectionsResolveAsync(
            fake.Client, [typeof(CleanItem)]);
    }

    /// <summary>
    /// The '$' must reach the server unencoded — routing this through the normal path encoder
    /// would produce %24metadata, which Business Central does not recognise.
    /// </summary>
    [Fact]
    public async Task Metadata_Request_Hits_The_Service_Root_With_An_Unencoded_Dollar()
    {
        using var fake = FakeReturning(Edmx);

        await fake.Client.GetMetadataAsync();

        var request = fake.Requests.Single();

        Assert.EndsWith("/$metadata", request.PathAndQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("%24", request.PathAndQuery, StringComparison.Ordinal);

        // Service root: no Company('...') segment, since $metadata describes the tenant.
        Assert.DoesNotContain("Company(", request.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metadata_Response_Is_Returned_Verbatim()
    {
        using var fake = FakeReturning(Edmx);

        Assert.Equal(Edmx, await fake.Client.GetMetadataAsync());
    }

    #endregion

    #region Assembly scanning

    [Fact]
    public void EntityTypesIn_Finds_Annotated_Types_Only()
    {
        var found = BusinessCentralMetadata.EntityTypesIn(typeof(MetadataProjectionTests).Assembly);

        Assert.Contains(typeof(CleanItem), found);
        Assert.Contains(typeof(SalesOrder), found);
        Assert.DoesNotContain(typeof(UnannotatedEntity), found);
    }

    #endregion
}
