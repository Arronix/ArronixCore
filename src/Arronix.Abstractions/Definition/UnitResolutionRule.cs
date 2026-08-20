
namespace Arronix.Abstractions.Definition;

/// <summary>
/// How units are resolved within an entry for one release kind.
/// </summary>
public sealed record UnitResolutionRule
{
    /// <summary>
    /// Gets the release-kind discriminator captured at parse that selects this row, or null for the
    /// default row.
    /// </summary>
    public string? ReleaseKind { get; init; }

    /// <summary>
    /// Gets the resolution attempts, in order. Provenance gates — a space that only applies to release
    /// names — derive from the space's own declaration and are enforced by the engine; the row only
    /// orders the attempts.
    /// </summary>
    public required IReadOnlyList<SpaceAttempt> Spaces { get; init; }

    /// <summary>
    /// Gets how a span-scoped reading expands into member units.
    /// </summary>
    public SpanExpansion Expansion { get; init; } = SpanExpansion.None;
}
