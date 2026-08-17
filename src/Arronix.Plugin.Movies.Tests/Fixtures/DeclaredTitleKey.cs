#pragma warning disable ARX0019 // Definition contracts are experimental; these tests exercise them.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;
using Arronix.Plugin.Movies.Definition;

namespace Arronix.Plugin.Movies.Tests.Fixtures;

/// <summary>
/// Derives a comparison key by running the movie definition's own declared normalization data.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not an implementation of the normalization chain: the chain is host code and is
/// tested against the host's engines. What lives here is the smallest driver that applies the
/// <i>declared</i> rows — the transliteration table and the stop-word expression — so that a fixture index
/// and the corpus assertions read from the declaration rather than from a second copy of it. If a row is
/// dropped from the declaration these tests change answer, which is the whole point.
/// </para>
/// <para>
/// The host's <c>clean-title</c> key normalizer folds case and diacritics and strips to alphanumerics;
/// the kind-declared part is the stop-word expression and the umlaut rows, and only those are applied
/// here before the host's fold.
/// </para>
/// </remarks>
internal static class DeclaredTitleKey
{
    private static readonly NormalizationOptions Declared = MoviesParsing.Build().Normalization;

    private static readonly Regex StopWords = new(
        Declared.StopWordExpression!,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>The declared normalization rows the key is derived from.</summary>
    internal static NormalizationOptions Options => Declared;

    /// <summary>Derives the comparison key of one title.</summary>
    /// <param name="title">The raw title.</param>
    /// <returns>The key, or an empty string when the title is empty.</returns>
    internal static string Of(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var working = Transliterate(title);
        var stripped = StopWords.Replace(working, string.Empty).ToLowerInvariant();
        var folded = Fold(stripped);

        // The declared guard two of the four surveyed forks lack: a normalization that consumes the whole
        // text falls back to the raw form rather than yielding an empty key.
        return folded.Length == 0 && Declared.EmptyResultFallsBackToRaw
            ? Fold(title.ToLowerInvariant())
            : folded;
    }

    private static string Transliterate(string value)
    {
        foreach (var rule in Declared.Transliterations)
        {
            value = value.Replace(rule.From, rule.To, StringComparison.Ordinal);
        }

        return value;
    }

    private static string Fold(string value)
    {
        if (!Declared.FoldDiacritics)
        {
            return value;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var rune in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(rune))
            {
                builder.Append(rune);
            }
        }

        return builder.ToString();
    }
}
