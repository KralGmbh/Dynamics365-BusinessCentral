using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Dynamics365.BusinessCentral;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessCentral(
        this IServiceCollection services,
        Action<BusinessCentralOptions> configure)
    {
        services.Configure(configure);

        services.AddHttpClient<IBusinessCentralClient, BusinessCentralClient>();

        return services;
    }
}