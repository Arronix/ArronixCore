using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Host.Media;

// The intent contracts are experimental; the registry publishes them.
#pragma warning disable ARX0016

namespace Arronix.Host.Intent;

/// <summary>
/// Reads declared intent out of the media-kind registry.
/// </summary>
/// <remarks>
/// A projection rather than a second store. Intent is admitted at the same moment as the shape it describes
/// and is meaningless without it, so holding it separately would create two places a media kind could exist
/// and one of them could outlive the other.
/// </remarks>
/// <param name="kinds">The media-kind registry.</param>
public sealed class IntentRegistry(IMediaKindRegistry kinds) : IIntentRegistry
{
    private readonly IMediaKindRegistry _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));

    /// <inheritdoc />
    public IReadOnlyList<PluginIntentSurface> All => [.. _kinds.All.Select(kind => kind.Intent)];

    /// <inheritdoc />
    public bool TryGet(MediaKindId kind, [NotNullWhen(true)] out PluginIntentSurface? surface)
    {
        if (_kinds.TryGet(kind, out var registered))
        {
            surface = registered.Intent;
            return true;
        }

        surface = null;
        return false;
    }

    /// <inheritdoc />
    public ActionDescriptor? FindAction(MediaKindId kind, string actionId)
        => actionId is not null && TryGet(kind, out var surface)
            ? surface.Actions.FirstOrDefault(action =>
                string.Equals(action.ActionId, actionId, StringComparison.Ordinal))
            : null;

    /// <inheritdoc />
    public WorkbenchDescriptor? FindWorkbench(MediaKindId kind, string workbenchId)
        => workbenchId is not null && TryGet(kind, out var surface)
            ? surface.Workbenches.FirstOrDefault(bench =>
                string.Equals(bench.WorkbenchId, workbenchId, StringComparison.Ordinal))
            : null;
}
