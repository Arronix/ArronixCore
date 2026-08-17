using System.Text.RegularExpressions;
using Arronix.Abstractions.DTOs;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Reads the languages a release title names.
/// </summary>
/// <remarks>
/// <para>
/// A port of <c>Arronix.Plugin.Movies/MoviesReleaseParser.cs</c> (<c>MovieLanguageReader</c>), whose own
/// remarks record that the surveyed six-hundred-line implementations
/// (Radarr <c>src/NzbDrone.Core/Parser/LanguageParser.cs</c> and its near-identical television copy) are
/// not media semantics at all — a title says <c>GERMAN</c> the same way whatever it contains — which is
/// why the table lives once, host-side, per the parsing design's kind-agnostic layer.
/// </para>
/// <para>
/// What is kept is the part with the highest hit rate — spelled-out names and unambiguous short codes —
/// plus the one genuinely non-obvious rule: a German release tagged <c>DL</c> is dual language and also
/// carries the original, and one tagged <c>ML</c> carries the original and English too.
/// </para>
/// </remarks>
internal static partial class ReleaseLanguageScanner
{
    private static readonly (string Token, Language Language)[] SpelledOut =
    [
        ("english", new Language("en", "English")),
        ("spanish", new Language("es", "Spanish")),
        ("latino", new Language("es-419", "Spanish (Latino)")),
        ("castellano", new Language("es", "Spanish")),
        ("french", new Language("fr", "French")),
        ("truefrench", new Language("fr", "French")),
        ("german", new Language("de", "German")),
        ("italian", new Language("it", "Italian")),
        ("dutch", new Language("nl", "Dutch")),
        ("flemish", new Language("nl-BE", "Flemish")),
        ("danish", new Language("da", "Danish")),
        ("swedish", new Language("sv", "Swedish")),
        ("norwegian", new Language("no", "Norwegian")),
        ("finnish", new Language("fi", "Finnish")),
        ("icelandic", new Language("is", "Icelandic")),
        ("japanese", new Language("ja", "Japanese")),
        ("korean", new Language("ko", "Korean")),
        ("mandarin", new Language("zh", "Chinese")),
        ("cantonese", new Language("zh", "Chinese")),
        ("chinese", new Language("zh", "Chinese")),
        ("russian", new Language("ru", "Russian")),
        ("ukrainian", new Language("uk", "Ukrainian")),
        ("polish", new Language("pl", "Polish")),
        ("czech", new Language("cs", "Czech")),
        ("slovak", new Language("sk", "Slovak")),
        ("hungarian", new Language("hu", "Hungarian")),
        ("romanian", new Language("ro", "Romanian")),
        ("bulgarian", new Language("bg", "Bulgarian")),
        ("greek", new Language("el", "Greek")),
        ("turkish", new Language("tr", "Turkish")),
        ("hebrew", new Language("he", "Hebrew")),
        ("arabic", new Language("ar", "Arabic")),
        ("persian", new Language("fa", "Persian")),
        ("hindi", new Language("hi", "Hindi")),
        ("bengali", new Language("bn", "Bengali")),
        ("tamil", new Language("ta", "Tamil")),
        ("telugu", new Language("te", "Telugu")),
        ("malayalam", new Language("ml", "Malayalam")),
        ("kannada", new Language("kn", "Kannada")),
        ("marathi", new Language("mr", "Marathi")),
        ("thai", new Language("th", "Thai")),
        ("vietnamese", new Language("vi", "Vietnamese")),
        ("tagalog", new Language("tl", "Tagalog")),
        ("brazilian", new Language("pt-BR", "Portuguese (Brazil)")),
        ("portuguese", new Language("pt", "Portuguese")),
        ("latvian", new Language("lv", "Latvian")),
        ("lithuanian", new Language("lt", "Lithuanian")),
        ("albanian", new Language("sq", "Albanian")),
        ("afrikaans", new Language("af", "Afrikaans")),
        ("catalan", new Language("ca", "Catalan"))
    ];

    /// <summary>
    /// Gets the marker for "whatever the work was made in", which a dual-audio release also carries.
    /// </summary>
    internal static Language Original { get; } = new("original", "Original");

    /// <summary>Reads the languages a title names.</summary>
    /// <param name="title">The release title, ideally with the work's own title already masked out.</param>
    /// <returns>The languages, or empty when the title named none.</returns>
    internal static IReadOnlyList<Language> Scan(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var found = new List<Language>();

        foreach (var (token, language) in SpelledOut)
        {
            if (title.Contains(token, StringComparison.OrdinalIgnoreCase) && !found.Contains(language))
            {
                found.Add(language);
            }
        }

        foreach (Match match in ShortCodes().Matches(title))
        {
            var language = FromShortCode(match);

            if (language is not null && !found.Contains(language))
            {
                found.Add(language);
            }
        }

        if (found.Count == 0)
        {
            return [];
        }

        // The German dual-audio convention. Only applied when German is the sole language found: a
        // release that already names two languages is not making this claim.
        if (found.Count == 1 && string.Equals(found[0].Code, "de", StringComparison.Ordinal))
        {
            if (GermanDualLanguage().IsMatch(title))
            {
                found.Add(Original);
            }
            else if (GermanMultiLanguage().IsMatch(title))
            {
                found.Add(Original);
                found.Add(new Language("en", "English"));
            }
        }

        return found;
    }

    private static Language? FromShortCode(Match match)
    {
        if (match.Groups["french"].Success)
        {
            return new Language("fr", "French");
        }

        if (match.Groups["russian"].Success)
        {
            return new Language("ru", "Russian");
        }

        if (match.Groups["german"].Success)
        {
            return new Language("de", "German");
        }

        if (match.Groups["japanese"].Success)
        {
            return new Language("ja", "Japanese");
        }

        if (match.Groups["korean"].Success)
        {
            return new Language("ko", "Korean");
        }

        if (match.Groups["hungarian"].Success)
        {
            return new Language("hu", "Hungarian");
        }

        if (match.Groups["polish"].Success)
        {
            return new Language("pl", "Polish");
        }

        return match.Groups["original"].Success ? Original : null;
    }

    // Only codes that cannot be anything else. "ES" is excluded on purpose: it is the tail of DTS-ES and
    // the surveyed implementation needs a negative lookbehind to keep it from claiming Spanish on every
    // high-end audio track.
    [GeneratedRegex(
        @"(?:\W|_|^)(?:(?<french>TRUEFRENCH|VFF|VFQ|VFI|VF2)|(?<russian>RUS)|(?<german>GER[. ]DUB)|(?<japanese>JAP)|(?<korean>KOR)|(?<hungarian>HUNDUB)|(?<polish>PL\W?DUB|DUB\W?PL)|(?<original>orig|original))(?:\W|_|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex ShortCodes();

    [GeneratedRegex(
        @"(?<!WEB[-_. ]?)\bDL\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex GermanDualLanguage();

    [GeneratedRegex(
        @"\bML\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex GermanMultiLanguage();
}
