
namespace Arronix.Abstractions.Definition;

/// <summary>
/// How coordinates are spelled in the community's release grammar.
/// </summary>
/// <remarks>
/// One grammar serves the query templater and the naming renderer: the way a release community writes a
/// position is the way queries must ask for it and file names must state it. A kind with a singleton
/// coordinate space has nothing to spell and declares <see cref="None"/>.
/// </remarks>
public sealed record CoordinateGrammar
{
    /// <summary>
    /// Gets the empty grammar, for kinds whose releases spell no coordinates.
    /// </summary>
    public static CoordinateGrammar None { get; } = new();

    /// <summary>
    /// Gets the spellings, one per way the community writes a position.
    /// </summary>
    public IReadOnlyList<CoordinateSpelling> Spellings { get; init; } = [];
}
