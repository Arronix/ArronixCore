using Arronix.Abstractions.Health;
using Arronix.Host.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;


namespace Arronix.Host.Composition;

/// <summary>
/// Registers health aggregation and the platform's own contributors.
/// </summary>
/// <remarks>
/// The extension contributor is registered as itself as well as a contributor, because the bootstrapper
/// hands it the contributors the extensions registered after the container was built. That is what keeps
/// extensions from ever mutating the container: their contributions go into an object the container already
/// holds rather than into the container itself.
/// </remarks>
internal static class HealthRegistration
{
    /// <summary>
    /// Registers the health subsystem.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddHealthAggregation(this IServiceCollection services)
    {
        services.TryAddSingleton<PluginHealthContributor>();
        services.TryAddSingleton<IHealthAggregator, HealthAggregator>();

        // Typed to the implementation rather than to the contract so the container can tell this
        // registration apart from the other contributors; an untyped factory is indistinguishable from them
        // and is refused.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PluginHealthContributor>(
            provider => provider.GetRequiredService<PluginHealthContributor>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SchedulerHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, ProviderHealthContributor>());

        return services;
    }
}
