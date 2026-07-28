using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

namespace Dynamics365.BusinessCentral.Tests;

public class OptionsTests
{
    [Fact]
    public void Defaults_Require_Only_Four_Settings()
    {
        var options = new BusinessCentralOptions
        {
            TenantId = "11111111-2222-3333-4444-555555555555",
            ClientId = "client",
            ClientSecret = "secret",
            Company = "CRONUS AG"
        };

        Assert.Equal(
            "https://api.businesscentral.dynamics.com/v2.0/11111111-2222-3333-4444-555555555555/Production/ODataV4",
            options.ResolvedBaseUrl);

        Assert.Equal(
            "https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/oauth2/v2.0/token",
            options.ResolvedTokenEndpoint);
    }

    [Fact]
    public void Environment_Placeholder_Is_Substituted()
    {
        var options = new BusinessCentralOptions
        {
            TenantId = "tenant",
            Environment = "Sandbox",
            ClientId = "c",
            ClientSecret = "s",
            Company = "Test"
        };

        Assert.Contains("/tenant/Sandbox/ODataV4", options.ResolvedBaseUrl);
    }

    [Fact]
    public void Legacy_TenantId_Placeholder_Still_Works()
    {
        var options = new BusinessCentralOptions
        {
            TenantId = "tenant",
            ClientId = "c",
            ClientSecret = "s",
            Company = "Test",
            TokenEndpoint = "https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token"
        };

        Assert.Equal("https://login.microsoftonline.com/tenant/oauth2/v2.0/token", options.ResolvedTokenEndpoint);
    }

    [Fact]
    public async Task BaseUrl_Placeholder_Reaches_The_Wire()
    {
        string? capturedUrl = null;

        var client = TestBase.CreateClient(
            TestBase.WithToken(req =>
            {
                capturedUrl = req.RequestUri!.AbsoluteUri;
                return TestBase.Json("{\"value\":[]}");
            }),
            configure: o =>
            {
                o.TenantId = "abc-123";
                o.Environment = "UAT";
                o.BaseUrl = "https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/ODataV4";
            });

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.NotNull(capturedUrl);
        Assert.Contains("/v2.0/abc-123/UAT/ODataV4/", capturedUrl);
        Assert.DoesNotContain("%7B", capturedUrl);
        Assert.DoesNotContain("{", capturedUrl);
    }

    [Fact]
    public void Unsubstituted_Placeholder_Fails_Validation_With_A_Clear_Message()
    {
        var services = new ServiceCollection();

        services.AddBusinessCentral(o =>
        {
            o.TenantId = "tenant";
            o.ClientId = "client";
            o.ClientSecret = "secret";
            o.Company = "Test";
            o.BaseUrl = "https://api.businesscentral.dynamics.com/v2.0/{unknown}/ODataV4";
        });

        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<BusinessCentralOptions>>().Value);

        var message = string.Join(" | ", ex.Failures);

        Assert.Contains("unsubstituted placeholder", message);

        // The message must list every placeholder ResolvePlaceholders actually handles,
        // including the historical {TenantId}, or it sends users the wrong way.
        Assert.Contains("{tenant}", message);
        Assert.Contains("{TenantId}", message);
        Assert.Contains("{environment}", message);
    }

    [Fact]
    public async Task Client_Can_Be_Bound_From_Configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BusinessCentral:TenantId"] = "tenant",
                ["BusinessCentral:ClientId"] = "client",
                ["BusinessCentral:ClientSecret"] = "secret",
                ["BusinessCentral:Company"] = "CRONUS AG",
                ["BusinessCentral:Environment"] = "Sandbox",
                ["BusinessCentral:Retry:MaxAttempts"] = "7"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddBusinessCentral(configuration.GetSection("BusinessCentral"));

        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<BusinessCentralOptions>>().Value;

        Assert.Equal("CRONUS AG", options.Company);
        Assert.Equal("Sandbox", options.Environment);
        Assert.Equal(7, options.Retry.MaxAttempts);
        Assert.Contains("/tenant/Sandbox/", options.ResolvedBaseUrl);

        var client = provider.GetRequiredService<IBusinessCentralClient>();

        Assert.Equal("CRONUS AG", client.Company);

        await Task.CompletedTask;
    }

    [Fact]
    public void Configuration_Overload_Accepts_A_Code_Override()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TenantId"] = "tenant",
                ["ClientId"] = "client",
                ["ClientSecret"] = "from-config",
                ["Company"] = "Test"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddBusinessCentral(configuration, o => o.ClientSecret = "from-code");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BusinessCentralOptions>>().Value;

        Assert.Equal("from-code", options.ClientSecret);
        Assert.Equal("tenant", options.TenantId);
    }

    [Fact]
    public async Task Token_Endpoint_Uses_The_Resolved_Placeholder()
    {
        string? tokenUrl = null;

        var client = TestBase.CreateClient(
            req =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("login") || req.RequestUri!.AbsoluteUri.Contains("auth"))
                {
                    tokenUrl = req.RequestUri!.AbsoluteUri;
                    return TestBase.Json("{\"access_token\":\"abc\",\"expires_in\":3600}");
                }

                return TestBase.Json("{\"value\":[]}");
            },
            configure: o =>
            {
                o.TenantId = "my-tenant";
                o.TokenEndpoint = "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";
            });

        await client.QueryAsync<TestEntity>("orders", "true");

        Assert.Equal("https://login.microsoftonline.com/my-tenant/oauth2/v2.0/token", tokenUrl);
    }

    [Fact]
    public void Filter_In_With_Empty_Collection_Is_Valid_OData()
    {
        Assert.Equal("false", Dynamics365.BusinessCentral.OData.Filter.In("id").Value);
        Assert.Equal("false", Dynamics365.BusinessCentral.OData.Filter.In("id", Array.Empty<object>()).Value);
        Assert.Equal("id in ('a','b')", Dynamics365.BusinessCentral.OData.Filter.In("id", "a", "b").Value);
    }
}
