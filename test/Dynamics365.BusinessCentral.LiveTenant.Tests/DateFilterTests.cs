using Dynamics365.BusinessCentral.OData;
using Xunit.Abstractions;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// The <see cref="DateTimeKind.Unspecified"/> filter change, checked against rows rather than
/// against a rendered string.
/// </summary>
/// <remarks>
/// <para>
/// 1.0 ran every <see cref="DateTime"/> through <see cref="DateTime.ToUniversalTime"/>. For a
/// kindless value — anything parsed from configuration or loaded from a database — .NET reads
/// that as machine-local and shifts it. The same filter object therefore selected different rows
/// depending on the container's timezone. 2.0 treats a kindless value as already UTC.
/// </para>
/// <para>
/// The unit suite pins the rendered literal. What it cannot show is that the shift <b>changes
/// which rows come back</b>, which is the only reason the change matters. That needs a tenant
/// with real timestamps, and it is what these facts measure.
/// </para>
/// <para>
/// Every comparison here spans several counts of a collection the tenant is still writing to, so
/// each one is bracketed rather than compared exactly — see <see cref="LiveTenantAssert"/>. The
/// bracket is the same instrument the paging facts use, for the same reason: an insertion between
/// two requests would otherwise fail the fact in exactly the shape of the regression it exists to
/// catch.
/// </para>
/// </remarks>
public sealed class DateFilterTests(ITestOutputHelper output)
{
    /// <summary>
    /// A kindless <see cref="DateTime"/> selects exactly the rows an explicitly-UTC one does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The boundary is read from the tenant rather than hard-coded, so the fact keeps discriminating
    /// as the data moves: a fixed date would eventually fall outside the data and compare two
    /// identical full-set counts, passing while testing nothing. The assertion that both counts sit
    /// strictly between zero and the total is what enforces that.
    /// </para>
    /// <para>
    /// The kindless count is taken either side of the UTC one, so the two renderings are compared
    /// across a measured window rather than assumed to have been taken at the same instant.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task Kindless_DateTime_selects_the_same_rows_as_an_explicitly_utc_one()
    {
        var client = LiveTenant.CreateClient();

        var boundary = await FindBoundaryAsync(client);

        RequireADiscriminatingClock(TimeZoneInfo.Local.GetUtcOffset(boundary));

        var kindless = DateTime.SpecifyKind(boundary, DateTimeKind.Unspecified);
        var utc = DateTime.SpecifyKind(boundary, DateTimeKind.Utc);

        var total = await client.Query<ProdOrderLine>().CountAsync();

        var kindlessBefore = await CountFromAsync(client, kindless);
        var utcCount = await CountFromAsync(client, utc);
        var kindlessAfter = await CountFromAsync(client, kindless);

        output.WriteLine($"boundary={boundary:O} total={total} " +
                         $"kindless={kindlessBefore}..{kindlessAfter} utc={utcCount}");

        LiveTenantAssert.WithinBracket(
            kindlessBefore, kindlessAfter, utcCount,
            "An explicitly-UTC filter against the kindless one measured either side of it");

        // Not the full set and not empty: a boundary that stopped discriminating would make the
        // comparison above true of two identical numbers and prove nothing.
        Assert.InRange(utcCount, 1, total - 1);
    }

