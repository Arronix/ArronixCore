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
    /// The extension-facing <see cref="ICacheProvider"/> is the authority and namespace control is derived
    /// from that exact instance. A host that substitutes its own provider therefore either keeps control of
    /// the caches its extensions fill or is told it cannot, rather than quietly leaving the host releasing
    /// namespaces on one object while extensions fill another.
    /// </remarks>
    internal static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptionsWithValidateOnStart<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateDataAnnotations();

        services.TryAddSingleton<CacheProvider>();
        services.TryAddSingleton<ICacheProvider>(
            static provider => provider.GetRequiredService<CacheProvider>());
        services.TryAddSingleton(
            static provider => provider.GetRequiredService<ICacheProvider>() as ICacheNamespaceProvider
                ?? throw new InvalidOperationException(
                    $"The registered {nameof(ICacheProvider)} is a "
                    + $"'{provider.GetRequiredService<ICacheProvider>().GetType().FullName}', which does not "
                    + $"implement {nameof(ICacheNamespaceProvider)}. Extensions are handed releasable cache "
                    + "namespaces, and a host cannot unload an extension whose caches it cannot take back."));

        return services;
    }
}
