using System.Linq;

namespace Arronix.Plugins.Manifest;

/// <summary>
/// The ordered policy identifiers an extension declares, by category.
/// </summary>
/// <remarks>
/// <para>
/// Five categories, closed. Because the reader rejects unmapped members, the closed set is enforced by the
/// type itself rather than by a list the validator has to keep in step — a category nobody implements is a
/// manifest that does not parse.
/// </para>
/// <para>
/// Cross-checking these identifiers against the handlers an extension actually supplies is not possible
/// until a policy engine exists. Until then the declaration is checked for self-consistency only, and that
/// limit is recorded rather than implied: when the engine lands, the registry gains one method and the
/// comparison here becomes exact, with no redesign.
/// </para>
/// </remarks>
public sealed record PolicyGraph
{
    /// <summary>
    /// Gets the ordered identifiers of the steps that turn a release name into components.
    /// </summary>
    public IReadOnlyList<string> Parsing { get; init; } = [];

    /// <summary>
    /// Gets the ordered identifiers of the steps that associate a release with items.
    /// </summary>
    public IReadOnlyList<string> Matching { get; init; } = [];

    /// <summary>
    /// Gets the ordered identifiers of the steps that rank and cut off quality.
    /// </summary>
    public IReadOnlyList<string> Quality { get; init; } = [];

    /// <summary>
    /// Gets the ordered identifiers of the steps that take files into the library.
    /// </summary>
    public IReadOnlyList<string> Import { get; init; } = [];

    /// <summary>
    /// Gets the ordered identifiers of the steps that materialize tokens into paths.
    /// </summary>
    public IReadOnlyList<string> Naming { get; init; } = [];

    /// <summary>
    /// Gets the category names, in declaration order.
    /// </summary>
    public static IReadOnlyList<string> CategoryNames { get; } =
        ["parsing", "matching", "quality", "import", "naming"];

    /// <summary>
    /// Lists the categories and their identifiers.
    /// </summary>
    /// <returns>One entry per category, including empty ones.</returns>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> Categories()
    {
        yield return new KeyValuePair<string, IReadOnlyList<string>>("parsing", Parsing);
        yield return new KeyValuePair<string, IReadOnlyList<string>>("matching", Matching);
        yield return new KeyValuePair<string, IReadOnlyList<string>>("quality", Quality);
        yield return new KeyValuePair<string, IReadOnlyList<string>>("import", Import);
        yield return new KeyValuePair<string, IReadOnlyList<string>>("naming", Naming);
    }

    /// <summary>
    /// Gets the total number of declared identifiers across every category.
    /// </summary>
    /// <returns>The count.</returns>
    public int TotalCount() => Categories().Sum(category => category.Value.Count);
}
