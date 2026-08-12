using Dynamics365.BusinessCentral.Options;

namespace Dynamics365.BusinessCentral.LiveTenant.Tests;

/// <summary>
/// The environment guardrail, tested without a tenant.
/// </summary>
/// <remarks>
/// Plain <see cref="FactAttribute"/>, not <see cref="LiveTenantFactAttribute"/>: these need no
/// credentials and must therefore run everywhere, including on a contributor's machine where every
/// other fact in this project skips. A guardrail whose own tests skip in the same conditions that
/// make it matter is not a guardrail.
/// </remarks>
public sealed class GuardEnvironmentTests
{
    [Fact]
    public void Accepts_the_sandbox_environment()
    {
        var options = OptionsFor(LiveTenant.Environment);

        LiveTenant.GuardEnvironment(options);
    }

    /// <summary>
    /// A name that merely <i>starts with</i> the sandbox's is a different environment.
    /// </summary>
    /// <remarks>
    /// The reason the check compares path segments rather than calling
    /// <see cref="string.Contains(string, StringComparison)"/>. <c>KRALTEST2</c> is a realistic
    /// name for a second sandbox, and a substring test would have sent live queries there while
    /// reporting the guard satisfied.
    /// </remarks>
    [Theory]
    [InlineData("KRALTEST2")]
    [InlineData("PREKRALTEST")]
    [InlineData("Production")]
    [InlineData("")]
    public void Rejects_any_other_environment(string environment)
    {
        var options = OptionsFor(environment);

        Assert.Throws<InvalidOperationException>(() => LiveTenant.GuardEnvironment(options));
    }

    /// <summary>
    /// A wholesale <c>BaseUrl</c> is judged on the URL, not on the decorative
    /// <see cref="BusinessCentralOptions.Environment"/> beside it.
    /// </summary>
    [Fact]
    public void Rejects_a_base_url_that_names_another_environment_whatever_Environment_says()
    {
        var options = new BusinessCentralOptions
        {
            TenantId = "00000000-0000-0000-0000-000000000000",
            ClientId = "id",
            ClientSecret = "secret",
            Company = LiveTenant.Company,
            Environment = LiveTenant.Environment,
            BaseUrl = "https://api.businesscentral.dynamics.com/v2.0/tenant/Production/ODataV4",
        };

        Assert.Throws<InvalidOperationException>(() => LiveTenant.GuardEnvironment(options));
    }

    /// <summary>And the placeholder form resolves before it is judged.</summary>
    [Fact]
    public void Accepts_the_sandbox_through_an_unresolved_placeholder()
    {
        var options = OptionsFor(LiveTenant.Environment);
        options.BaseUrl = "https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/ODataV4";

        LiveTenant.GuardEnvironment(options);
    }

    private static BusinessCentralOptions OptionsFor(string environment) => new()
    {
        TenantId = "00000000-0000-0000-0000-000000000000",
        ClientId = "id",
        ClientSecret = "secret",
        Company = LiveTenant.Company,
        Environment = environment,
    };
}
