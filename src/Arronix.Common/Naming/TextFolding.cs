using System.Globalization;
using System.Linq;
using System.Text;
using Arronix.Abstractions.Naming;

// The naming contract is experimental. This file consumes the folding provider contract to assemble the
// table it applies; it takes no other dependency on the unstable surface.
#pragma warning disable ARX0009

namespace Arronix.Common.Naming;

/// <summary>
/// Folds accented and non-Latin-alphabet letters down to their unaccented equivalents so that two spellings
/// of the same title compare equal.
/// </summary>
/// <remarks>
/// <para>
/// Folding is two operations in sequence. First the contributed folds are applied, because they map letters
/// that are not accented forms of anything and so survive normalization untouched. Then the text is
/// decomposed into base characters plus combining marks, the marks are dropped, and what is left is
/// recomposed. That second step is a property of Unicode itself and needs no table.
/// </para>
/// <para>
/// This replaces a third-party folding library whose only contribution beyond the framework was a
/// three-entry table. Doing it here means the table is contributable — a component that knows a language
/// supplies the folds that language wants, rather than every installation carrying one library's opinion.
/// </para>
/// </remarks>
public static class TextFolding
{
    /// <summary>
    /// Folds text using Unicode decomposition alone.
    /// </summary>
    /// <param name="text">The text to fold.</param>
    /// <returns>
    /// The text with its combining marks removed. Letters that are not decomposable are returned unchanged;
    /// use <see cref="Fold(string, IReadOnlyDictionary{char, string})"/> to fold those as well.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static string Fold(string text) => Fold(text, FrozenFolds.None);

    /// <summary>
    /// Folds text, applying contributed folds before Unicode decomposition.
    /// </summary>
    /// <param name="text">The text to fold.</param>
    /// <param name="replacements">
    /// Single characters mapped to the sequences that replace them, as produced by
    /// <see cref="BuildFoldTable"/>. A replacement may be longer than one character.
    /// </param>
    /// <returns>The folded text.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="text"/> or <paramref name="replacements"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Text containing an unpaired surrogate cannot be normalized, and a title that arrived from a remote
    /// index is not guaranteed to be well-formed. Rather than letting the framework's normalization throw
    /// from inside a rename or a comparison, such text is returned with the contributed folds applied and
    /// the decomposition step skipped — degraded, but never a failure in a path whose whole purpose is to be
    /// forgiving about spelling.
    /// </remarks>
    public static string Fold(string text, IReadOnlyDictionary<char, string> replacements)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(replacements);

        if (text.Length == 0)
        {
            return text;
        }

        var replaced = ApplyReplacements(text, replacements);

        try
        {
            return RemoveCombiningMarks(replaced);
        }
        catch (ArgumentException)
        {
            // The text holds an unpaired surrogate; normalization is not defined for it.
            return replaced;
        }
    }

    /// <summary>
    /// Merges the folds contributed by a set of providers into one table.
    /// </summary>
    /// <param name="providers">The contributing providers, in any order.</param>
    /// <param name="languageTag">
    /// The BCP 47 tag of the text about to be folded, or <see langword="null"/> to take only the folds that
    /// apply regardless of language. A provider declaring a language matches when the tag is that language
    /// or a region-qualified form of it.
    /// </param>
    /// <returns>A table mapping each character to the sequence that replaces it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Where two providers claim the same character, the platform's own table yields: its entries are a
    /// floor that anyone who knows better may raise, not a ceiling. Ordering is decided here rather than
    /// left to registration order, so a host that reorders its registrations does not silently change how
    /// titles fold.
    /// </remarks>
    public static IReadOnlyDictionary<char, string> BuildFoldTable(
        IEnumerable<IDiacriticFoldingProvider> providers,
        string? languageTag = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var table = new Dictionary<char, string>();

        var applicable = providers
            .Where(provider => Applies(provider, languageTag))
            .OrderBy(static provider => provider is DefaultDiacriticFoldingProvider ? 0 : 1);

        foreach (var provider in applicable)
        {
            foreach (var fold in provider.Replacements)
            {
                table[fold.Key] = fold.Value;
            }
        }

        return table;
    }

    private static bool Applies(IDiacriticFoldingProvider provider, string? languageTag)
    {
        if (provider.Language is null)
        {
            return true;
        }

        if (languageTag is null)
        {
            return false;
        }

        return languageTag.Equals(provider.Language, StringComparison.OrdinalIgnoreCase)
            || (languageTag.StartsWith(provider.Language, StringComparison.OrdinalIgnoreCase)
                && languageTag.Length > provider.Language.Length
                && languageTag[provider.Language.Length] == '-');
    }

    private static string ApplyReplacements(string text, IReadOnlyDictionary<char, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return text;
        }

        StringBuilder? builder = null;

        for (var index = 0; index < text.Length; index++)
        {
            if (!replacements.TryGetValue(text[index], out var replacement))
            {
                builder?.Append(text[index]);
                continue;
            }

            builder ??= new StringBuilder(text.Length).Append(text, 0, index);
            builder.Append(replacement);
        }

        return builder?.ToString() ?? text;
    }

    private static string RemoveCombiningMarks(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Holds the empty table, so the parameterless overload allocates nothing per call.
    /// </summary>
    private static class FrozenFolds
    {
        internal static readonly IReadOnlyDictionary<char, string> None =
            new Dictionary<char, string>();
    }
}
