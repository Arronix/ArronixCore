using Arronix.Abstractions.Hosting;
using Arronix.Common.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Common.Composition;

/// <summary>
/// Registers <c>Hosting/</c>: the immutable process and operating-system facts, and the probe chain
/// behind the second of them.
/// </summary>
internal static class HostingFactsRegistration
{
    /// <summary>
    /// Registers the operating-system identity probe, the operating-system facts and the runtime facts.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The probe is added to the chain rather than replacing it, because identity probing is additive: a
    /// platform pack contributes its reader alongside the freedesktop one, not instead of it.
    /// </remarks>
    internal static IServiceCollection AddHostingFacts(this IServiceCollection services)
    {
        // Constructed by hand rather than by the container, because the probe is host mechanics and
        // stays internal; the implementation type is still named so the enumerable de-duplicates.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IOsVersionProbe, OsReleaseProbe>(static _ => new OsReleaseProbe()));

        services.TryAddSingleton<IOperatingSystemInfo, OperatingSystemInfo>();
        services.TryAddSingleton<IHostRuntimeInfo, HostRuntimeInfo>();

        return services;
    }
}
