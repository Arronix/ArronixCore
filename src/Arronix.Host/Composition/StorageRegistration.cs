using Arronix.Host.Storage;
using Arronix.Host.Intent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Host.Composition;

/// <summary>
/// Registers the storage seam.
/// </summary>
/// <remarks>
/// Only the interface and its records are visible; the implementation is internal on purpose. When the
/// relational store arrives it replaces this one registration and nothing else moves, which is the entire
/// reason the seam is shaped now rather than when persistence is built.
/// </remarks>
internal static class StorageRegistration
{
    /// <summary>
    /// Registers the in-memory store.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddInMemoryStorage(this IServiceCollection services)
    {
        services.TryAddSingleton<IMediaStore, InMemoryMediaStore>();
        services.TryAddSingleton<IStandardActionDispatcher, StandardActionDispatcher>();
        return services;
    }
}
