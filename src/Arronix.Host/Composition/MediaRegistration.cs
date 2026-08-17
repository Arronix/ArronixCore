using Arronix.Host.Intent;
using Arronix.Host.Media.Typed;
using Arronix.Plugins.Registration;
using Arronix.Host.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Host.Composition;

/// <summary>
/// Registers the media-kind registry and everything derived from a validated shape.
/// </summary>
/// <remarks>
/// Declared intent is registered here rather than in a subsystem of its own, because it is admitted at the
/// same moment as the shape it describes and is meaningless without it. Two registration points would create
/// two places a media kind could exist, and one of them could outlive the other.
/// </remarks>
internal static class MediaRegistration
{
    /// <summary>
    /// Registers the media subsystem.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddMediaRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<MediaKindRegistry>();
        services.TryAddSingleton<IMediaKindRegistry>(
            provider => provider.GetRequiredService<MediaKindRegistry>());

        services.TryAddSingleton<IIntentRegistry, IntentRegistry>();
        services.TryAddSingleton<WorkbenchBroker>();
        services.TryAddSingleton<CompletenessCalculator>();
        services.TryAddSingleton<MediaItemProjection>();

        // The typed registration path. The catalog's engine slots are filled by EngineRegistration, and the
        // binder refuses a kind whose required slots are still empty rather than admitting one that declares
        // behavior nothing can execute. The catalog is registered there rather than here so that "what this
        // build can execute" has one answer in one file.
        services.TryAddSingleton<MediaTypeBinder>();

        // How a typed kind is priced in capabilities, for the registry's half of the bidirectional check.
        // Registered against the plugins-side interface because the registry is what consumes it.
        services.TryAddSingleton<IMediaTypeCapabilityReader, MediaTypeCapabilityReader>();

        return services;
    }
}
