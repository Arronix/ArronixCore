using System.Globalization;
using System.Text.RegularExpressions;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Reads the group that published a release out of its title.
/// </summary>
/// <remarks>
/// A port of Radarr's <c>src/NzbDrone.Core/Parser/ReleaseGroupParser.cs</c> via the modernized copy in
/// <c>Arronix.Plugin.Movies/MoviesReleaseParser.cs</c> (<c>MovieReleaseGroupParser</c>), order preserved:
/// a bracketed prefix is a fan-subtitling group and wins outright; a name on the exact-match list never
/// uses the <c>-Group</c> suffix and would otherwise be unfindable; a name on the bracket-suffix list
/// closes with <c>)</c> or <c>]</c>; only then is the ordinary trailing <c>-Group</c> convention tried.
/// The surveyed applications carried this table in near-identical copies per kind, which is why it is
/// host code with host data.
/// </remarks>
internal static partial class ReleaseGroupScanner
{
    /// <summary>Reads the release group, if the title names one.</summary>
    /// <param name="title">A release title, ideally with the work's own title already masked out.</param>
    /// <returns>The group, or null.</returns>
    internal static string? Scan(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var text = StripRecognizedExtension(title.Trim());
        text = WebsitePrefix().Replace(text, string.Empty);
        text = TorrentSuffix().Replace(text, string.Empty);

        var subGroupPrefix = SubGroupPrefix().Match(text);

        if (subGroupPrefix.Success)
        {
            return subGroupPrefix.Groups["subgroup"].Value;
        }

        // Obfuscation suffixes are appended after the group name by re-uploaders, so they have to come
        // off before the trailing-group convention is applied or the group reads as "Obfuscated".
        text = ObfuscationSuffix().Replace(text, string.Empty);

        var exact = ExceptionGroupsExact().Matches(text);

        if (exact.Count != 0)
        {
            return exact[^1].Groups["releasegroup"].Value;
        }

        var bracketed = ExceptionGroupsBracketed().Matches(text);

        if (bracketed.Count != 0)
        {
            return bracketed[^1].Groups["releasegroup"].Value;
        }

        var matches = TrailingGroup().Matches(text);

        if (matches.Count == 0)
        {
            return null;
        }

        var group = matches[^1].Groups["releasegroup"].Value;

        // A bare number is a part marker, not a group, and an eight-hex-digit run is a checksum.
        if (int.TryParse(group, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        return InvalidGroup().IsMatch(group) ? null : group;
    }

    /// <summary>Removes a trailing media or usenet file extension.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The title with a recognized extension removed.</returns>
    /// <remarks>
    /// Only recognized extensions come off. A blanket "remove the last dot-suffix" would eat the group
    /// whenever it happened to be three or four characters long.
    /// </remarks>
    internal static string StripRecognizedExtension(string title) =>
        ReleaseTokenVocabulary.TrailingExtension().Replace(
            title,
            static match => ReleaseTokenVocabulary.RecognizedContainerExtensions.Contains(match.Value)
                ? string.Empty
                : match.Value);

    [GeneratedRegex(
        @"-(?<releasegroup>[a-z0-9]+(?<part2>-[a-z0-9]+)?(?!.+?(?:480p|576p|720p|1080p|2160p)))(?<!(?:WEB-(DL|Rip)|Blu-Ray|480p|576p|720p|1080p|2160p|DTS-HD|DTS-X|DTS-MA|DTS-ES|-ES|-EN|-CAT|-ENG|-JAP|-GER|-FRA|-FRE|-ITA|-HDRip|\d{1,2}-bit|[ ._]\d{4}-\d{2}|-\d{2}|tmdb(id)?-(?<tmdbid>\d+)|(?<imdbid>tt\d{7,8}))(?:\k<part2>)?)(?:\b|[-._ ]|$)|[-._ ]\[(?<releasegroup>[a-z0-9]+)\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex TrailingGroup();

    [GeneratedRegex(
        @"^([se]\d+|[0-9a-f]{8})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex InvalidGroup();

    [GeneratedRegex(
        @"^(?:\[(?<subgroup>(?!\s).+?(?<!\s))\](?:_|-|\s|\.)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex SubGroupPrefix();

    [GeneratedRegex(
        @"\b(?<releasegroup>KRaLiMaRKo|E\.N\.D|D\-Z0N3|Koten_Gars|BluDragon|ZØNEHD|HQMUX|VARYG|YIFY|YTS(.(MX|LT|AG))?|TMd|Eml HDTeam|LMain|DarQ|BEN THE MEN|TAoE|QxR|126811)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex ExceptionGroupsExact();

    [GeneratedRegex(
        @"(?<=[._ \[])(?<releasegroup>(Silence|afm72|Panda|Ghost|MONOLITH|Tigole|Joy|ImE|UTR|t3nzin|Anime Time|Project Angel|Hakata Ramen|HONE|GiLG|Vyndros|SEV|Garshasp|Kappa|Natty|RCVR|SAMPA|YOGI|r00t|EDGE2020|RZeroX|FreetheFish|Anna|Bandi|Qman|theincognito|HDO|DusIctv|DHD|CtrlHD|-ZR-|ADC|XZVN|RH|Kametsu|Celdra)(?=\]|\)))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex ExceptionGroupsBracketed();

    [GeneratedRegex(
        @"(-(RP|1|NZBGeek|Obfuscated|Obfuscation|Scrambled|sample|Pre|postbot|xpost|Rakuv[a-z0-9]*|WhiteRev|BUYMORE|AsRequested|AlternativeToRequested|GEROV|Z0iDS3N|Chamele0n|4P|4Planet|AlteZachen|RePACKPOST))+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex ObfuscationSuffix();

    [GeneratedRegex(
        @"^(?:(?:\[|\()\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?<!Naruto-Kun\.)(?:[a-z]{2,6}\.[a-z]{2,6}|xn--[a-z0-9-]{4,}|[a-z]{2,})\b(?:\s*(?:\]|\))|[ -]{2,})[ -]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex WebsitePrefix();

    [GeneratedRegex(
        @"\[(?:ettv|rartv|rarbg|cttv|publichd)\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReleaseTokenVocabulary.MatchTimeoutMilliseconds)]
    private static partial Regex TorrentSuffix();
}
