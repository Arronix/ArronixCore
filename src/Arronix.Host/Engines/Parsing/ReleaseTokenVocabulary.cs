using System.Text.RegularExpressions;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// The host's shared release-token vocabulary: the source, resolution, revision and remux expressions
/// every kind's releases spell the same way.
/// </summary>
/// <remarks>
/// <para>
/// Ported from Radarr's <c>src/NzbDrone.Core/Parser/QualityParser.cs</c> (regexes at lines 17-77) via the
/// modernized copy in <c>Arronix.Plugin.Movies/MoviesReleaseParser.cs</c> (<c>MovieQualityTokenParser</c>),
/// whose own remarks record that none of it is media semantics: a release title says <c>x264</c> or
/// <c>REPACK</c> the same way whatever it contains. Four copies of these tables across the surveyed
/// applications were exactly the disease the host-owned layer exists to cure.
/// </para>
/// <para>
/// Every static pattern carries a match timeout: these expressions run over text the platform did not
/// author, and a scan that cannot finish must fail the one title, not the process.
/// </para>
/// </remarks>
internal static partial class ReleaseTokenVocabulary
{
    /// <summary>The match timeout, in milliseconds, for every host scan and every declared pattern.</summary>
    internal const int MatchTimeoutMilliseconds = 1000;

    /// <summary>
    /// The named groups of <see cref="SourceTags"/>, in the order the source scan reports them.
    /// </summary>
    internal static readonly IReadOnlyList<string> SourceGroupNames =
    [
        "bluray", "webdl", "webrip", "hdtv", "bdrip", "brrip", "dvdr", "dvd", "dsr",
        "regional", "scr", "ts", "tc", "cam", "wp", "pdtv", "sdtv", "tvrip"
    ];

