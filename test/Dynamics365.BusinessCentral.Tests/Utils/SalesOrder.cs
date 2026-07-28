using Dynamics365.BusinessCentral.OData;
using System.Text.Json.Serialization;

namespace Dynamics365.BusinessCentral.Tests.Utils;

/// <summary>Annotated entity used to exercise path inference and typed selectors.</summary>
[BusinessCentralEntity("salesOrders")]
public sealed class SalesOrder
{
    public string No { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>Exercises the JsonPropertyName branch of property-name resolution.</summary>
    [JsonPropertyName("Sell_to_Customer_No")]
    public string CustomerNo { get; set; } = string.Empty;

    /// <summary>Exercises nested navigation paths.</summary>
    public SalesOrderCustomer? Customer { get; set; }

    public List<SalesOrderLine> Lines { get; set; } = [];
}

public sealed class SalesOrderCustomer
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SalesOrderLine
{
    public int LineNo { get; set; }
}

/// <summary>Deliberately not annotated, to test the error path.</summary>
public sealed class UnannotatedEntity
{
    public string Name { get; set; } = string.Empty;
}
