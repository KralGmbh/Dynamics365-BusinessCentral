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

    [Fact]
    public void DateOnly_Works_In_In_Filters()
    {
        var filter = Filter.In("postingDate", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2));

        Assert.Equal("postingDate in (2026-07-01,2026-07-02)", filter.Value);
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
}