    /// <summary>
    /// The 1.0 interpretation would have selected a measurably different set — by exactly the rows
    /// in the timezone gap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fact that gives the migration note its teeth. It states the size of the error a
    /// consumer inherits if they were relying on the old behaviour, in rows, on real data.
    /// </para>
    /// <para>
    /// It is inert on a machine running UTC — where local and UTC are the same instant and there
    /// is nothing to measure — so it reports and returns rather than asserting a difference that
    /// cannot exist. The scheduled workflow fixes <c>TZ=Europe/Vienna</c> so its run always
    /// discriminates; the early return remains useful for an ordinary local run under UTC.
    /// </para>
    /// <para>
    /// The identity being checked is <c>local == kindless ± gap</c>: plus when the local
    /// interpretation moves the boundary earlier, minus when it moves it later. It spans
    /// <b>two</b> populations that must be measured separately: the rows above the boundary, and
    /// the rows inside the timezone window. Both are therefore read either side of the reading
    /// under test. Under the workflow's positive offset, bracketing only the first would leave the
    /// fact blind in a specific and unlucky way — a row inserted or deleted inside the window
    /// changes the gap and the machine-local count while every <c>&gt;= boundary</c> count stays put,
    /// so the tolerance would measure zero at exactly the moment it was needed, and the fact would
    /// fail with the conversion working correctly.
    /// </para>
    /// <para>
    /// The tolerance is the sum of what each population was seen to move, and nothing else. A
    /// sample where that movement outweighs the gap is <b>inconclusive, and fails</b>: the identity
    /// still held, but restoring the 1.0 conversion would not have failed such a run, and a fact
    /// that cannot fail must not report the same green as one that can.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task The_old_machine_local_interpretation_would_have_shifted_the_result_set()
    {
        var client = LiveTenant.CreateClient();

        var boundary = await FindBoundaryAsync(client);
        var offset = TimeZoneInfo.Local.GetUtcOffset(boundary);

        RequireADiscriminatingClock(offset);

        if (offset == TimeSpan.Zero)
        {
            output.WriteLine(
                "Machine timezone is UTC, so the 1.0 and 2.0 interpretations coincide and there " +
                "is no shift to measure. Not a failure — see the remarks.");
            return;
        }

        var kindless = DateTime.SpecifyKind(boundary, DateTimeKind.Unspecified);   // 2.0: already UTC
        var asLocal = DateTime.SpecifyKind(boundary, DateTimeKind.Local);          // 1.0: shifted

        // The two instants differ by the machine's offset. The rows between them are what the 1.0
        // reading additionally selects in a positive-offset zone and excludes in a negative one.
        var kindlessInstant = DateTime.SpecifyKind(kindless, DateTimeKind.Utc);
        var localInstant = asLocal.ToUniversalTime();
        var localIsEarlier = localInstant < kindlessInstant;
        var earlier = localIsEarlier ? localInstant : kindlessInstant;
        var later = localIsEarlier ? kindlessInstant : localInstant;

        // Both populations are read either side of the reading under test. Under the workflow's
        // positive offset, a row inside the timezone window changes the gap while leaving every
        // count above the boundary untouched.
        var kindlessBefore = await CountFromAsync(client, kindless);
        var gapBefore = await CountBetweenAsync(client, earlier, later);

        var localCount = await CountFromAsync(client, asLocal);

        var gapAfter = await CountBetweenAsync(client, earlier, later);
        var kindlessAfter = await CountFromAsync(client, kindless);

        // In a positive-offset zone the local boundary is earlier: local == kindless + gap. In a
        // negative-offset zone it is later: local == kindless - gap. Pairing the independent
        // minima/maxima this way prevents movement in the two populations from cancelling out.
        var kindlessLow = Math.Min(kindlessBefore, kindlessAfter);
        var kindlessHigh = Math.Max(kindlessBefore, kindlessAfter);
        var gapLow = Math.Min(gapBefore, gapAfter);
        var gapHigh = Math.Max(gapBefore, gapAfter);
        var low = localIsEarlier ? kindlessLow + gapLow : kindlessLow - gapHigh;
        var high = localIsEarlier ? kindlessHigh + gapHigh : kindlessHigh - gapLow;
        var relationship = localIsEarlier ? "kindless + gap" : "kindless - gap";

        output.WriteLine(
            $"offset={offset} kindless(2.0)={kindlessBefore}..{kindlessAfter} " +
            $"gap={gapBefore}..{gapAfter} rows ({relationship}) → " +
            $"local(1.0) expected {low}..{high}, got {localCount}");

        LiveTenantAssert.WithinBracket(
            low, high, localCount,
            "The 1.0 machine-local reading, against the 2.0 reading adjusted by the timezone gap");

        // The two readings are provably apart, in either direction, exactly when the smallest the
        // gap was seen to be outweighs how far the kindless population moved. Below that the
        // identity above still held, but the run cannot say the 1.0 conversion would have selected
        // anything different — and an inconclusive sample must not report the same green as a
        // conclusive one, or the fact stops being able to fail while still passing.
        var gapFloor = gapLow;
        var kindlessDrift = Math.Abs(kindlessAfter - kindlessBefore);

        Assert.True(
            gapFloor > kindlessDrift,
            $"Inconclusive: the timezone window held {gapFloor} row(s), which does not outweigh " +
            $"the {kindlessDrift} that moved above the boundary while measuring, so restoring the " +
            "1.0 conversion would not have failed this run. FindBoundaryAsync places the boundary " +
            "so the window is never empty, and this suite runs against a tenant quiet enough for " +
            "the drift to be zero — if that has stopped being true, this fact needs a denser " +
            "sample, not a softer assertion.");

        Assert.NotEqual(kindlessBefore, localCount);
    }

