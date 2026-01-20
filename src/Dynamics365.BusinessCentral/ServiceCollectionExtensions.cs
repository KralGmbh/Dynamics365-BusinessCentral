using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics.CodeAnalysis;

namespace Dynamics365.BusinessCentral;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessCentral(
        this IServiceCollection services,
        Action<BusinessCentralOptions> configure)
    {
        services
            .AddOptions<BusinessCentralOptions>()
            .Configure(configure)
            .Validate(ValidateOption, "BusinessCentralOptions contain invalid configuration.")
            .ValidateOnStart();

        services.AddHttpClient<IBusinessCentralClient, BusinessCentralClient>();

        return services;
    }

    public static IServiceCollection AddObserver<TObserver>(
        this IServiceCollection services)
        where TObserver : class, IBusinessCentralObserver
    {
        services.TryAddSingleton<IBusinessCentralObserver, TObserver>();
        return services;
    }

    private static bool ValidateOption(BusinessCentralOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.TenantId)
               && !string.IsNullOrWhiteSpace(options.ClientId)
               && !string.IsNullOrWhiteSpace(options.ClientSecret)
               && !string.IsNullOrWhiteSpace(options.BaseUrl)
               && !string.IsNullOrWhiteSpace(options.Company)
               && !string.IsNullOrWhiteSpace(options.Scope)
               && !string.IsNullOrWhiteSpace(options.TokenEndpoint);
    }
}
