using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Naming;

/// <summary>
/// Contributes character folds that Unicode decomposition alone cannot express.
/// </summary>
/// <remarks>
/// <para>
/// Most accents fold by decomposing a string and dropping the combining marks. A handful of letters are
/// not accented forms of anything — eth and thorn are the standard examples — so folding them to a
/// Latin equivalent is a per-language editorial decision, not a Unicode operation. Making that decision
/// contributable is what removes the need for a third-party folding library.
/// </para>
/// <para>
/// Providers are additive. When two providers map the same character, the platform's own provider
/// yields to the contributed one.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Naming, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IDiacriticFoldingProvider
{
    /// <summary>
    /// Gets the BCP 47 language tag these folds apply to, or <see langword="null"/> when they apply
    /// regardless of language.
    /// </summary>
    string? Language { get; }

    /// <summary>
    /// Gets the folds this provider contributes, mapping a single character to the sequence that
    /// replaces it. A fold may expand to more than one character.
    /// </summary>
    IReadOnlyDictionary<char, string> Replacements { get; }
}
