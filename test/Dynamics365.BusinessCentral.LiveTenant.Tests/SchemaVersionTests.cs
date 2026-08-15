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
    /// Under 2.1 the same query succeeds, and selects the same rows as the portable or-chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The or-chain is read either side of the native one and the comparison is bracketed by
    /// identity — see <see cref="LiveTenantAssert.SetWithinBracket"/>. Set equality against a
    /// single or-chain read would fail whenever a matching row appeared or disappeared between two
    /// live requests, and it would fail claiming the two renderings disagree, which is precisely
    /// the finding this fact exists to make.
    /// </para>
    /// <para>
    /// On a quiet tenant the two reference reads are identical and the bracket collapses to exact
    /// set equality, so the check loses nothing when there is nothing to absorb. Both counts are
    /// printed, so a bracket widening under real activity is visible rather than silent.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task Native_in_under_schema_version_2_1_matches_the_or_chain()
    {
        var client = LiveTenant.CreateClient(o =>
        {
            o.SchemaVersion = "2.1";
            o.InStyle = ODataInStyle.Native;
        });

        var values = await SampleProductionOrderNumbersAsync();

        async Task<IReadOnlyList<LdatSummaryRow>> ReadAsync(ODataInStyle style) =>
            await client.Query<LdatSummaryRow>()
                .Where(Filter.In<LdatSummaryRow>(x => x.ProductionOrderNo, values, style))
                .ToAllAsync();

        // The tenant is live: bracket the native read with the portable rendering so a row that
        // changes while these requests run cannot masquerade as a rendering regression.
        var orChainBefore = await ReadAsync(ODataInStyle.OrChain);
        var native = await ReadAsync(ODataInStyle.Native);
        var orChainAfter = await ReadAsync(ODataInStyle.OrChain);

        var beforeIds = UniqueIds(orChainBefore, "or-chain before");
        var nativeIds = UniqueIds(native, "native");
        var afterIds = UniqueIds(orChainAfter, "or-chain after");

        output.WriteLine(
            $"{values.Length} keys → or-chain {beforeIds.Count}/{afterIds.Count} rows around " +
            $"native {nativeIds.Count} rows");

        Assert.NotEmpty(nativeIds);
        LiveTenantAssert.SetWithinBracket(beforeIds, afterIds, nativeIds, "Native in result");
    }

    private static HashSet<Guid> UniqueIds(
        IReadOnlyCollection<LdatSummaryRow> rows,
        string rendering)
    {
        var ids = rows.Select(r => r.SystemId).ToHashSet();

        Assert.True(
            ids.Count == rows.Count,
            $"The {rendering} result contained duplicate SystemId values.");

        return ids;
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
