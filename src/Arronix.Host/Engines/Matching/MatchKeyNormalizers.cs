using System.Globalization;
using System.Text;
using Arronix.Abstractions.DTOs;
using Arronix.Host.Languages;

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The host's named key normalizers. A <c>MatchLayer.NormalizerId</c> names one of these; an unknown
/// identifier is a load failure at engine construction, never a silent fallback.
/// </summary>
/// <remarks>
/// Normalization is host code with host data, exactly like the agnostic tag layer: a key derived from
/// <c>"The Movie: Part II"</c> must collide with one derived from <c>"the movie part 2"</c> the same way
/// for every kind, or two kinds would disagree about what a title is. The <c>clean-title</c> chain ports
/// the semantics of Radarr's <c>Parser.CleanMovieTitle</c> as consumed by
/// <c>MovieService.FindByTitle</c> (<c>_reference/Radarr/src/NzbDrone.Core/Movies/MovieService.cs:126-158</c>):
/// case folding, diacritic stripping, ampersand spelling, and removal of everything that is not a letter
/// or digit.
/// </remarks>
internal sealed class MatchKeyNormalizers(LanguageTextService languages)
{
    private readonly LanguageTextService _languages = languages ?? throw new ArgumentNullException(nameof(languages));

    /// <summary>The full title-cleaning chain.</summary>
    internal const string CleanTitle = "clean-title";

    /// <summary>Case folding and alphanumeric stripping only, with no word rewrites.</summary>
    internal const string StripNonAlphanumericUpper = "strip-non-alnum-upper";

    /// <summary>
    /// Determines whether a normalizer identifier is registered.
    /// </summary>
    /// <param name="normalizerId">The identifier to check.</param>
    /// <returns><see langword="true"/> when the identifier names a host normalizer.</returns>
    internal static bool Exists(string normalizerId) =>
        normalizerId is CleanTitle or StripNonAlphanumericUpper;

    /// <summary>
    /// Runs one normalizer.
    /// </summary>
    /// <param name="normalizerId">The normalizer to run.</param>
    /// <param name="text">The text to normalize.</param>
    /// <param name="language">The language of the text, when stated.</param>
    /// <returns>The derived key.</returns>
    /// <exception cref="InvalidOperationException">The identifier names no host normalizer.</exception>
    internal IReadOnlySet<string> Normalize(
        string normalizerId,
        string text,
        Language? language = null) => normalizerId switch
    {
        CleanTitle => _languages.ComparisonKeys(text, language),
        StripNonAlphanumericUpper => OneInvariantKey(text),
        _ => throw new InvalidOperationException(
            $"'{normalizerId}' names no host key normalizer. Registered: '{CleanTitle}', '{StripNonAlphanumericUpper}'."),
    };

    /// <summary>Runs a language-neutral normalizer for identifiers which cannot carry language.</summary>
    internal static string NormalizeInvariant(string normalizerId, string text) => normalizerId switch
    {
        CleanTitle or StripNonAlphanumericUpper => StripToAlphanumericUpper(text),
        _ => throw new InvalidOperationException(
            $"'{normalizerId}' names no host key normalizer. Registered: '{CleanTitle}', '{StripNonAlphanumericUpper}'."),
    };

    private static IReadOnlySet<string> OneInvariantKey(string text)
    {
        var key = StripToAlphanumericUpper(text);
        return key.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal) { $"und:{key}" };
    }

    private static string StripToAlphanumericUpper(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var rune in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(rune))
            {
                builder.Append(char.ToUpperInvariant(rune));
            }
        }

        return builder.ToString();
    }
}
