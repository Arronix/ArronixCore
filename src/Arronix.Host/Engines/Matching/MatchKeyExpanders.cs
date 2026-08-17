using System.Linq;

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The host's named key expanders: each multiplies a derived key into the accepted variants of itself.
/// A <c>MatchLayer.ExpanderIds</c> entry names one of these; an unknown identifier is a load failure.
/// </summary>
/// <remarks>
/// The roman-numeral expander ports the arabic-to-roman and roman-to-arabic rewrites Radarr applies to
/// the search title before candidate lookup
/// (<c>_reference/Radarr/src/NzbDrone.Core/Movies/MovieService.cs:160-186</c>,
/// <c>FindByTitleCandidates</c>): both rewrites of the reading-side key are accepted, and the entry-side
/// keys are never rewritten — a numeral rewrite of a stored rewrite would compound.
/// </remarks>
internal static class MatchKeyExpanders
{
    /// <summary>Accepts the arabic and roman spellings of any numeral in the key.</summary>
    internal const string RomanNumeralVariants = "roman-numeral-variants";

    private static readonly (string Arabic, string Roman)[] Numerals =
    [
        ("20", "XX"),
        ("19", "XIX"),
        ("18", "XVIII"),
        ("17", "XVII"),
        ("16", "XVI"),
        ("15", "XV"),
        ("14", "XIV"),
        ("13", "XIII"),
        ("12", "XII"),
        ("11", "XI"),
        ("10", "X"),
        ("9", "IX"),
        ("8", "VIII"),
        ("7", "VII"),
        ("6", "VI"),
        ("5", "V"),
        ("4", "IV"),
        ("3", "III"),
        ("2", "II"),
        ("1", "I"),
    ];

    /// <summary>
    /// Determines whether an expander identifier is registered.
    /// </summary>
    /// <param name="expanderId">The identifier to check.</param>
    /// <returns><see langword="true"/> when the identifier names a host expander.</returns>
    internal static bool Exists(string expanderId) => expanderId is RomanNumeralVariants;

    /// <summary>
    /// Runs one expander over an already-normalized key.
    /// </summary>
    /// <param name="expanderId">The expander to run.</param>
    /// <param name="key">The key to expand.</param>
    /// <returns>The accepted variants, not including the key itself.</returns>
    /// <exception cref="InvalidOperationException">The identifier names no host expander.</exception>
    internal static IReadOnlyList<string> Expand(string expanderId, string key)
    {
        if (expanderId is not RomanNumeralVariants)
        {
            throw new InvalidOperationException(
                $"'{expanderId}' names no host key expander. Registered: '{RomanNumeralVariants}'.");
        }

        var romanized = key;
        var arabicized = key;

        foreach (var (arabic, roman) in Numerals)
        {
            romanized = romanized.Replace(arabic, roman, StringComparison.Ordinal);
            arabicized = arabicized.Replace(roman, arabic, StringComparison.Ordinal);
        }

        return new[] { romanized, arabicized }
            .Where(variant => !string.Equals(variant, key, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
