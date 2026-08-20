using Arronix.Abstractions.Serialization;
using Arronix.Common.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;


namespace Arronix.Common.Composition;

/// <summary>
/// Registers the platform's serialization services.
/// </summary>
internal static class SerializationRegistration
{
    /// <summary>
    /// Registers the JSON serializer.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The serializer is stateless and holds a frozen options instance, so one instance serves the whole
    /// process. Registration yields to a host substitution: a host that must speak an established external
    /// wire shape registers its own implementation first, and everything downstream — including extensions —
    /// then agrees with it, which is the entire point of resolving a serializer rather than constructing one.
    /// </remarks>
    internal static IServiceCollection AddSerialization(this IServiceCollection services)
    {
        services.TryAddSingleton<IJsonSerializer, SystemTextJsonSerializer>();

        return services;
    }
}
