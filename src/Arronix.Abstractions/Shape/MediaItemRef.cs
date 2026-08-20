using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// The host's only handle on an item owned by a media extension: kind, level and identifier, always
/// together.
/// </summary>
/// <param name="Kind">The media kind the item belongs to.</param>
/// <param name="Level">The level of that kind's hierarchy the item sits at.</param>
/// <param name="Id">The item's identifier, unique within its kind and level.</param>
/// <remarks>
/// The three travel as one value because none of them identifies anything on its own: identifiers are
/// minted per level, levels are declared per kind, and a bare integer crossing a queue, a job parameter
/// or an event payload is unresolvable. Everything the host stores about an item — monitoring, file
/// links, group membership — is keyed by this triple.
/// </remarks>
public readonly record struct MediaItemRef(MediaKindId Kind, MediaLevelId Level, MediaItemId Id)
{
    /// <summary>
    /// Gets the diagnostic form <c>kind:level:id</c>.
    /// </summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => $"{Kind}:{Level}:{Id}";
}