    /// <summary>
    /// The containers the extension fallback's <c>"*"</c> row applies to. A port of the recognized
    /// media-extension set in <c>MoviesReleaseParser.cs</c> (<c>RemovableExtensions</c>), itself Radarr's
    /// <c>MediaFileExtensions</c> table: an unrecognized dot-suffix is somebody's release group, not a
    /// container, and must imply nothing.
    /// </summary>
    internal static readonly IReadOnlySet<string> RecognizedContainerExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".webm", ".m4v", ".3gp", ".nsv", ".ty", ".strm", ".rm", ".rmvb", ".m3u", ".ifo", ".mov",
            ".qt", ".divx", ".xvid", ".bivx", ".nrg", ".pva", ".wmv", ".asf", ".asx", ".ogm", ".ogv",
            ".m2v", ".avi", ".bin", ".dat", ".mpg", ".mpeg", ".mp4", ".avc", ".vp3", ".svq3", ".nuv",
            ".viv", ".dv", ".fli", ".flv", ".wpl", ".img", ".iso", ".vob", ".mkv", ".mk3d", ".ts",
            ".wtv", ".m2ts", ".par2", ".nzb"
        };

    /// <summary>
    /// The source scan. Radarr's <c>SourceRegex</c> verbatim (<c>QualityParser.cs:17-37</c>); the caller
    /// takes the LAST occurrence in the text, exactly as <c>QualityParser.cs:117-118</c> does.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:
            (?<bluray>M?Blu[-_. ]?Ray|HD[-_. ]?DVD|BD(?!$)|UHD2?BD|BDISO|BDMux|BD25|BD50|BR[-_. ]?DISK)|
            (?<webdl>WEB[-_. ]?DL(?:mux)?|AmazonHD|AmazonSD|iTunesHD|MaxdomeHD|NetflixU?HD|WebHD|HBOMaxHD|DisneyHD|[. ]WEB[. ](?:[xh][ .]?26[45]|AVC|HEVC|DDP?5[. ]1)|[. ](?-i:WEB)$|(?:\d{3,4}0p)[-. ](?:Hybrid[-_. ]?)?WEB[-. ]|[-. ]WEB[-. ]\d{3,4}0p|\b\s\/\sWEB\s\/\s\b|(?:AMZN|NF|DP)[. -]WEB[. -](?!Rip))|
            (?<webrip>WebRip|Web-Rip|WEBMux)|
            (?<hdtv>HDTV)|
            (?<bdrip>BDRip|BDLight|HD[-_. ]?DVDRip|UHDBDRip)|
            (?<brrip>BRRip)|
            (?<dvdr>\d?x?M?DVD-?[R59])|
            (?<dvd>DVD(?!-R)|DVDRip|xvidvd)|
            (?<dsr>WS[-_. ]DSR|DSR)|
            (?<regional>R[0-9]{1}|REGIONAL)|
            (?<scr>SCR|SCREENER|DVDSCR|DVDSCREENER)|
            (?<ts>TS[-_. ]|TELESYNCH?|HD-TS|HDTS|PDVD|TSRip|HDTSRip)|
            (?<tc>TC|TELECINE|HD-TC|HDTC)|
            (?<cam>CAMRIP|(?:NEW)?CAM|HD-?CAM(?:Rip)?|HQCAM)|
            (?<wp>WORKPRINT|WP)|
            (?<pdtv>PDTV)|
            (?<sdtv>SDTV)|
            (?<tvrip>TVRip)
            )(?:\b|$|[ .])",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex SourceTags();

    /// <summary>The stated-resolution scan (<c>QualityParser.cs:59-60</c>).</summary>
    [GeneratedRegex(
        @"\b(?:(?<r360p>360p)|(?<r480p>480p|480i|640x480|848x480)|(?<r540p>540p)|(?<r576p>576p)|(?<r720p>720p|1280x720|960p)|(?<r1080p>1080p|1920x1080|1440p|FHD|1080i|4kto1080p)|(?<r2160p>2160p|3840x2160|4k[-_. ](?:UHD|HEVC|BD|H\.?265)|(?:UHD|HEVC|BD|H\.?265)[-_. ]4k))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex ResolutionTags();

    /// <summary>"UHD" and "[4K]" name a resolution without stating it numerically (<c>QualityParser.cs:63-64</c>).</summary>
    [GeneratedRegex(
        @"\b(?<r2160p>UHD)\b|(?<r2160p>\[4K\])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex AlternativeResolutionTags();

    /// <summary>The PROPER marker (<c>QualityParser.cs:47-48</c>).</summary>
    [GeneratedRegex(
        @"\b(?<proper>proper)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex ProperTag();

    /// <summary>The REPACK/RERIP marker (<c>QualityParser.cs:50-51</c>).</summary>
    [GeneratedRegex(
        @"\b(?<repack>repack\d?|rerip\d?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex RepackTag();

    /// <summary>The explicit version marker (<c>QualityParser.cs:53-54</c>).</summary>
    [GeneratedRegex(
        @"\d[-._ ]?v(?<version>\d)[-._ ]|\[v(?<version>\d)\]|repack(?<version>\d)|rerip(?<version>\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex VersionTag();

    /// <summary>
    /// The REAL marker, deliberately case-sensitive and run against the raw text
    /// (<c>QualityParser.cs:56-57</c>): <c>REAL</c> is a revision bump where <c>real</c> is an ordinary
    /// English word, and a default-insensitive scan would silently flatten the two.
    /// </summary>
    [GeneratedRegex(
        @"\b(?<real>REAL)\b",
        RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex RealTag();

    /// <summary>The remux marker (<c>QualityParser.cs:76</c>).</summary>
    [GeneratedRegex(
        @"(?:[_. \[]|\d{4}p-|\bHybrid-)(?<remux>(?:(BD|UHD)[-_. ]?)?Remux)\b|(?<remux>(?:(BD|UHD)[-_. ]?)?Remux[_. ]\d{4}p)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex RemuxTag();

    /// <summary>A trailing dot-suffix that may be a container extension.</summary>
    [GeneratedRegex(
        @"\.[a-z0-9]{2,4}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        MatchTimeoutMilliseconds)]
    internal static partial Regex TrailingExtension();
}
