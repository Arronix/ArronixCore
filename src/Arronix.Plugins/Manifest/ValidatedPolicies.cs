using System.Collections.ObjectModel;
using System.Linq;

namespace Arronix.Plugins.Manifest;

/// <summary>
/// The proved policy identifiers a declaration states, by category.
/// </summary>
/// <remarks>
/// An immutable snapshot rather than the deserialized graph. Each category is copied into a read-only
/// collection at construction, so nothing that reaches the loader can be edited by whoever supplied the
/// declaration.
/// </remarks>
public sealed class ValidatedPolicies
{
    internal ValidatedPolicies(
        IReadOnlyList<string> parsing,
        IReadOnlyList<string> matching,
        IReadOnlyList<string> quality,
        IReadOnlyList<string> import,
        IReadOnlyList<string> naming)
    {
        Parsing = parsing.ToList().AsReadOnly();
        Matching = matching.ToList().AsReadOnly();
        Quality = quality.ToList().AsReadOnly();
        Import = import.ToList().AsReadOnly();
        Naming = naming.ToList().AsReadOnly();
    }

    /// <summary>Gets the declaration that states no policy at all.</summary>
    public static ValidatedPolicies Empty { get; } = new([], [], [], [], []);

    /// <summary>Gets the steps that turn a release name into components.</summary>
    public ReadOnlyCollection<string> Parsing { get; }

    /// <summary>Gets the steps that associate a release with items.</summary>
    public ReadOnlyCollection<string> Matching { get; }

    /// <summary>Gets the steps that rank and cut off quality.</summary>
    public ReadOnlyCollection<string> Quality { get; }

    /// <summary>Gets the steps that take files into the library.</summary>
    public ReadOnlyCollection<string> Import { get; }

    /// <summary>Gets the steps that materialize tokens into paths.</summary>
    public ReadOnlyCollection<string> Naming { get; }

    /// <summary>Lists the categories and their identifiers, including empty ones.</summary>
    /// <returns>One entry per category, in declaration order.</returns>
    public IEnumerable<KeyValuePair<string, ReadOnlyCollection<string>>> Categories()
    {
        yield return new KeyValuePair<string, ReadOnlyCollection<string>>("parsing", Parsing);
        yield return new KeyValuePair<string, ReadOnlyCollection<string>>("matching", Matching);
        yield return new KeyValuePair<string, ReadOnlyCollection<string>>("quality", Quality);
        yield return new KeyValuePair<string, ReadOnlyCollection<string>>("import", Import);
        yield return new KeyValuePair<string, ReadOnlyCollection<string>>("naming", Naming);
    }

    /// <summary>Gets the total number of declared identifiers across every category.</summary>
    /// <returns>The count.</returns>
    public int TotalCount() => Categories().Sum(category => category.Value.Count);
}
