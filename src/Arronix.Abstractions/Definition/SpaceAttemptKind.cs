
namespace Arronix.Abstractions.Definition;

/// <summary>
/// What a unit-resolution attempt does.
/// </summary>
public enum SpaceAttemptKind
{
    /// <summary>Resolve the reading's coordinates in the named space.</summary>
    Coordinate = 0,

    /// <summary>Resolve leftover title text against unit titles through the named normalizer.</summary>
    TitleLookup = 1
}
