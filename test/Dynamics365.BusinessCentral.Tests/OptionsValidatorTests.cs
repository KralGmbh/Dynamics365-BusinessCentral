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
}
