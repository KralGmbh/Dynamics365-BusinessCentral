using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace Dynamics365.BusinessCentral;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessCentral(
        this IServiceCollection services,
        Action<BusinessCentralOptions> configure)
    {
        // Register options instance
        var options = new BusinessCentralOptions();
        configure(options);
        services.AddSingleton(options);

        // Register client with factory so observer can be resolved
        services.AddHttpClient<IBusinessCentralClient, BusinessCentralClient>()
            .AddTypedClient((http, sp) =>
            {
                var observer = sp.GetService<IBusinessCentralObserver>();

                return new BusinessCentralClient(
                    http,
                    sp.GetRequiredService<BusinessCentralOptions>(),
                    observer);
            });

        return services;
    }

    public static IServiceCollection AddObserver<TObserver>(
        this IServiceCollection services)
        where TObserver : class, IBusinessCentralObserver
    {
        services.AddSingleton<IBusinessCentralObserver, TObserver>();
        return services;
    }
}
