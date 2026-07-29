using Dynamics365.BusinessCentral.OData;
using Dynamics365.BusinessCentral.Tests.Utils;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// <see cref="BusinessCentralField"/> and the now-public <see cref="EntityPath"/> let
/// path-based consumers derive wire names from the entity model instead of hand-maintained
/// constants classes. Resolution must match deserialization exactly.
/// </summary>
public class BusinessCentralFieldTests
{
    [Fact]
    public void JsonPropertyName_Wins_Over_The_Naming_Policy()
    {
        Assert.Equal("Sell_to_Customer_No", BusinessCentralField.Of<SalesOrder>(o => o.CustomerNo));
    }

    [Fact]
    public void Unannotated_Properties_Follow_The_CamelCase_Policy()
    {
        Assert.Equal("no", BusinessCentralField.Of<SalesOrder>(o => o.No));
        Assert.Equal("amount", BusinessCentralField.Of<SalesOrder>(o => o.Amount));
    }

    [Fact]
    public void Nested_Selectors_Become_Navigation_Paths()
    {
        Assert.Equal("customer/name", BusinessCentralField.Of<SalesOrder>(o => o.Customer!.Name));
    }

    [Fact]
    public void Computed_Expressions_Are_Rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            BusinessCentralField.Of<SalesOrder>(o => o.No.ToUpperInvariant()));
    }

    [Fact]
    public void EntityPath_Is_Publicly_Resolvable()
    {
        Assert.Equal("salesOrders", EntityPath.For<SalesOrder>());
    }
}
