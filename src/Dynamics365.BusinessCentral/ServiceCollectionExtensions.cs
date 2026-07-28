using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dynamics365.BusinessCentral;

public static class ServiceCollectionExtensions
{
    /// <summary>Name of the internal HttpClient used for token acquisition.</summary>
    internal const string TokenHttpClientName = "Dynamics365.BusinessCentral.Token";

    /// <summary>Name of the HttpClient used for data requests.</summary>
    internal const string ClientHttpClientName = "Dynamics365.BusinessCentral.Client";

    public static IServiceCollection AddBusinessCentral(
        this IServiceCollection services,
        Action<BusinessCentralOptions> configure)
    {
        services
            .AddOptions<BusinessCentralOptions>()
            .Configure(configure)
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BusinessCentralOptions>, BusinessCentralOptionsValidator>());

        services.AddHttpClient(TokenHttpClientName);

        // Singleton so the access-token cache is shared. Typed HTTP clients are registered
        // as transient, so a per-client cache would re-authenticate on every injection.
        services.TryAddSingleton(sp => new BusinessCentralTokenProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(TokenHttpClientName),
            sp.GetRequiredService<IOptions<BusinessCentralOptions>>(),
            sp.GetService<IBusinessCentralObserver>()));

        // Explicit factory rather than ActivatorUtilities: the constructor taking the
        // token provider is internal and would otherwise not be selected.
        services.AddHttpClient<IBusinessCentralClient, BusinessCentralClient>(
            ClientHttpClientName,
            (http, sp) => new BusinessCentralClient(
                http,
                sp.GetRequiredService<IOptions<BusinessCentralOptions>>(),
                sp.GetRequiredService<BusinessCentralTokenProvider>(),
                sp.GetService<IBusinessCentralObserver>()));

        return services;
    }

    public static IServiceCollection AddObserver<TObserver>(
        this IServiceCollection services)
        where TObserver : class, IBusinessCentralObserver
    {
        services.TryAddSingleton<IBusinessCentralObserver, TObserver>();
        return services;
    }
}
