using Arronix.Abstractions.FileSystem;
using Arronix.Host.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// The file system contract is experimental; the host registers its only implementation.
#pragma warning disable ARX0005

namespace Arronix.Host.Composition;

/// <summary>
/// Registers the platform's own file system.
/// </summary>
/// <remarks>
/// Registered before the extension runtime, and that ordering is load-bearing. The shared platform assembly
/// consumes the file system contract without shipping an implementation of it, so a host that registered
/// only the shared assembly could not resolve a file hasher at all. The extension runtime then wraps this
/// registration in a scoping decorator per extension, which is why the unconfined implementation must exist
/// first and why nothing but the composition root may resolve it directly.
/// </remarks>
internal static class FileSystemRegistration
{
    /// <summary>
    /// Registers the file system.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddHostFileSystem(this IServiceCollection services)
    {
        services.TryAddSingleton<IFileSystem, HostFileSystem>();
        return services;
    }
}
