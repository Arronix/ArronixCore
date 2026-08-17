using System.Globalization;
using System.Text.RegularExpressions;
using Arronix.Common.Naming;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// The closed, media-agnostic modifier vocabulary a template may apply to a token value.
/// </summary>
/// <remarks>
/// Closed by design (<c>docs/design/naming-and-tokens.md</c> resolution #3): an open transform vocabulary
/// is a plugin-supplied formatter with no validation. Kind-specific transforms are declared as fields,
/// never as ninth modifiers.
/// </remarks>
internal enum NamingModifier
{
    /// <summary>Scene punctuation stripped, <c>&amp;</c> → <c>and</c>, diacritics folded.</summary>
    Clean = 0,

    /// <summary>Leading article moved to the end: <c>The Expanse</c> → <c>Expanse, The</c>.</summary>
    The = 1,

    /// <summary>Appends <c> (YYYY)</c> from the bound year, when not already present.</summary>
    Year = 2,

    /// <summary>Strips a trailing <c> (YYYY)</c>.</summary>
    NoYear = 3,

    /// <summary>First alphanumeric grapheme, folded and upper-cased; <c>_</c> when there is none.</summary>
    First = 4,

    /// <summary>Diacritic folding only.</summary>
    Fold = 5,

    /// <summary>Lower-cases the value.</summary>
    Lower = 6,

    /// <summary>Upper-cases the value.</summary>
    Upper = 7,

    /// <summary>Title-cases the value — the one casing the surveyed implicit rule could not express.</summary>
    Title = 8,

    /// <summary>Spaces become dots.</summary>
    Dot = 9,

    /// <summary>Spaces become hyphens.</summary>
    Kebab = 10,

    /// <summary>Spaces become underscores.</summary>
    Snake = 11,
}

/// <summary>
/// Parses and applies the modifier vocabulary.
/// </summary>
/// <remarks>
/// The transforms are ported from the four near-identical plugin formatter copies this engine deletes —
/// <c>Arronix.Plugin.Movies/MoviesNaming.cs</c> (<c>CleanTitle</c>:254, <c>TitleThe</c>:271,
/// <c>TitleFirstCharacter</c>:299) — themselves ports of Sonarr's
/// <c>src/NzbDrone.Core/Organizer/FileNameBuilder.cs:302-381</c>. Folding goes through
/// <see cref="TextFolding"/> rather than a private copy.
/// </remarks>
internal static partial class NamingModifiers
{
    /// <summary>
    /// Resolves a modifier name written in a template.
    /// </summary>
    /// <param name="text">The name after the colon.</param>
    /// <param name="modifier">The modifier, when recognized.</param>
    /// <returns>Whether the name is part of the vocabulary.</returns>
    public static bool TryParse(string text, out NamingModifier modifier)
    {
        switch (text.ToLowerInvariant())
        {
            case "clean":
                modifier = NamingModifier.Clean;
                return true;
            case "the":
                modifier = NamingModifier.The;
                return true;
            case "year":
                modifier = NamingModifier.Year;
                return true;
            case "noyear":
                modifier = NamingModifier.NoYear;
                return true;
            case "first":
                modifier = NamingModifier.First;
                return true;
            case "fold":
                modifier = NamingModifier.Fold;
                return true;
            case "lower":
                modifier = NamingModifier.Lower;
                return true;
            case "upper":
                modifier = NamingModifier.Upper;
                return true;
            case "title":
                modifier = NamingModifier.Title;
                return true;
            case "dot":
                modifier = NamingModifier.Dot;
                return true;
            case "kebab":
                modifier = NamingModifier.Kebab;
                return true;
            case "snake":
                modifier = NamingModifier.Snake;
                return true;
            default:
                modifier = default;
                return false;
        }
    }

    /// <summary>
    /// Applies one modifier to a value.
    /// </summary>
    /// <param name="value">The token value.</param>
    /// <param name="modifier">The modifier.</param>
    /// <param name="year">The year bound beside the token's level, for <see cref="NamingModifier.Year"/>.</param>
    /// <returns>The transformed value.</returns>
    public static string Apply(string value, NamingModifier modifier, int? year)
    {
        if (value.Length == 0 && modifier != NamingModifier.First)
        {
            return value;
        }

        return modifier switch
        {
            NamingModifier.Clean => Clean(value),
            NamingModifier.The => MoveArticle(value),
            NamingModifier.Year => AppendYear(value, year),
            NamingModifier.NoYear => TrailingYear().Replace(value, string.Empty),
            NamingModifier.First => FirstCharacter(value),
            NamingModifier.Fold => TextFolding.Fold(value),
            NamingModifier.Lower => value.ToLowerInvariant(),
            NamingModifier.Upper => value.ToUpperInvariant(),
            NamingModifier.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant()),
            NamingModifier.Dot => value.Replace(' ', '.'),
            NamingModifier.Kebab => value.Replace(' ', '-'),
            NamingModifier.Snake => value.Replace(' ', '_'),
            _ => value,
        };
    }

    private static string Clean(string value)
    {
        var replaced = value.Replace("&", "and", StringComparison.Ordinal);
        replaced = ScenifyReplace().Replace(replaced, " ");
        replaced = ScenifyRemove().Replace(replaced, string.Empty);

        return TextFolding.Fold(replaced).Trim();
    }

    private static string MoveArticle(string value) => TitlePrefix().Replace(value, "$2, $1$3");

    private static string AppendYear(string value, int? year)
    {
        if (year is not { } bound || bound <= 0 || TrailingYear().IsMatch(value))
        {
            return value;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value} ({bound})");
    }

    private static string FirstCharacter(string value)
    {
        // Only the first two positions are probed, matching the surveyed behavior: a title opening with
        // two symbols files under '_' rather than under whatever letter appears later.
        foreach (var character in value.Length > 2 ? value[..2] : value)
        {
            if (char.IsLetterOrDigit(character))
            {
                return TextFolding.Fold(char.ToUpperInvariant(character).ToString());
            }
        }

        return "_";
    }

    [GeneratedRegex(
        @"(?<=\s)(,|<|>|\/|\\|;|:|'|""|\||`|’|~|!|\?|@|\$|%|\^|\*|-|_|=){1}(?=\s)|('|`|’|:|\?|,)(?=(?:(?:s|m|t|ve|ll|d|re)\s)|\s|$)|(\(|\)|\[|\]|\{|\})",
        RegexOptions.IgnoreCase)]
    private static partial Regex ScenifyRemove();

    [GeneratedRegex(@"[\/]")]
    private static partial Regex ScenifyReplace();

    [GeneratedRegex(@"^(The|An|A) (.*?)((?: *\([^)]+\))*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TitlePrefix();

    [GeneratedRegex(@" \((19|20)\d{2}\)$")]
    private static partial Regex TrailingYear();
}
