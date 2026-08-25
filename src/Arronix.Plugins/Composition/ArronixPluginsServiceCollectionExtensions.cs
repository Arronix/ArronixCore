using Arronix.Plugins.Configuration;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arronix.Plugins.Composition;

/// <summary>
/// The single registration entry point for extension loading.
/// </summary>
/// <remarks>
/// <para>
/// Explicit and exhaustive, with no assembly scanning, matching the idiom the platform assembly already
/// establishes: a host that cannot enumerate what it registered cannot withhold anything from an extension
/// either, and withholding is the whole of the least-privilege model.
/// </para>
/// <para>
/// Nothing here loads anything. Registration composes the loader; running it is the host's decision, taken
/// at the point in startup where the host is ready to accept what extensions contribute. Loading during
/// container construction would mean an extension could observe a half-built host.
/// </para>
/// </remarks>
public static class ArronixPluginsServiceCollectionExtensions
{
    /// <summary>
    /// Registers extension loading, isolation and the runtime registry.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">
    /// The configuration the options section is read from. The root is taken rather than a section so that
    /// the options type names the section it owns.
    /// </param>
    /// <returns>The same collection, so registration can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The clock is registered defensively. Everything here reads time through it, and a host that composed
    /// this without the platform assembly would otherwise fail at activation rather than at registration.
    /// </para>
    /// <para>
    /// Two things the host must already have registered: the platform's serializer, and the framework's
    /// logging. Neither is registered here on purpose — quietly supplying a substitute for logging would
    /// win the race against the host's own registration and silently discard its diagnostics, which is a
    /// worse outcome than failing to resolve.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddArronixPlugins(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptionsWithValidateOnStart<PluginRuntimeOptions>()
            .Bind(configuration.GetSection(PluginRuntimeOptions.SectionName))
            .ValidateDataAnnotations();

        services.TryAddSingleton(TimeProvider.System);

        // The bundle of platform services an extension context is built from. Its gated members are
        // optional, so a host that has not built a subsystem yet still composes.
        services.TryAddSingleton<PluginPlatformServices>();

        services.TryAddSingleton<PluginPublicationGate>();
        services.TryAddSingleton<TokenRegistry>();
        services.TryAddSingleton<PluginRuntimeRegistry>();
        services.TryAddSingleton<IPluginRuntimeRegistry>(
            provider => provider.GetRequiredService<PluginRuntimeRegistry>());

        services.TryAddSingleton<PackageDependencyRegistry>();

        // What a browser may load. A projection of the Active registry rather than a second registry, so a
        // package's client facet appears and disappears with the package itself.
        services.TryAddSingleton<ClientContractCatalog>();
        services.TryAddSingleton<IClientContractCatalog>(
            provider => provider.GetRequiredService<ClientContractCatalog>());

        // The one authority on what the installation requires of itself, and the one authority on the
        // shared contract assemblies it admits. Both are required: an installation that declares no
        // dependency resolves to an empty graph and an installation that shares nothing admits an empty
        // set, and neither is the same statement as having no authority at all.
        services.TryAddSingleton<IPackageGraphSource, PackageDependencyResolver>();
        services.TryAddSingleton(provider => new SharedContractStore(
            provider.GetService<ILogger<SharedContractStore>>()));

        services.TryAddSingleton(provider => new PluginLoader(
            provider.GetRequiredService<IOptions<PluginRuntimeOptions>>(),
            provider.GetRequiredService<PluginPlatformServices>(),
            provider.GetRequiredService<PluginRuntimeRegistry>(),
            provider.GetRequiredService<TokenRegistry>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PluginLoader>>(),
            provider.GetRequiredService<PluginPublicationGate>(),
            provider.GetRequiredService<IPackageGraphSource>(),
            provider.GetRequiredService<SharedContractStore>(),
            provider.GetRequiredService<PackageDependencyRegistry>(),
            provider.GetService<IMediaTypeCapabilityReader>()));

        return services;
    }
}
