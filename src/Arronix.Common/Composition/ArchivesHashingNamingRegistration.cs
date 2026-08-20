using Arronix.Abstractions.Naming;
using Arronix.Common.Archives;
using Arronix.Common.Hashing;
using Arronix.Common.Naming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;


namespace Arronix.Common.Composition;

/// <summary>
/// Registers the three folders that turn content into something the rest of the platform can name, identify
/// and move around: <c>Archives/</c>, <c>Hashing/</c> and <c>Naming/</c>.
/// </summary>
/// <remarks>
/// They share one registration method because between them they contribute three services and no options.
/// Most of what these folders provide is static and stateless — deriving a digest, folding a title,
/// sanitizing a name — and a static function needs no registration at all, so splitting three lines across
/// three files would add ceremony without adding a seam.
/// </remarks>
internal static class ArchivesHashingNamingRegistration
{
    /// <summary>
    /// Registers the archive service, the file hasher and the platform's default fold table.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Every registration is conditional, so a host that has already supplied its own archive service,
    /// hasher or fold table keeps it. The fold table is added to the set of providers rather than replacing
    /// it, because folding is additive by design: a component that knows a language contributes its folds
    /// alongside the platform's rather than instead of them.
    /// </remarks>
    internal static IServiceCollection AddArchivesHashingAndNaming(this IServiceCollection services)
    {
        services.TryAddSingleton<IArchiveService, ArchiveService>();
        services.TryAddSingleton<IFileHasher, FileHasher>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDiacriticFoldingProvider, DefaultDiacriticFoldingProvider>());

        return services;
    }
}
