using System.Text.Json;
using Xunit.Abstractions;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// Reports the shape of the sandbox tenant: which published pages honour <c>$count</c>, how many
/// rows they hold, and which of their columns are dates.
/// </summary>
/// <remarks>
/// This exists because the other facts in this suite need those numbers to be chosen honestly.
/// A paging fact needs a collection genuinely larger than one page; a date fact needs a column
/// that is genuinely a date. Guessing either produces a test that passes without testing
/// anything. Run this, read the output, then write the assertion.
/// </remarks>
public sealed class TenantShapeTests(ITestOutputHelper output)
{
    /// <summary>
    /// Candidate entity sets, smallest column count first. These are published pages on the
    /// sandbox; the set is deliberately short, because each name is one round trip.
    /// </summary>
    private static readonly string[] Candidates =
    [
        "LdatSummary",
        "LDATItemAttributes",
        "LDATItemCategory",
        "LDATProductionOrder",
        "LDATProdOrderLine",
        "LDATProdOrderComp",
        "LDATReservationEntries",
        "LDATSalesLine",
        "LDATItems",
    ];

    [LiveTenantFact]
    public async Task Report_row_counts_and_date_columns()
    {
        var client = LiveTenant.CreateClient();
        var honoursCount = 0;

        foreach (var set in Candidates)
        {
            try
            {
                // $count=true with a single row: one round trip answers both "how many" and
                // "which columns", and whether this page honours $count at all.
                var response = await client.QueryRawAsync<JsonElement>($"{set}?$count=true&$top=1");

                var honoured = response.TryGetProperty("@odata.count", out var countElement);
                var count = honoured ? countElement.GetRawText() : "(not honoured)";

                var rows = response.GetProperty("value");
                var columns = rows.GetArrayLength() > 0
                    ? rows[0].EnumerateObject().Select(p => p.Name).ToArray()
                    : [];

                var dateColumns = columns
                    .Where(c => c.Contains("date", StringComparison.OrdinalIgnoreCase)
                             || c.Contains("time", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                output.WriteLine($"{set}: count={count} columns={columns.Length}");
                output.WriteLine($"    dates: {(dateColumns.Length == 0 ? "(none)" : string.Join(", ", dateColumns))}");

                if (rows.GetArrayLength() > 0 && dateColumns.Length > 0)
                {
                    var samples = dateColumns.Take(4)
                        .Select(c => $"{c}={rows[0].GetProperty(c).GetRawText()}");
                    output.WriteLine($"    sample: {string.Join(" ", samples)}");
                }

                // A count only qualifies after the rest of the response shape has proved usable.
                // Otherwise the catch below can suppress a malformed value while leaving this
                // candidate counted as a success.
                if (honoured)
                    honoursCount++;
            }
            catch (Exception ex)
            {
                output.WriteLine($"{set}: FAILED — {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Not a formality. CountAsync falls back to streaming and counting the whole set when the
        // endpoint ignores $count, and on a six-figure entity set that fallback is invisible and
        // very expensive. Several facts in this suite call CountAsync against these pages; this is
        // what tells us they cost one round trip rather than fifty.
        Assert.Equal(Candidates.Length, honoursCount);
    }

    /// <summary>
    /// Prints the columns of the three sets the rest of the suite is written against, and requires
    /// each of them to actually answer with a row.
    /// </summary>
    /// <remarks>
    /// The report is the purpose; the assertion is what stops it becoming decoration. Every other
    /// fact here reads one of these three sets, so an empty one is a precondition failure that
    /// should be named here rather than surfacing as a confusing assertion somewhere downstream.
    /// </remarks>
    [LiveTenantFact]
    public async Task Report_columns_of_the_sets_the_other_facts_use()
    {
        var client = LiveTenant.CreateClient();

        foreach (var set in new[] { "LdatSummary", "LDATProdOrderLine", "LDATSalesLine" })
        {
            var response = await client.QueryRawAsync<JsonElement>($"{set}?$top=1");
            var rows = response.GetProperty("value");

            Assert.True(
                rows.GetArrayLength() > 0,
                $"{set} answered no rows. Facts elsewhere in this suite are written against it, " +
                "so they are about to fail for a reason that has nothing to do with the package.");

            var columns = rows[0].EnumerateObject().Select(p => p.Name).ToArray();

            Assert.NotEmpty(columns);

            output.WriteLine($"--- {set} ({columns.Length}) ---");
            output.WriteLine(string.Join(", ", columns.Take(60)));
            output.WriteLine("");
        }
    }
}
