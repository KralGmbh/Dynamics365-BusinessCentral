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
}
