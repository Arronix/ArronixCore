using Arronix.Host.Media.Catalog;
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

        // The journaled forms. A composed host reads back what a previous process configured and assigned;
        // the unjournaled constructors exist for fixtures and unit tests and are never registered.
        services.TryAddSingleton(provider => new ProviderDefinitionStore(
            provider.GetRequiredService<ProviderRegistry>(),
            provider.GetRequiredService<Abstractions.Events.IEventPublisher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IProviderDefinitionJournal>()));
        services.TryAddSingleton<ProviderStatusStore>();
        services.TryAddSingleton<ProviderSessionStore>();
        services.TryAddSingleton<ProviderTestService>();
        services.TryAddSingleton(provider => new CatalogIdentity(
            provider.GetRequiredService<ICatalogIdentityJournal>()));
        services.TryAddSingleton<CatalogDispatcher>();
        services.TryAddSingleton(provider => new CatalogLibrary(
            provider.GetRequiredService<Media.MediaKindRegistry>(),
            provider.GetRequiredService<CatalogDispatcher>(),
            provider.GetRequiredService<Storage.ICatalogRecordStore>(),
            provider.GetRequiredService<Storage.IMediaStore>(),
            provider.GetRequiredService<CatalogIdentity>(),
            provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IndexerDispatcher>();
        services.TryAddSingleton<NotificationDispatcher>();

        return services;
    }
}
