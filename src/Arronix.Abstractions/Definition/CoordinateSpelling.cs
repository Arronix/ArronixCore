
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One way the community writes a coordinate.
/// </summary>
public sealed record CoordinateSpelling
{
    /// <summary>
    /// Gets the spelling's identifier, referenced by alias templates and naming styles.
    /// </summary>
    public required string SpellingId { get; init; }

    /// <summary>
    /// Gets the coordinate space the spelling addresses.
    /// </summary>
    public required string SpaceId { get; init; }

    /// <summary>
    /// Gets the rendering template, where a run of zeros is a zero-padded component slot — the
    /// two-component <c>"S{00}E{00}"</c> form, a three-digit absolute <c>"{000}"</c>.
    /// </summary>
    public required string Template { get; init; }
}
