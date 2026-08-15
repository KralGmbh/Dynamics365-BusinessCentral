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
    /// Four counts, so the identity being checked — that the difference between the two readings
    /// <i>is</i> the rows in the gap — is allowed the drift the tenant caused while they were
    /// taken, and no more. When the gap is smaller than that drift the fact says so and stops
    /// rather than asserting on noise.
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

        var kindlessBefore = await CountFromAsync(client, kindless);
        var localCount = await CountFromAsync(client, asLocal);

        // The two instants differ by the machine's offset. Count the rows that fall in that gap
        // directly, so the difference is explained rather than merely observed.
        var earlier = asLocal.ToUniversalTime() < kindless ? asLocal.ToUniversalTime() : kindless;
        var later = asLocal.ToUniversalTime() < kindless ? kindless : asLocal.ToUniversalTime();

        var gap = await client.Query<ProdOrderLine>()
            .Where(Filter.GreaterOrEqual<ProdOrderLine>(x => x.EndingDateTime, DateTime.SpecifyKind(earlier, DateTimeKind.Utc))
                .And(Filter.LessThan<ProdOrderLine>(x => x.EndingDateTime, DateTime.SpecifyKind(later, DateTimeKind.Utc))))
            .CountAsync();

        var kindlessAfter = await CountFromAsync(client, kindless);

        // How much the tenant moved under the four reads. It is the only tolerance this fact
        // grants, and it is measured rather than chosen.
        var drift = Math.Abs(kindlessAfter - kindlessBefore);
        var difference = Math.Abs(kindlessBefore - localCount);

        output.WriteLine(
            $"offset={offset} kindless(2.0)={kindlessBefore}..{kindlessAfter} local(1.0)={localCount} " +
            $"difference={difference} gap={gap} rows drift={drift}");

        LiveTenantAssert.WithinBracket(
            gap - drift, gap + drift, difference,
            "The rows the 1.0 reading would have moved, against the rows in the timezone gap");

        if (gap <= drift)
            output.WriteLine(
                $"The gap ({gap} rows) is within the drift the tenant caused while measuring " +
                $"({drift}), so this run cannot say the two readings differ — only that the " +
                "difference is explained. Not a failure; the identity above still held.");
        else
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

    private static Task<long> CountFromAsync(Client.IBusinessCentralClient client, DateTime from) =>
        client.Query<ProdOrderLine>()
            .Where(Filter.GreaterOrEqual<ProdOrderLine>(x => x.EndingDateTime, from))
            .CountAsync();

    /// <summary>
    /// Picks a boundary from the middle of the data: the timestamp of a row roughly in the middle
    /// of the set when ordered by time, so filters on either side of it match a useful number of
    /// rows.
    /// </summary>
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

        return row!.EndingDateTime.UtcDateTime;
    }
}
