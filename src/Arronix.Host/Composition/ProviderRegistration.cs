using Arronix.Host.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Host.Composition;

/// <summary>
/// Registers the provider subsystem.
/// </summary>
/// <remarks>
/// Five families, one registry, one definition store and one status store. The families are a value on a
/// registration rather than a generic type argument, which is what lets one store hold all of them and one
/// announcement describe a change to any of them.
/// </remarks>
internal static class ProviderRegistration
{
    /// <summary>
    /// Registers the provider subsystem.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddProviderRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<ProviderRegistry>();
        services.TryAddSingleton<ProviderDefinitionStore>();
        services.TryAddSingleton<ProviderStatusStore>();
        services.TryAddSingleton<ProviderSessionStore>();
        services.TryAddSingleton<ProviderTestService>();
        services.TryAddSingleton<IndexerDispatcher>();
        services.TryAddSingleton<NotificationDispatcher>();

        return services;
    }
}
