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
/// </remarks>
public sealed class DateFilterTests(ITestOutputHelper output)
{
    /// <summary>
    /// A kindless <see cref="DateTime"/> selects exactly the rows an explicitly-UTC one does.
    /// </summary>
    /// <remarks>
    /// The boundary is read from the tenant rather than hard-coded, so the fact keeps discriminating
    /// as the data moves: a fixed date would eventually fall outside the data and compare two
    /// identical full-set counts, passing while testing nothing. The assertion that both counts sit
    /// strictly between zero and the total is what enforces that.
    /// </remarks>
    [LiveTenantFact]
    public async Task Kindless_DateTime_selects_the_same_rows_as_an_explicitly_utc_one()
    {
        var client = LiveTenant.CreateClient();

        var boundary = await FindBoundaryAsync(client);
        var kindless = DateTime.SpecifyKind(boundary, DateTimeKind.Unspecified);
        var utc = DateTime.SpecifyKind(boundary, DateTimeKind.Utc);

        var total = await client.Query<ProdOrderLine>().CountAsync();
        var kindlessCount = await CountFromAsync(client, kindless);
        var utcCount = await CountFromAsync(client, utc);

        output.WriteLine($"boundary={boundary:O} total={total} kindless={kindlessCount} utc={utcCount}");

        Assert.Equal(utcCount, kindlessCount);
        Assert.InRange(kindlessCount, 1, total - 1);
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
    /// cannot exist. CI runners are usually UTC; a developer machine in Vienna is where this one
    /// actually bites, which is exactly the asymmetry that let the 1.0 bug survive.
    /// </para>
    /// </remarks>
    [LiveTenantFact]
    public async Task The_old_machine_local_interpretation_would_have_shifted_the_result_set()
    {
        var client = LiveTenant.CreateClient();

        var boundary = await FindBoundaryAsync(client);
        var offset = TimeZoneInfo.Local.GetUtcOffset(boundary);

        if (offset == TimeSpan.Zero)
        {
            output.WriteLine(
                "Machine timezone is UTC, so the 1.0 and 2.0 interpretations coincide and there " +
                "is no shift to measure. Not a failure — see the remarks.");
            return;
        }

        var kindless = DateTime.SpecifyKind(boundary, DateTimeKind.Unspecified);   // 2.0: already UTC
        var asLocal = DateTime.SpecifyKind(boundary, DateTimeKind.Local);          // 1.0: shifted

        var kindlessCount = await CountFromAsync(client, kindless);
        var localCount = await CountFromAsync(client, asLocal);

        // The two instants differ by the machine's offset. Count the rows that fall in that gap
        // directly, so the difference is explained rather than merely observed.
        var earlier = asLocal.ToUniversalTime() < kindless ? asLocal.ToUniversalTime() : kindless;
        var later = asLocal.ToUniversalTime() < kindless ? kindless : asLocal.ToUniversalTime();

        var gap = await client.Query<ProdOrderLine>()
            .Where(Filter.GreaterOrEqual<ProdOrderLine>(x => x.EndingDateTime, DateTime.SpecifyKind(earlier, DateTimeKind.Utc))
                .And(Filter.LessThan<ProdOrderLine>(x => x.EndingDateTime, DateTime.SpecifyKind(later, DateTimeKind.Utc))))
            .CountAsync();

        output.WriteLine(
            $"offset={offset} kindless(2.0)={kindlessCount} local(1.0)={localCount} gap={gap} rows");

        Assert.Equal(gap, Math.Abs(kindlessCount - localCount));

        if (gap == 0)
            output.WriteLine("No rows fall in the gap at this boundary, so the two agree by luck, not by design.");
        else
            Assert.NotEqual(kindlessCount, localCount);
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

        Assert.NotNull(row);

        return row!.EndingDateTime.UtcDateTime;
    }
}
