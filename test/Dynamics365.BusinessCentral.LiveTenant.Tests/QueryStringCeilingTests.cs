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
        var client = LiveTenant.CreateClient(o =>
        {
            // Out of the way: this fact is about the gateway, not the guard.
            o.QueryStringLengthWarningThreshold = 40_000;
            o.MaxQueryStringLength = 50_000;
        });

        var exception = await Assert.ThrowsAnyAsync<BusinessCentralException>(() =>
            client.Query<LdatSummaryRow>()
                .Where(Filter.In<LdatSummaryRow>(x => x.ProductionOrderNo, OverLongKeySet(600)))
                .ToListAsync());

        output.WriteLine($"over-length query string → {(int)exception.StatusCode} {exception.StatusCode}");

        Assert.Equal(414, (int)exception.StatusCode);
    }

    /// <summary>
    /// A query string just under the package's own refusal threshold is still accepted by the
    /// gateway.
    /// </summary>
    /// <remarks>
    /// The other side of the same measurement, and the one that would catch the guard becoming
    /// wrong in the expensive direction: a default that refused requests Business Central would
    /// happily have served.
    /// </remarks>
    [LiveTenantFact]
    public async Task A_query_string_just_under_the_default_ceiling_is_accepted()
    {
        var warnings = new LengthWarningRecorder();
        var client = LiveTenant.CreateClient(observer: warnings);

        // 100 keys. Each or-clause costs roughly 65 encoded characters at this field-name and key
        // length — the field name is repeated per clause and every quote becomes %27 — so this
        // lands near 6,600: over the 6,000 warning threshold, under the 8,000 refusal, and under
        // the 8,099 the gateway itself enforces. That per-clause cost is why Filter.In answers a
        // bulk lookup with chunking advice rather than a bigger limit.
        var rows = await client.Query<LdatSummaryRow>()
            .Where(Filter.In<LdatSummaryRow>(x => x.ProductionOrderNo, OverLongKeySet(100)))
            .ToListAsync();

        var warning = Assert.Single(warnings.Observed);

        output.WriteLine(
            $"accepted: queryString={warning.QueryStringLength} url={warning.UrlLength} " +
            $"orClauses={warning.OrClauseCount} rows={rows.Count}");

        // The band this fact exists to hold open: warned about, sent anyway, served.
        Assert.InRange(warning.QueryStringLength, 6_000, 8_000);
        Assert.False(warning.ExceedsLimit);
    }

    /// <summary>Captures <c>OnUrlLengthWarning</c> so the fact can assert on a measured length.</summary>
    private sealed class LengthWarningRecorder : Diagnostics.IBusinessCentralObserver
    {
        public List<Diagnostics.BusinessCentralUrlLengthInfo> Observed { get; } = [];

        public void OnUrlLengthWarning(Diagnostics.BusinessCentralUrlLengthInfo url) => Observed.Add(url);

        public void OnRequestStarting(Diagnostics.BusinessCentralRequestInfo request) { }
        public void OnRequestSucceeded(Diagnostics.BusinessCentralRequestInfo request) { }
        public void OnRequestFailed(Diagnostics.BusinessCentralErrorInfo error) { }
        public void OnTokenRequested() { }
        public void OnTokenRefreshed(Diagnostics.BusinessCentralTokenInfo token) { }
        public void OnDeserializationFailed(Diagnostics.BusinessCentralErrorInfo error) { }
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
