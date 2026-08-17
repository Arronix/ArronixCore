using Arronix.Common.Composition;
using Arronix.Host.Runtime;
using Arronix.Plugins.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Host.Composition;

/// <summary>
/// The single registration entry point for the unified host runtime.
/// </summary>
/// <remarks>
/// <para>
/// A table of contents and nothing else. Each subsystem is one line, implemented as an internal static
/// method in its own file, so that adding a subsystem is one line here and a file there — and so that the
/// question "what does this host register?" has an answer a reviewer can read in one screen.
/// </para>
/// <para>
/// Nothing scans an assembly. A host that cannot enumerate what it registered cannot withhold anything from
/// an extension either, and withholding is the whole of the least-privilege model.
/// </para>
/// <para>
/// The order of the lines is not arbitrary in two places. The platform's own file system is registered
/// before the extension runtime, because the shared assembly consumes the file-system contract without
/// shipping an implementation and because the extension runtime wraps that implementation per extension. And
/// the extension bootstrapper is registered last, because it commits into every registry above it.
/// </para>
/// </remarks>
public static class ArronixHostServiceCollectionExtensions
{
    /// <summary>
    /// Registers the host runtime: composition, extension lifecycle, scheduling, health, media and storage.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">
    /// The configuration the host's options sections are read from. The root is taken rather than a section,
    /// so that each options type names the section it owns and the mapping between a type and its
    /// configuration path lives in one place.
    /// </param>
    /// <returns>The same collection, so registration can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddArronixHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddArronixCommon(configuration);
        services.AddArronixHostOptions(configuration);

        // The clock every host component reads. Registered by the shared assembly too; taken here as well so
        // that a host composed without it still has one, and so that substituting it in a test needs one
        // registration rather than knowing which assembly claimed it first.
        services.TryAddSingleton(TimeProvider.System);

        services.AddHostFileSystem();
        services.AddArronixPlugins(configuration);

        services.AddMediaRegistry();
        services.AddDefinitionEngines();
        services.AddInMemoryStorage();
        services.AddScheduling();
        services.AddProviderRegistry();
        services.AddHealthAggregation();

        services.AddHostedService<PluginBootstrapper>();

        return services;
    }
}
