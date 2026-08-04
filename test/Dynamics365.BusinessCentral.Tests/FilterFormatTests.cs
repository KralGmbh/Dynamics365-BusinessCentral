using Dynamics365.BusinessCentral.OData;

namespace Dynamics365.BusinessCentral.Tests;

public class FilterFormatTests
{
    // Business Central date fields (postingDate, documentDate, …) are Edm.Date. Before
    // these types were handled, they fell through to Convert.ToString and produced a
    // culture-formatted literal ("07/29/2026") that the server rejects with a 400.
    [Fact]
    public void DateOnly_Is_Formatted_As_An_OData_Date_Literal()
    {
        var filter = Filter.Equals("postingDate", new DateOnly(2026, 7, 29));

        Assert.Equal("postingDate eq 2026-07-29", filter.Value);
    }

    [Fact]
    public void TimeOnly_Is_Formatted_As_An_OData_TimeOfDay_Literal()
    {
        var filter = Filter.Equals("startTime", new TimeOnly(13, 45, 30));

        Assert.Equal("startTime eq 13:45:30.0000000", filter.Value);
    }

    [Fact]
    public void DateOnly_Works_In_Range_Filters()
    {
        var filter = Filter.GreaterOrEqual("postingDate", new DateOnly(2026, 1, 1))
            .And(Filter.LessThan("postingDate", new DateOnly(2027, 1, 1)));

        Assert.Equal("(postingDate ge 2026-01-01) and (postingDate lt 2027-01-01)", filter.Value);
    }

    // Filter.In renders a same-field or-chain, NOT the OData `in` operator: Business
    // Central rejects `in` without $schemaversion=2.1 (BadRequest_MethodNotImplemented,
    // verified against a live tenant), while same-field `or` works everywhere.
    [Fact]
    public void In_Renders_A_Same_Field_Or_Chain()
    {
        var filter = Filter.In("status", "active", "pending");

        Assert.Equal("(status eq 'active') or (status eq 'pending')", filter.Value);
    }

    [Fact]
    public void In_With_A_Single_Value_Collapses_To_Eq()
    {
        var filter = Filter.In("status", "active");

        Assert.Equal("status eq 'active'", filter.Value);
    }

    [Fact]
    public void In_With_No_Values_Matches_Nothing()
    {
        Assert.Equal("false", Filter.In("status").Value);
        Assert.Equal("false", Filter.In("status", Enumerable.Empty<object>()).Value);
    }

    [Fact]
    public void DateOnly_Works_In_In_Filters()
    {
        var filter = Filter.In("postingDate", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2));

