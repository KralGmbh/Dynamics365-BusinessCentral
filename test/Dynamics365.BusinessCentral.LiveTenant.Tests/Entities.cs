using System.Text.Json.Serialization;
using Dynamics365.BusinessCentral.OData;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// A row of the sandbox's <c>LdatSummary</c> page — the smallest published page here (16 columns,
/// ~1,900 rows), which makes it the cheap target for paging and projection facts.
/// </summary>
/// <remarks>
/// Note <c>SystemId</c>: this page answers it in <b>PascalCase</b>, while <c>LDATProdOrderLine</c>
/// and <c>LDATSalesLine</c> on the same tenant answer <c>systemId</c>. That is not a mistake in
/// this file — it is the casing drift measured in the <c>$metadata</c> probe round, and
/// <see cref="ProjectionTests"/> depends on both spellings existing.
/// </remarks>
[BusinessCentralEntity("LdatSummary")]
public sealed class LdatSummaryRow
{
    [JsonPropertyName("SystemId")]
    public Guid SystemId { get; set; }

    [JsonPropertyName("productionOrderID")]
    public Guid ProductionOrderId { get; set; }

    [JsonPropertyName("productionOrderNo")]
    public string ProductionOrderNo { get; set; } = "";

    [JsonPropertyName("serialNo")]
    public string SerialNo { get; set; } = "";
}

/// <summary>
/// A row of <c>LDATProdOrderLine</c>, carrying the one thing this suite could not find anywhere
/// smaller: a datetime column with real, spread-out values (<c>endingDateTime</c>).
/// </summary>
[BusinessCentralEntity("LDATProdOrderLine")]
public sealed class ProdOrderLine
{
    [JsonPropertyName("systemId")]
    public Guid SystemId { get; set; }

    [JsonPropertyName("prodOrderNo")]
    public string ProdOrderNo { get; set; } = "";

    [JsonPropertyName("lineNo")]
    public int LineNo { get; set; }

    [JsonPropertyName("endingDateTime")]
    public DateTimeOffset EndingDateTime { get; set; }
}

/// <summary>
/// A deliberately two-column view of <c>LDATSalesLine</c>, which has <b>373</b> columns and
/// ~29,700 rows on this tenant.
/// </summary>
/// <remarks>
/// Both numbers are load-bearing. The row count is the only one in reach that exceeds Business
/// Central's own server page, so this is the type <see cref="PagingTests"/> uses to make the
/// server issue a genuine <c>@odata.nextLink</c> without the client asking for a page size. The
/// column count is why the derived <c>$select</c> matters: reading this set unprojected would
/// transfer roughly two orders of magnitude more data for the same rows.
/// </remarks>
[BusinessCentralEntity("LDATSalesLine")]
public sealed class SalesLine
{
    [JsonPropertyName("systemId")]
    public Guid SystemId { get; set; }

    [JsonPropertyName("ccoProdOrderNo")]
    public string ProdOrderNo { get; set; } = "";
}
