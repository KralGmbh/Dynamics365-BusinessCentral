using Dynamics365.BusinessCentral.Errors;
using Dynamics365.BusinessCentral.OData;
using Xunit.Abstractions;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// What the gateway actually does with an over-long query string — findings S4 and S5.
/// </summary>
/// <remarks>
/// <para>
/// The package's length guard measures the <b>query string</b>, not the full URL, because the
/// measured ceiling is invariant across environments while the full URL moves with environment
/// name, company name and entity-set path. The defaults (8,000 refuse / 6,000 warn) sit under a
/// measured ceiling of 8,099.
/// </para>
/// <para>
/// The facts here deliberately raise the guard out of the way and let the request go, because the
/// point is not to test the guard — the unit suite does that — but to keep the number the guard is
/// derived from honest. If the gateway's ceiling ever moves, this is what notices.
/// </para>
/// </remarks>
public sealed class QueryStringCeilingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Adjacent clause counts that put these fixed-width keys immediately below and above the
    /// tenant's measured 8,099-character boundary.
    /// </summary>
    private const int LastAcceptedKeyCount = 125;
    private const int FirstRejectedKeyCount = LastAcceptedKeyCount + 1;

    /// <summary>
    /// A query string past the ceiling is answered <c>414 URI Too Long</c> — not the opaque 400
    /// the guard's first design assumed.
    /// </summary>
    /// <remarks>
    /// S5. The correction matters for what the guard is <i>for</i>: the server already answers
    /// legibly, so the guard's value is pre-flight diagnosis — naming the filter and the clause
    /// count — rather than decoding an unhelpful status.
    /// </remarks>
    [LiveTenantFact]
    public async Task An_over_length_query_string_is_answered_414()
    {
        var wire = new RecordingObserver();
        var client = LiveTenant.CreateClient(o =>
        {
            // Out of the way: this fact is about the gateway, not the guard.
            o.QueryStringLengthWarningThreshold = 1;
            o.MaxQueryStringLength = 50_000;
        }, wire);

        var exception = await Assert.ThrowsAnyAsync<BusinessCentralException>(() =>
            client.Query<LdatSummaryRow>()
                .Where(Filter.In<LdatSummaryRow>(
                    x => x.ProductionOrderNo,
                    OverLongKeySet(FirstRejectedKeyCount)))
                .ToListAsync());

        var warning = Assert.Single(wire.LengthWarnings);

        output.WriteLine(
            $"rejected: queryString={warning.QueryStringLength} " +
            $"orClauses={warning.OrClauseCount} → {(int)exception.StatusCode} {exception.StatusCode}");

        Assert.InRange(warning.QueryStringLength, 8_100, 8_200);
        Assert.Equal(414, (int)exception.StatusCode);
    }

    /// <summary>
    /// A query string immediately below the measured gateway ceiling is still accepted.
    /// </summary>
    /// <remarks>
    /// The other side of the same measurement, and the one that would catch the guard becoming
    /// wrong in the expensive direction: a default that refused requests Business Central would
    /// happily have served.
    /// </remarks>
    [LiveTenantFact]
    public async Task A_query_string_immediately_below_the_gateway_ceiling_is_accepted()
    {
        var wire = new RecordingObserver();
        var client = LiveTenant.CreateClient(o =>
        {
            // The request intentionally exceeds the package's conservative 8,000-character
            // default. Raise the guard so this fact can measure the gateway boundary itself.
            o.QueryStringLengthWarningThreshold = 1;
            o.MaxQueryStringLength = 50_000;
        }, wire);

        // One fewer fixed-width key than the rejected probe. Together the two requests form the
        // narrowest interval this real filter shape can express around the gateway boundary.
        var rows = await client.Query<LdatSummaryRow>()
            .Where(Filter.In<LdatSummaryRow>(
                x => x.ProductionOrderNo,
                OverLongKeySet(LastAcceptedKeyCount)))
            .ToListAsync();

        var warning = Assert.Single(wire.LengthWarnings);

        output.WriteLine(
            $"accepted: queryString={warning.QueryStringLength} url={warning.UrlLength} " +
            $"orClauses={warning.OrClauseCount} rows={rows.Count}");

        // The band this fact exists to hold open: above the package's conservative default, but
        // immediately below the measured gateway ceiling, sent and served.
        Assert.InRange(warning.QueryStringLength, 8_001, 8_099);
        Assert.False(warning.ExceedsLimit);
    }

    /// <summary>
    /// Keys shaped like real production-order numbers, none of which match anything.
    /// </summary>
    /// <remarks>
    /// <b>19 characters, and that is a constraint, not a coincidence.</b> <c>productionOrderNo</c>
    /// is a <c>Code[20]</c>, and Business Central validates the field's length before it evaluates
    /// the filter: a 23-character key is answered <c>400</c> naming the field and the limit, not
    /// <c>414</c>. Only the over-length fact escapes that, because the gateway refuses the request
    /// before any AL code sees it — which is itself a useful ordering to know.
    /// </remarks>
    private static object[] OverLongKeySet(int count) =>
        Enumerable.Range(0, count).Select(i => (object)$"PO-{i:D6}-UNMATCHED").ToArray();
}