        Assert.Equal("(postingDate eq 2026-07-01) or (postingDate eq 2026-07-02)", filter.Value);
    }

    // A kindless DateTime (parsed from config, loaded from a database) used to be run
    // through ToUniversalTime, which assumes local — so the literal depended on the
    // machine's timezone. It is now taken to already be UTC.
    [Fact]
    public void Unspecified_DateTime_Is_Treated_As_Utc_Not_Shifted()
    {
        var value = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Unspecified);

        var filter = Filter.GreaterOrEqual("lastModifiedDateTime", value);

        Assert.Equal("lastModifiedDateTime ge 2026-07-29T12:00:00.0000000Z", filter.Value);
    }

    [Fact]
    public void Utc_DateTime_Is_Formatted_Unchanged()
    {
        var value = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

        var filter = Filter.Equals("lastModifiedDateTime", value);

        Assert.Equal("lastModifiedDateTime eq 2026-07-29T12:00:00.0000000Z", filter.Value);
    }

    [Fact]
    public void Local_DateTime_Is_Still_Converted_To_Utc()
    {
        var value = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Local);

        var filter = Filter.Equals("lastModifiedDateTime", value);

        Assert.Equal($"lastModifiedDateTime eq {value.ToUniversalTime():O}", filter.Value);
    }

    // OData escapes a single quote inside a string literal by doubling it. This is the
    // highest-risk escaping rule in the filter surface — get it wrong and a customer named
    // O'Brien terminates the literal early, producing a filter the server rejects or, worse,
    // one it misreads.
    [Fact]
    public void Single_Quote_In_A_String_Is_Doubled()
    {
        var filter = Filter.Equals("name", "O'Brien");

        Assert.Equal("name eq 'O''Brien'", filter.Value);
    }

    [Fact]
    public void Every_Single_Quote_Is_Doubled_Not_Just_The_First()
    {
        var filter = Filter.Equals("name", "O'Brien's 'shop'");

        Assert.Equal("name eq 'O''Brien''s ''shop'''", filter.Value);
    }

    [Fact]
    public void Quote_Doubling_Survives_The_Whole_Pipeline_To_The_Wire()
    {
        var filter = Filter.In("name", "O'Brien", "D'Angelo");

        Assert.Equal("(name eq 'O''Brien') or (name eq 'D''Angelo')", filter.Value);
    }

    // README tells readers to filter on lastModifiedDateTime, which is Edm.DateTimeOffset.
    // The "O" round-trip format is the OData literal; the offset must be normalised to UTC
    // so the same instant filters identically regardless of the caller's offset.
    [Fact]
    public void DateTimeOffset_Is_Formatted_As_A_Utc_Literal()
    {
        var value = new DateTimeOffset(2026, 7, 29, 14, 0, 0, TimeSpan.FromHours(2));

        var filter = Filter.GreaterThan("lastModifiedDateTime", value);

        Assert.Equal("lastModifiedDateTime gt 2026-07-29T12:00:00.0000000+00:00", filter.Value);
    }

    [Fact]
    public void DateTimeOffset_Already_Utc_Is_Unchanged()
    {
        var value = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

        var filter = Filter.Equals("lastModifiedDateTime", value);

        Assert.Equal("lastModifiedDateTime eq 2026-07-29T12:00:00.0000000+00:00", filter.Value);
    }

    // OData v4 dropped the v3 guid'...' prefix: an Edm.Guid literal is bare and unquoted.
    // systemId eq <guid> is the most common key filter there is.
    [Fact]
    public void Guid_Is_Formatted_Unquoted()
    {
        var id = Guid.Parse("2f1b8c4e-9d3a-4f56-8b21-7c0e5a9d1234");

        var filter = Filter.Equals("systemId", id);

        Assert.Equal("systemId eq 2f1b8c4e-9d3a-4f56-8b21-7c0e5a9d1234", filter.Value);
    }

    private enum OrderStatus
    {
        Open,
        Released
    }

    // An enum renders as its C# member name, quoted — so it only matches a Business Central
    // option field whose option strings happen to equal the member names. BC option values
    // routinely contain spaces, which no member name can spell; this pins the rule so the
    // limitation stays visible.
    [Fact]
    public void Enum_Is_Formatted_As_Its_Quoted_Member_Name()
    {
        var filter = Filter.Equals("status", OrderStatus.Released);

        Assert.Equal("status eq 'Released'", filter.Value);
    }

    [Fact]
    public void Bool_Is_Formatted_Lowercase()
    {
        Assert.Equal("blocked eq true", Filter.Equals("blocked", true).Value);
        Assert.Equal("blocked eq false", Filter.Equals("blocked", false).Value);
    }

    [Fact]
    public void Null_Is_Formatted_As_The_Null_Literal()
    {
        Assert.Equal("description eq null", Filter.Equals("description", null).Value);
    }

    // A collection has no scalar literal. It used to fall through to Convert.ToString and
    // produce "no eq System.Object[]", which the URL builder encodes and sends. Filter.In is
    // right there, so this is a plausible slip and worth naming rather than shipping.
    [Fact]
    public void Passing_A_Collection_To_A_Scalar_Comparison_Throws_And_Names_Filter_In()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Filter.Equals("no", new[] { "A", "B" }));

        Assert.Contains("Filter.In", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("System.String[]", Filter.In("no", "A", "B").Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_String_Is_Not_Mistaken_For_A_Collection()
    {
        // string is IEnumerable<char>; the guard must exempt it.
        Assert.Equal("no eq 'ABC'", Filter.Equals("no", "ABC").Value);
    }
}
