using Dynamics365.BusinessCentral.Errors;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// The exception subtypes are sealed siblings, so status-code guards on the wrong subtype
/// silently never match. The predicates on the base type are the supported alternative;
/// this pins their truth table.
/// </summary>
public class ExceptionPredicateTests
{
    [Fact]
    public void NotFound_Sets_Only_IsNotFound()
    {
        var ex = new BusinessCentralNotFoundException("x", HttpStatusCode.NotFound, "GET", null, null);

        Assert.True(ex.IsNotFound);
        Assert.False(ex.IsThrottled);
        Assert.False(ex.IsValidation);
        Assert.False(ex.IsAuth);
        Assert.False(ex.IsConnectionFailure);
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void Throttled_Sets_IsThrottled_And_IsTransient()
    {
        var ex = new BusinessCentralThrottledException("x", HttpStatusCode.TooManyRequests, "GET", null, null);

        Assert.True(ex.IsThrottled);
        Assert.True(ex.IsTransient);
        Assert.False(ex.IsNotFound);
    }

    [Fact]
    public void Validation_Sets_IsValidation()
    {
        var ex = new BusinessCentralValidationException("x", HttpStatusCode.BadRequest, "POST", null, null);

        Assert.True(ex.IsValidation);
        Assert.False(ex.IsTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Auth_Failures_Set_IsAuth(HttpStatusCode status)
    {
        var ex = new BusinessCentralAuthException("x", status, "GET", null, null);

        Assert.True(ex.IsAuth);
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void Connection_Failure_Sets_IsConnectionFailure_And_IsTransient()
    {
        var ex = new BusinessCentralConnectionException("x", "GET", null, new HttpRequestException());

        Assert.True(ex.IsConnectionFailure);
        Assert.True(ex.IsTransient);
        Assert.False(ex.IsNotFound);
        Assert.Contains("Status: (no response received)", ex.ToString());
    }

    [Fact]
    public void Protocol_Violation_Uses_A_Neutral_Status_Description()
    {
        var ex = new BusinessCentralProtocolException("x", "https://example.test/next");

        Assert.True(ex.IsProtocolViolation);
        Assert.False(ex.IsConnectionFailure);
        Assert.False(ex.IsTransient);
        Assert.Contains("Status: (no HTTP status associated)", ex.ToString());
        Assert.DoesNotContain("no response received", ex.ToString());
    }

    [Fact]
    public void Transient_Server_Failure_Matches_No_Specific_Predicate()
    {
        var ex = new BusinessCentralServerException("x", HttpStatusCode.ServiceUnavailable, "GET", null, null);

        Assert.True(ex.IsTransient);
        Assert.False(ex.IsNotFound);
        Assert.False(ex.IsThrottled);
        Assert.False(ex.IsValidation);
        Assert.False(ex.IsAuth);
        Assert.False(ex.IsConnectionFailure);
    }
}
