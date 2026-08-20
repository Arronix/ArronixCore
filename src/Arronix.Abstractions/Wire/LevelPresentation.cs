using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// One level of a media kind, projected into everything a consumer needs to work with it.
/// </summary>
public sealed record LevelPresentation
{
    /// <summary>
    /// Gets the level.
    /// </summary>
    public required MediaLevelId Level { get; init; }

    /// <summary>
    /// Gets the level's display name for one item.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the level's display name for several items.
    /// </summary>
    public required string PluralName { get; init; }

    /// <summary>
    /// Gets the fields items at this level carry.
    /// </summary>
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }

    /// <summary>
    /// Gets what can be done with items at this level. Derived by the host from the validated shape and
    /// from what is configured, never declared by the extension: a declaration that can be derived is a
    /// declaration that can disagree with the thing it describes.
    /// </summary>
    public required IReadOnlyList<Affordance> Affordances { get; init; }

    /// <summary>
    /// Gets the actions offered at this level.
    /// </summary>
    public required IReadOnlyList<ActionDescriptor> Actions { get; init; }
}
