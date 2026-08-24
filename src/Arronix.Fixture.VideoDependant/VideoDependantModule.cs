using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;

namespace Arronix.Fixture.VideoDependant;

/// <summary>
/// A notifier whose only job is to name the <see cref="Video"/> type this package resolved.
/// </summary>
/// <remarks>
/// The provider family is incidental. What matters is that the type is reachable from the registered
/// implementation type, so a test can compare it by reference with what another package resolved without
/// either package knowing the other exists.
/// </remarks>
public sealed class VideoDependantNotifier : INotifier
{
    private static readonly PluginId Package = PluginId.FromString("fixture.video.dependant");

    /// <summary>
    /// Gets the video representation this package composes.
    /// </summary>
    /// <remarks>
    /// Declared as <see cref="Video"/> rather than as a <see cref="Type"/> so the package's own public
    /// surface names the shared contract type. A test can then observe what this package resolved without
    /// this package handing it an answer.
    /// </remarks>
    public Video? Witness => null;

    /// <inheritdoc />
    public IReadOnlyList<NotificationEvent> SupportedEvents { get; } = [NotificationEvent.ApplicationUpdated];

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ValidationOutcome.Success);

    /// <inheritdoc />
    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);

    /// <inheritdoc />
    public Task NotifyAsync(
        ProviderInvocation invocation,
        NotificationMessage message,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>The fixture's entry module.</summary>
public sealed class VideoDependantModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("fixture.video.dependant");

    /// <inheritdoc />
    public string Name => "Video dependant fixture";

    /// <inheritdoc />
    public string Version => "0.1.0";

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry.AddNotifier<VideoDependantNotifier>(new ProviderDescriptor
        {
            LocalId = "video-witness",
            Name = "Video witness",
            Settings = [],
        });
    }
}
