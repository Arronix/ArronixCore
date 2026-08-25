using Arronix.Abstractions.Caching;
using Arronix.Common.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Common.Composition;

/// <summary>
/// Registers <c>Caching/</c>.
/// </summary>
internal static class CachingRegistration
{
    /// <summary>
    /// Registers the cache provider under both of its faces, and its sweep options.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">Where the sweep options are read from.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// One instance, two contracts. The extension-facing <see cref="ICacheProvider"/> and the host-facing
    /// <see cref="ICacheNamespaceProvider"/> must be the same object, because a host that released
    /// namespaces on one provider while extensions filled another would release nothing.
    /// </remarks>
    internal static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptionsWithValidateOnStart<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateDataAnnotations();

        services.TryAddSingleton<CacheProvider>();
        services.TryAddSingleton<ICacheNamespaceProvider>(
            static provider => provider.GetRequiredService<CacheProvider>());
        services.TryAddSingleton<ICacheProvider>(
            static provider => provider.GetRequiredService<ICacheNamespaceProvider>());

        return services;
    }
}
