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

        // "Does not throw" is the assertion; stating it beats leaving it to the absence of one.
        Assert.Null(Record.Exception(() => LiveTenant.GuardEnvironment(options)));
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

        Assert.Null(Record.Exception(() => LiveTenant.GuardEnvironment(options)));
    }

    /// <summary>
    /// The sandbox name somewhere harmless in the path does not make the target the sandbox.
    /// </summary>
    /// <remarks>
    /// Both of these passed an earlier version of this guard. The first is the reason it now reads
    /// the environment out of a position rather than searching for it: the URL carries the marker
    /// and points at Production. The second is the same mistake in a shape nobody would write on
    /// purpose but a template could easily produce.
    /// </remarks>
    [Theory]
    [InlineData("https://proxy.internal/KRALTEST/v2.0/tenant/Production/ODataV4")]
    [InlineData("https://api.businesscentral.dynamics.com/KRALTEST/v2.0/tenant/Production/ODataV4")]
    public void Rejects_the_sandbox_name_outside_the_environment_position(string baseUrl)
    {
        var options = OptionsFor(LiveTenant.Environment);
        options.BaseUrl = baseUrl;

        Assert.Throws<InvalidOperationException>(() => LiveTenant.GuardEnvironment(options));
    }

    /// <summary>
    /// A service root this code cannot parse is one whose environment it cannot verify.
    /// </summary>
    /// <remarks>
    /// "Cannot verify" and "verified safe" must not produce the same answer, so an unrecognized
    /// shape is rejected rather than interpreted — including the case where the sandbox name is
    /// the last segment and there is no <c>ODataV4</c> at all.
    /// </remarks>
    [Theory]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("https://api.businesscentral.dynamics.com/v2.0/tenant/KRALTEST")]
    [InlineData("https://api.businesscentral.dynamics.com/v2.0/tenant/KRALTEST/api/v2.0")]
    public void Rejects_a_service_root_it_cannot_read(string baseUrl)
    {
        var options = OptionsFor(LiveTenant.Environment);
        options.BaseUrl = baseUrl;

        Assert.Throws<InvalidOperationException>(() => LiveTenant.GuardEnvironment(options));
    }

    /// <summary>The opt-in is a second condition, not a substitute for credentials.</summary>
    /// <remarks>
    /// Pinned because the failure it prevents is silent in the other direction: were
    /// <see cref="LiveTenant.IsConfigured"/> to stop consulting the opt-in, a solution-wide
    /// <c>dotnet test</c> on a machine holding credentials would quietly start contacting the
    /// tenant, and nothing would report that it had.
    /// </remarks>
    [Fact]
    public void Live_facts_need_the_opt_in_as_well_as_credentials()
    {
        if (!LiveTenant.IsOptedIn)
            Assert.False(LiveTenant.IsConfigured);
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
