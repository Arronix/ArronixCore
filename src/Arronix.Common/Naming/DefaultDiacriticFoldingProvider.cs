using System.Collections.Frozen;
using Arronix.Abstractions.Naming;

// The naming contract is experimental while its only implementer is the platform's own default table.
#pragma warning disable ARX0009

namespace Arronix.Common.Naming;

/// <summary>
/// The folds the platform ships with: the Latin letters that Unicode decomposition cannot fold because they
/// are not accented forms of anything.
/// </summary>
/// <remarks>
/// <para>
/// Decomposing a string and dropping its combining marks folds every accented Latin letter — é becomes e, ñ
/// becomes n — because those characters really are a base letter plus a mark. Eth and thorn are not. They
/// are letters in their own right, so no amount of normalization turns them into anything, and choosing what
/// they should become is an editorial decision rather than a Unicode operation.
/// </para>
/// <para>
/// The set is deliberately tiny. Every other non-decomposable letter — the Scandinavian slashed o, the
/// Polish stroked l, the ligatures — folds differently depending on the language of the text, so those
/// belong to whichever component knows the language, contributed through the same contract. The platform
/// claims only the folds that are uncontroversial in every language that uses them.
/// </para>
/// </remarks>
public sealed class DefaultDiacriticFoldingProvider : IDiacriticFoldingProvider
{
    /// <summary>
    /// The fold table, frozen because it is read on every folding operation and never written after
    /// construction.
    /// </summary>
    /// <remarks>
    /// Both cases of both letters are present. Carrying the lowercase thorn without the uppercase one — as
    /// the table this replaces did — folds "thor" in a lowercase title and leaves it unfolded in a title
    /// that happens to start the word, so the same text matches or fails to match depending on its casing.
    /// </remarks>
    private static readonly FrozenDictionary<char, string> Folds = new Dictionary<char, string>
    {
        ['ð'] = "d",   // LATIN SMALL LETTER ETH
        ['Ð'] = "D",   // LATIN CAPITAL LETTER ETH
        ['þ'] = "th",  // LATIN SMALL LETTER THORN
        ['Þ'] = "Th",  // LATIN CAPITAL LETTER THORN
    }.ToFrozenDictionary();

    /// <inheritdoc />
    /// <remarks>
    /// The folds apply regardless of language: no language that uses eth or thorn wants them left in a
    /// folded string, and no language that does not use them is affected.
    /// </remarks>
    public string? Language => null;

    /// <inheritdoc />
    public IReadOnlyDictionary<char, string> Replacements => Folds;
}
