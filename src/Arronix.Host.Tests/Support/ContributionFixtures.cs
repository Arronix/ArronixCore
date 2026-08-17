using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;

// Shape and plugin contracts are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0014

namespace Arronix.Host.Tests.Support;

/// <summary>
/// Builds the bundle the registry admits, so a test states only the part it is about.
/// </summary>
internal static class ContributionFixtures
{
    internal static readonly PluginId Plugin = PluginId.FromString("fixture");

    internal static MediaKindContribution For(MediaShape shape, IMediaItemSource? items = null)
        => new()
        {
            Plugin = Plugin,
            PluginVersion = "0.1.0",
            Capabilities = CapabilitySet.Of(
                Capability.MediaKind,
                Capability.Indexing,
                Capability.Metadata,
                Capability.Renaming),
            Shape = shape,
            Items = items ?? new FakeItemSource(shape.Kind),
        };
}
