using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Everything an extension declares about how its media kind is worked with.
/// </summary>
/// <remarks>
/// <para>
/// Field and setting semantics are deliberately not re-declared here: they are already declared once, in
/// the shape and in the provider settings schema, and that reuse is the point. One declaration, several
/// consumers, nothing that can disagree.
/// </para>
/// <para>
/// The host is the sole publisher of this surface. It is validated at load and serialized by the host, so
/// an action, a working surface or an ability the extension's capabilities do not support is refused
/// outright rather than filtered out later — which is what keeps the residual blast radius of a hostile
/// declaration down to a misleading label.
/// </para>
/// </remarks>
public sealed record PluginIntentSurface
{
    /// <summary>
    /// Gets the media kind this surface belongs to.
    /// </summary>
    public required MediaKindId MediaKind { get; init; }

    /// <summary>
    /// Gets the ways the kind's items can be traversed.
    /// </summary>
    public IReadOnlyList<BrowseAxis> BrowseAxes { get; init; } = [];

    /// <summary>
    /// Gets the orderings the kind's items may be listed in.
    /// </summary>
    public IReadOnlyList<SortOption> Sorts { get; init; } = [];

    /// <summary>
    /// Gets the ways the kind's items may be narrowed.
    /// </summary>
    public IReadOnlyList<FilterOption> Filters { get; init; } = [];

    /// <summary>
    /// Gets the things a user may ask the platform to do with the kind.
    /// </summary>
    public IReadOnlyList<ActionDescriptor> Actions { get; init; } = [];

    /// <summary>
    /// Gets the conditions the kind's items can be in.
    /// </summary>
    public IReadOnlyList<StateDescriptor> States { get; init; } = [];

    /// <summary>
    /// Gets the representations of the kind's items that live outside the platform.
    /// </summary>
    public IReadOnlyList<ExternalSurfaceDescriptor> ExternalSurfaces { get; init; } = [];

    /// <summary>
    /// Gets the working surfaces the kind offers.
    /// </summary>
    public IReadOnlyList<WorkbenchDescriptor> Workbenches { get; init; } = [];
}
