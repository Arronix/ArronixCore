
using System.Globalization;
using System.Text;
using Arronix.Abstractions.Languages;
using Arronix.Languages.Reference;

namespace Arronix.Plugin.Movies.Tests.Fixtures;

/// <summary>
/// Derives a comparison key by composing one language implementation with invariant host mechanics.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is not a second language vocabulary. It calls the same reference implementation the host
/// activates, then applies only invariant Unicode folding and alphanumeric keying.
/// </para>
/// <para>
/// Language operations and media parsing now have separate lifecycles: Movies no longer supplies words,
/// articles or transliterations.
/// </para>
/// </remarks>
internal static class DeclaredTitleKey
{
    private static readonly ILanguageDefinition English = new EnglishLanguageDefinition();

    /// <summary>Derives the comparison key of one title.</summary>
    /// <param name="title">The raw title.</param>
    /// <param name="language">The language implementation; English when omitted.</param>
    /// <returns>The key, or an empty string when the title is empty.</returns>
    internal static string Of(string? title, ILanguageDefinition? language = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var prepared = (language ?? English).PrepareComparison(title);
        var folded = Fold(prepared.ToLowerInvariant());
        return folded.Length == 0 ? Fold(title.ToLowerInvariant()) : folded;
    }

    private static string Fold(string value)
    {
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
