using System.ComponentModel;
using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Languages;

/// <summary>Language-owned transformations used when titles are compared, queried, named and sorted.</summary>
/// <remarks>
/// The platform owns when each operation is needed; a language implementation owns what its words and
/// letters mean. The methods are ordinary typed code rather than regular-expression rows interpreted by
/// Host, so articles, stop words and transliteration do not become a core vocabulary.
/// </remarks>
public interface ILanguageDefinition
{
    /// <summary>Gets the BCP 47 language this implementation owns.</summary>
    Language Language { get; }

    /// <summary>Applies language-specific equivalence before invariant comparison-key cleaning.</summary>
    string PrepareComparison(string text);

    /// <summary>Produces the spelling sent to a search provider.</summary>
    string PrepareQuery(string text);

    /// <summary>Produces the spelling cleaned into a file or folder name before invariant punctuation rules.</summary>
    string PrepareFileName(string text);

    /// <summary>Produces the spelling used for alphabetical sorting.</summary>
    string PrepareSort(string text);
}

/// <summary>A language implementation type admitted for host-owned activation.</summary>
/// <param name="ImplementationType">The concrete implementation type.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record LanguageDefinitionRegistration(Type ImplementationType)
{
    /// <summary>Creates a registration without constructing the implementation in the plugin module.</summary>
    public static LanguageDefinitionRegistration For<TDefinition>()
        where TDefinition : class, ILanguageDefinition
        => new(typeof(TDefinition));
}
