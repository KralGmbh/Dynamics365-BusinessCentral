using Dynamics365.BusinessCentral.Options;

namespace Dynamics365.BusinessCentral.Tests;

/// <summary>
/// Direct pins on <see cref="BusinessCentralOptionsValidator"/> for the numeric options —
/// misconfiguration must fail as a named options error at startup, never as a bare
/// runtime exception when the client is first created.
/// </summary>
public class OptionsValidatorTests
{
    private static BusinessCentralOptions Valid() => new()
    {
        TenantId = "tenant",
        ClientId = "client",
        ClientSecret = "secret",
        Company = "Test",
        BaseUrl = "https://test",
        TokenEndpoint = "https://auth/{TenantId}"
    };

    private static IEnumerable<string> Failures(BusinessCentralOptions options) =>
        new BusinessCentralOptionsValidator().Validate(null, options).Failures ?? [];

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_Positive_RequestTimeout_Fails_Validation(int seconds)
    {
        var options = Valid();
        options.RequestTimeout = TimeSpan.FromSeconds(seconds);

        Assert.Contains(Failures(options), f => f.Contains(nameof(options.RequestTimeout)));
    }

    // HttpClient.Timeout throws above int.MaxValue milliseconds (~24.8 days); the
    // validator must reject it first with a message naming the option.
    [Fact]
    public void RequestTimeout_Above_HttpClient_Maximum_Fails_Validation()
    {
        var options = Valid();
        options.RequestTimeout = TimeSpan.FromDays(25);

        Assert.Contains(Failures(options), f => f.Contains(nameof(options.RequestTimeout)));
    }

    [Fact]
    public void Reasonable_RequestTimeout_Passes()
    {
        var options = Valid();
        options.RequestTimeout = TimeSpan.FromSeconds(60);

        Assert.DoesNotContain(Failures(options), f => f.Contains(nameof(options.RequestTimeout)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_MaxPageSize_Fails_Validation(int value)
    {
        var options = Valid();
        options.MaxPageSize = value;

        Assert.Contains(Failures(options), f => f.Contains(nameof(options.MaxPageSize)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_MaxQueryStringLength_Fails_Validation(int value)
    {
        var options = Valid();
        options.MaxQueryStringLength = value;

        Assert.Contains(Failures(options), f => f.Contains(nameof(options.MaxQueryStringLength)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_QueryStringLengthWarningThreshold_Fails_Validation(int value)
    {
        var options = Valid();
        options.QueryStringLengthWarningThreshold = value;

        Assert.Contains(Failures(options), f => f.Contains(nameof(options.QueryStringLengthWarningThreshold)));
    }

    // A threshold above the limit can never be reached, silently costing the deployment the
    // measurement window the two settings exist to create.
    [Fact]
    public void Warning_Threshold_Above_MaxQueryStringLength_Fails_Validation()
    {
        var options = Valid();
        options.MaxQueryStringLength = 1000;
        options.QueryStringLengthWarningThreshold = 2000;

        Assert.Contains(Failures(options), f => f.Contains(nameof(options.QueryStringLengthWarningThreshold)));
    }

    [Fact]
    public void Warning_Threshold_Equal_To_MaxQueryStringLength_Passes()
    {
        var options = Valid();
        options.MaxQueryStringLength = 2000;
        options.QueryStringLengthWarningThreshold = 2000;

        Assert.DoesNotContain(Failures(options), f => f.Contains(nameof(options.QueryStringLengthWarningThreshold)));
    }

    [Fact]
    public void Query_String_Length_Defaults_Pass_Validation()
    {
        var options = Valid();

        // Measured: the gateway accepts 8,099 query-string characters. Defaults leave headroom.
        Assert.Equal(8000, options.MaxQueryStringLength);
        Assert.Equal(6000, options.QueryStringLengthWarningThreshold);
        Assert.DoesNotContain(Failures(options), f => f.Contains("QueryStringLength"));
    }

    // Disabling one must not implicate the other.
    [Fact]
    public void Null_Query_String_Length_Settings_Pass_Validation()
    {
        var options = Valid();
        options.MaxQueryStringLength = null;
        options.QueryStringLengthWarningThreshold = null;

        Assert.DoesNotContain(Failures(options), f => f.Contains("QueryStringLength"));
    }
}
