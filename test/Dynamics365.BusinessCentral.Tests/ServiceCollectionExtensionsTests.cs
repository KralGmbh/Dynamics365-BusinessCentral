using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Dynamics365.BusinessCentral.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

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


    [Fact]
    public void Validation_Names_Every_Missing_Option()
    {
        var services = new ServiceCollection();

        services.AddBusinessCentral(options =>
        {
            options.TenantId = "tenant";
            options.ClientId = "";
            options.ClientSecret = "  ";
            options.BaseUrl = "https://test";
            options.Company = "Test";
            options.Scope = "scope";
            options.TokenEndpoint = "https://auth/{TenantId}";
        });

        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BusinessCentralOptions>>().Value);

        var message = string.Join(" | ", ex.Failures);

        Assert.Contains(nameof(BusinessCentralOptions.ClientId), message);
        Assert.Contains(nameof(BusinessCentralOptions.ClientSecret), message);
        Assert.DoesNotContain(nameof(BusinessCentralOptions.TenantId), message);
    }

    [Fact]
    public void Validation_Rejects_Relative_BaseUrl()
    {
        var services = new ServiceCollection();

        services.AddBusinessCentral(options =>
        {
            options.TenantId = "tenant";
            options.ClientId = "client";
            options.ClientSecret = "secret";
            options.BaseUrl = "not-a-url";
            options.Company = "Test";
            options.Scope = "scope";
            options.TokenEndpoint = "https://auth/{TenantId}";
        });

        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BusinessCentralOptions>>().Value);

        Assert.Contains("absolute URL", string.Join(" | ", ex.Failures));
    }

    [Fact]
    public async Task Token_Cache_Is_Shared_Across_Resolved_Clients()
    {
        // Typed HTTP clients are transient, so the token cache must live on a singleton
        // or every injection would re-authenticate.
        var tokenCalls = 0;

        var services = new ServiceCollection();

        services.AddBusinessCentral(DefaultOptions);

        // Configure the named handlers rather than re-registering the typed client,
        // which would replace the registration under test.
        services
            .AddHttpClient(BusinessCentralHttpClients.Token)
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpHandler(_ =>
            {
                Interlocked.Increment(ref tokenCalls);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc\",\"expires_in\":3600}")
                };
            }));

        services
            .AddHttpClient(BusinessCentralHttpClients.Client)
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[]}")
                }));

        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IBusinessCentralClient>();
        var second = provider.GetRequiredService<IBusinessCentralClient>();

        Assert.NotSame(first, second);

        await first.QueryAsync<TestEntity>("orders", "true");
        await second.QueryAsync<TestEntity>("orders", "true");

        Assert.Equal(1, tokenCalls);
    }

    [Fact]
    public void RequestTimeout_Is_Applied_To_The_Data_Client()
    {
        var services = new ServiceCollection();
        services.AddBusinessCentral(options =>
        {
            DefaultOptions(options);
            options.RequestTimeout = TimeSpan.FromSeconds(60);
        });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(TimeSpan.FromSeconds(60),
            factory.CreateClient(BusinessCentralHttpClients.Client).Timeout);
    }

    [Fact]
    public void RequestTimeout_Defaults_To_The_HttpClient_Default()
    {
        var services = new ServiceCollection();
        services.AddBusinessCentral(DefaultOptions);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(TimeSpan.FromSeconds(100),
            factory.CreateClient(BusinessCentralHttpClients.Client).Timeout);
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
