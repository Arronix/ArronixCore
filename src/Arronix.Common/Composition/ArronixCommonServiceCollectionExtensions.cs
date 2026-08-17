using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arronix.Common.Composition;

/// <summary>
/// The single registration entry point for the platform implementation assembly.
/// </summary>
/// <remarks>
/// <para>
/// Registration is explicit and exhaustive. Nothing here scans an assembly for types to register by
/// convention, because a host that cannot enumerate what it registered cannot withhold anything from an
/// extension either, and withholding is the whole of the least-privilege model. Every service the platform
/// provides is therefore named in source, on a line a reviewer can read.
/// </para>
/// <para>
/// The entry point stays thin by delegating to one <c>internal static</c> method per subsystem, each in its
/// own file under <c>Composition/</c>. A subsystem adds exactly one call below and owns everything behind
/// it, which keeps the entry point a table of contents rather than a second place where wiring decisions
/// are made.
/// </para>
/// </remarks>
public static class ArronixCommonServiceCollectionExtensions
{
    /// <summary>
    /// Registers the platform's options, primitives and services.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">
    /// The configuration the platform's options sections are read from. The root is taken rather than a
    /// section so that each options type names the section it owns, keeping the mapping between a type and
    /// its configuration path in one place.
    /// </param>
    /// <returns>The same collection, so registration can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Options are validated at startup. A host built on the framework's generic host gets that for free;
    /// a host that builds a provider directly triggers the same check by resolving
    /// <see cref="IStartupValidator"/> and calling <see cref="IStartupValidator.Validate"/> once, before it
    /// resolves anything else.
    /// </remarks>
    public static IServiceCollection AddArronixCommon(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddArronixCommonOptions(configuration);

        // The clock every platform component reads. It is registered here, once, so that no component has
        // to reach for a static clock and every one of them is testable by substituting this registration.
        services.TryAddSingleton(TimeProvider.System);

        // One line per subsystem, alphabetically, each implemented as an internal static extension method
        // in Composition/<Subsystem>Registration.cs. Nothing else belongs in this method.
        services.AddArchivesHashingAndNaming();
        services.AddSerialization();

        return services;
    }
}