    /// <summary>
    /// Fails a workflow run whose clock cannot tell the two interpretations apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under a UTC clock, <see cref="DateTime.ToUniversalTime"/> leaves a kindless value alone, so
    /// the 1.0 and 2.0 readings are the same instant and <b>both</b> facts in this class pass
    /// whichever one the package implements. That is fine on a contributor's machine — nothing is
    /// claimed and nothing is broken — and unacceptable in the scheduled run, which exists to fail
    /// if the conversion comes back.
    /// </para>
    /// <para>
    /// The workflow sets <c>TZ</c> for exactly this reason, which makes the guarantee one deleted
    /// YAML line thick. This turns that deletion into a failure naming the cause, rather than a
    /// green run that quietly stopped measuring — the same reason the credential check exists
    /// beside a suite that skips.
    /// </para>
    /// </remarks>
    private static void RequireADiscriminatingClock(TimeSpan offset)
    {
        Assert.False(
            offset == TimeSpan.Zero && LiveTenant.IsGitHubActions,
            "This run's clock is UTC, so the 1.0 machine-local conversion and the 2.0 reading are " +
            "the same instant and neither fact in this class can fail. The workflow must run these " +
            "under a non-UTC zone — see TZ in .github/workflows/live-tenant.yml.");
    }

    /// <summary>Rows inside the timezone window, <c>[from, to)</c>.</summary>
    /// <remarks>
    /// A population of its own, and the reason this fact reads five counts rather than three: rows
    /// Under the workflow's positive-offset clock these rows are <i>below</i> the kindless boundary,
    /// so they never touch a <c>&gt;= boundary</c> count. A tolerance derived only from those counts
    /// is blind to exactly the activity that would break the identity being checked. Negative
    /// offsets put the window above the boundary; the caller handles that by subtracting it.
    /// </remarks>
    private static Task<long> CountBetweenAsync(
        Client.IBusinessCentralClient client,
        DateTime from,
        DateTime to) =>
        client.Query<ProdOrderLine>()
            .Where(Filter.GreaterOrEqual<ProdOrderLine>(
                    x => x.EndingDateTime, DateTime.SpecifyKind(from, DateTimeKind.Utc))
                .And(Filter.LessThan<ProdOrderLine>(
                    x => x.EndingDateTime, DateTime.SpecifyKind(to, DateTimeKind.Utc))))
            .CountAsync();

    private static Task<long> CountFromAsync(Client.IBusinessCentralClient client, DateTime from) =>
        client.Query<ProdOrderLine>()
            .Where(Filter.GreaterOrEqual<ProdOrderLine>(x => x.EndingDateTime, from))
            .CountAsync();

    /// <summary>
    /// Picks a boundary from the middle of the data whose timezone window is <b>provably</b>
    /// non-empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two properties are needed, and only the first is about being mid-data. A boundary matching
    /// nothing on one side would compare two identical full-set counts and pass while testing
    /// nothing, so it is taken from a row well inside the ordered set.
    /// </para>
    /// <para>
    /// The second is what makes both facts in this class able to fail at all. Everything they
    /// detect is the rows the machine-local shift moves across the boundary — the timezone window
    /// — so a window that happens to contain no rows makes the 1.0 and 2.0 readings identical and
    /// the whole class passes with the conversion restored. Sampling a row and hoping its window is
    /// populated leaves that to the data: the margin on this tenant has been a single row.
    /// </para>
    /// <para>
    /// So the boundary is <i>placed</i> rather than sampled. The window is
    /// <c>[boundary - offset, boundary)</c> when the local reading lands earlier and
    /// <c>[boundary, boundary + |offset|)</c> when it lands later; offsetting the sampled row's
    /// timestamp by whichever direction applies puts that row inside the window by construction.
    /// One row is the floor, not the expectation — the window is usually far more populated — but
    /// the floor is what stops the suite ever going quietly non-discriminating.
    /// </para>
    /// </remarks>
    private static async Task<DateTime> FindBoundaryAsync(Client.IBusinessCentralClient client)
    {
        // Skip past the sentinel rows (BC's "unset" is 0001-01-01) to a real timestamp.
        var row = await client.Query<ProdOrderLine>()
            .Where(Filter.GreaterThan<ProdOrderLine>(
                x => x.EndingDateTime,
                new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .OrderBy(x => x.EndingDateTime)
            .Skip(1000)
            .Top(1)
            .FirstOrDefaultAsync();

        Assert.True(
            row is not null,
            "No row with a real timestamp past the first 1,000. This fact needs a datetime column " +
            "with spread-out values; run TenantShapeTests and pick another set rather than " +
            "lowering the skip.");

        var sampled = row!.EndingDateTime.UtcDateTime;
        var offset = TimeZoneInfo.Local.GetUtcOffset(sampled);

        // Place the boundary so the sampled row falls inside the timezone window. Ahead of the row
        // when the local reading lands earlier, on it when the local reading lands later.
        return offset > TimeSpan.Zero ? sampled + offset : sampled;
    }
}
