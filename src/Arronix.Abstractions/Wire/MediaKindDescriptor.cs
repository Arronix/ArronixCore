using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// Everything a consumer needs to know about one media kind, published by the host.
/// </summary>
/// <remarks>
/// <para>
/// The placement rule for this contract area, stated so that it needs no further judgment: a type
/// belongs in the contract layer when it crosses an <b>assembly-isolation boundary</b> — the extension
/// boundary or the client boundary. Everything else stays host-side.
/// </para>
/// <para>
/// That is the promotion rule's physical-necessity clause widened by one word, and the justification is
/// structural: a client that may reference nothing but this assembly would otherwise have to mirror these
/// types, and a mirrored type is a guaranteed-divergence defect. They are not duplicates either — this
/// descriptor <i>contains</i> the shape and the field descriptors the extension declared. One
/// declaration, three consumers.
/// </para>
/// </remarks>
public sealed record MediaKindDescriptor
{
    /// <summary>
    /// Gets the media kind.
    /// </summary>
    public required MediaKindId Kind { get; init; }

    /// <summary>
    /// Gets the kind's display name for one item.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the kind's display name for several items.
    /// </summary>
    public required string PluralName { get; init; }

    /// <summary>
    /// Gets the kind's structure, verbatim as the extension declared it.
    /// </summary>
    public required MediaShape Shape { get; init; }

    /// <summary>
    /// Gets the per-level projection a consumer works from.
    /// </summary>
    public required IReadOnlyList<LevelPresentation> Levels { get; init; }

    /// <summary>
    /// Gets what the extension declared about how the kind is worked with.
    /// </summary>
    public required PluginIntentSurface Intent { get; init; }

    /// <summary>
    /// Gets the capabilities granted to the extension, as wire names, after implication.
    /// </summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>
    /// Gets the extension that supplies the kind.
    /// </summary>
    public required PluginId Plugin { get; init; }
}
