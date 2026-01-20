using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Dynamics365.BusinessCentral.Tests;

public class ServiceCollectionExtensionsTests
{
    private static Action<BusinessCentralOptions> DefaultOptions => options =>
    {
        options.TenantId = "tenant";
        options.ClientId = "client";
        options.ClientSecret = "secret";
        options.BaseUrl = "https://test";
        options.Company = "Test";
        options.Scope = "scope";
        options.TokenEndpoint = "https://auth/{TenantId}";
    };

    [Fact]
    public void AddBusinessCentral_Registers_Client_Without_Observer()
    {
        var services = new ServiceCollection();

        services.AddBusinessCentral(DefaultOptions);

        var provider = services.BuildServiceProvider();

        var client = provider.GetService<IBusinessCentralClient>();

        Assert.NotNull(client);
        Assert.IsType<BusinessCentralClient>(client);
    }

    [Fact]
    public void AddBusinessCentral_With_Observer_Registers_Client()
    {
        var services = new ServiceCollection();

        services
            .AddBusinessCentral(DefaultOptions)
            .AddObserver<TestObserver>();

        var provider = services.BuildServiceProvider();

        var client = provider.GetService<IBusinessCentralClient>();

        Assert.NotNull(client);
        Assert.IsType<BusinessCentralClient>(client);

        // Ensure observer itself is resolvable
        var observer = provider.GetService<IBusinessCentralObserver>();
        Assert.NotNull(observer);
        Assert.IsType<TestObserver>(observer);
    }

    [Fact]
    public void AddObserver_Can_Be_Called_Without_AddBusinessCentral()
    {
        var services = new ServiceCollection();

        services.AddObserver<TestObserver>();

        var provider = services.BuildServiceProvider();

        var observer = provider.GetService<IBusinessCentralObserver>();

        Assert.NotNull(observer);
        Assert.IsType<TestObserver>(observer);
    }

    [Fact]
    public void AddBusinessCentral_Registers_Options()
    {
        var services = new ServiceCollection();

        services.AddBusinessCentral(DefaultOptions);

        var provider = services.BuildServiceProvider();

        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<BusinessCentralOptions>>();

        Assert.NotNull(options);
        Assert.Equal("tenant", options!.Value.TenantId);
    }


    private class TestObserver : IBusinessCentralObserver
    {
        public void OnRequestStarting(BusinessCentralRequestInfo info) { }
        public void OnRequestSucceeded(BusinessCentralRequestInfo info) { }
        public void OnRequestFailed(BusinessCentralErrorInfo error) { }
        public void OnTokenRequested() { }
        public void OnTokenRefreshed(BusinessCentralTokenInfo info) { }
        public void OnDeserializationFailed(BusinessCentralErrorInfo error) { }
    }
}
