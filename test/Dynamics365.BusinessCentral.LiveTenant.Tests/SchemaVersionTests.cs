using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Xunit.Abstractions;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// <c>$schemaversion=2.1</c> and the OData <c>in</c> operator — the pairing that decides whether
/// <see cref="Filter.In(string, object[])"/> may render natively.
/// </summary>
/// <remarks>
/// <para>
/// This is finding S1, pinned. Business Central answers <c>501</c> to a native <c>in</c> under the
/// default schema version and <c>200</c> under 2.1, with byte-identical result sets. The package
/// therefore defaults <c>Filter.In</c> to a portable <c>or</c>-chain and only renders <c>in</c>
/// when the schema version permits it.
/// </para>
/// <para>
/// Both halves matter. Without the first, nobody would know why the or-chain default exists and a
/// future contributor would "simplify" it; without the second, the opt-in would be untested.
/// </para>
/// </remarks>
public sealed class SchemaVersionTests(ITestOutputHelper output)
{
    /// <summary>Native <c>in</c> is rejected when no schema version is sent.</summary>
    [LiveTenantFact]
    public async Task Native_in_is_rejected_without_schema_version_2_1()
    {
        var client = LiveTenant.CreateClient(o => o.InStyle = ODataInStyle.Native);
        var values = await SampleProductionOrderNumbersAsync();

        var exception = await Assert.ThrowsAnyAsync<BusinessCentralException>(() =>
            client.Query<LdatSummaryRow>()
                .Where(Filter.In<LdatSummaryRow>(x => x.ProductionOrderNo, values))
                .ToListAsync());

        output.WriteLine($"native in, no schema version → {(int)exception.StatusCode} {exception.StatusCode}");

        Assert.Equal(501, (int)exception.StatusCode);
    }

    /// <summary>
    /// Under 2.1 the same query succeeds, and returns exactly what the portable or-chain returns.
    /// </summary>
    [LiveTenantFact]
    public async Task Native_in_under_schema_version_2_1_matches_the_or_chain()
    {
        var client = LiveTenant.CreateClient(o =>
        {
            o.SchemaVersion = "2.1";
            o.InStyle = ODataInStyle.Native;
        });

        var values = await SampleProductionOrderNumbersAsync();

        var native = await client.Query<LdatSummaryRow>()
            .Where(Filter.In<LdatSummaryRow>(x => x.ProductionOrderNo, values, ODataInStyle.Native))
            .ToAllAsync();

        var orChain = await client.Query<LdatSummaryRow>()
            .Where(Filter.In<LdatSummaryRow>(x => x.ProductionOrderNo, values, ODataInStyle.OrChain))
            .ToAllAsync();

        output.WriteLine($"{values.Length} keys → native {native.Count} rows, or-chain {orChain.Count} rows");

        Assert.NotEmpty(native);
        Assert.Equal(
            orChain.Select(r => r.SystemId).OrderBy(id => id),
            native.Select(r => r.SystemId).OrderBy(id => id));
    }

    /// <summary>
    /// Three real production-order numbers from the tenant. Read rather than invented, because a
    /// filter that matches nothing would let both halves of the comparison pass empty.
    /// </summary>
    private static async Task<object[]> SampleProductionOrderNumbersAsync()
    {
        var client = LiveTenant.CreateClient();

        var rows = await client.Query<LdatSummaryRow>().Top(3).ToListAsync();

        Assert.Equal(3, rows.Count);

        return rows.Select(r => (object)r.ProductionOrderNo).ToArray();
    }
}
