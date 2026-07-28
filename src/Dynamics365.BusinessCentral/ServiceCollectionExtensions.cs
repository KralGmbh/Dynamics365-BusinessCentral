using Dynamics365.BusinessCentral.Client;
using Dynamics365.BusinessCentral.Diagnostics;
using Dynamics365.BusinessCentral.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dynamics365.BusinessCentral;

/// <summary>
/// Registration helpers for the Business Central client.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Name of the internal HttpClient used for token acquisition.</summary>
    internal const string TokenHttpClientName = "Dynamics365.BusinessCentral.Token";

    /// <summary>Name of the HttpClient used for data requests.</summary>
    internal const string ClientHttpClientName = "Dynamics365.BusinessCentral.Client";

    /// <summary>
    /// Registers <see cref="IBusinessCentralClient"/> configured in code.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configure">Callback that populates <see cref="BusinessCentralOptions"/>.</param>
    /// <example>
    /// <code>
    /// services.AddBusinessCentral(o =>
    /// {
    ///     o.TenantId     = "...";
    ///     o.ClientId     = "...";
    ///     o.ClientSecret = "...";
    ///     o.Company      = "CRONUS AG";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddBusinessCentral(
        this IServiceCollection services,
        Action<BusinessCentralOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<BusinessCentralOptions>()
            .Configure(configure)
            .ValidateOnStart();

        return AddBusinessCentralCore(services);
    }

    /// <summary>
    /// Registers <see cref="IBusinessCentralClient"/> bound to a configuration section.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configuration">
    /// Section holding the settings, e.g. <c>configuration.GetSection("BusinessCentral")</c>.
    /// </param>
    /// <param name="configure">Optional callback applied after binding, to override values in code.</param>
    /// <example>
    /// <code>
    /// services.AddBusinessCentral(builder.Configuration.GetSection("BusinessCentral"));
    /// </code>
    /// </example>
    public static IServiceCollection AddBusinessCentral(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BusinessCentralOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services
            .AddOptions<BusinessCentralOptions>()
            .Bind(configuration)
            .ValidateOnStart();

        if (configure != null)
            builder.Configure(configure);

        return AddBusinessCentralCore(services);
    }

    private static IServiceCollection AddBusinessCentralCore(IServiceCollection services)
    {
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

    /// <summary>
    /// Registers a diagnostics observer. Call after <c>AddBusinessCentral</c>, or before —
    /// resolution order does not matter.
    /// </summary>
    /// <typeparam name="TObserver">Observer implementation.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    public static IServiceCollection AddObserver<TObserver>(
        this IServiceCollection services)
        where TObserver : class, IBusinessCentralObserver
    {
        services.TryAddSingleton<IBusinessCentralObserver, TObserver>();
        return services;
    }
}
