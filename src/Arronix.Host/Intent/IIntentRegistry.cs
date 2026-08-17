using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;

// The intent contracts are experimental; the registry publishes them.
#pragma warning disable ARX0016

namespace Arronix.Host.Intent;

/// <summary>
/// What every loaded extension declared about how its media kind is worked with.
/// </summary>
/// <remarks>
/// The host is the sole publisher of declared intent. That is what makes the security property hold: a
/// declaration reaches a consumer only by passing through here, so an action or a working surface an
/// extension's capabilities do not support is refused at load rather than filtered at render time by every
/// consumer independently.
/// </remarks>
public interface IIntentRegistry
{
    /// <summary>
    /// Gets every declared surface, ordered by media kind.
    /// </summary>
    IReadOnlyList<PluginIntentSurface> All { get; }

    /// <summary>
    /// Looks up one media kind's declared surface.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="surface">The surface when the kind is registered.</param>
    /// <returns><see langword="true"/> when the kind is registered.</returns>
    bool TryGet(MediaKindId kind, [NotNullWhen(true)] out PluginIntentSurface? surface);

    /// <summary>
    /// Looks up one declared action.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="actionId">The action.</param>
    /// <returns>The action, or <see langword="null"/> when the kind declares no such action.</returns>
    ActionDescriptor? FindAction(MediaKindId kind, string actionId);

    /// <summary>
    /// Looks up one declared working surface.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="workbenchId">The surface.</param>
    /// <returns>The surface, or <see langword="null"/> when the kind declares no such surface.</returns>
    WorkbenchDescriptor? FindWorkbench(MediaKindId kind, string workbenchId);
}
