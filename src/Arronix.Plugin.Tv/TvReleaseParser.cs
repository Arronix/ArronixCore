#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Parsing;

namespace Arronix.Plugin.Tv;

/// <summary>
/// Keys this extension writes into <see cref="ParsedRelease.AdditionalMetadata"/>.
/// </summary>
/// <remarks>
/// <see cref="ParsedRelease"/> is a stable 0.1.0 DTO with a fixed member set and no coordinate slot, so the
/// numbering this extension reads out of a title travels in its bag. The matcher prefers a strongly-typed
/// re-parse and treats these as a cross-check, which is why the keys are published rather than private.
/// </remarks>
public static class TvReleaseFields
{
    /// <summary>Which addressing scheme the title used: one of <see cref="TvAddressingSchemes"/>.</summary>
    public const string AddressingScheme = "tv.addressingScheme";

    /// <summary>The outer ordinal, when the title carried one.</summary>
    public const string Season = "tv.season";

    /// <summary>The inner ordinals, comma-separated. More than one means a multi-unit release.</summary>
    public const string Episodes = "tv.episodes";

    /// <summary>The flat ordinals, comma-separated.</summary>
    public const string AbsoluteNumbers = "tv.absoluteNumbers";

    /// <summary>The transmission date, in <c>yyyy-MM-dd</c>.</summary>
    public const string AirDate = "tv.airDate";

    /// <summary>Present and <c>true</c> when the release covers a whole run of the sequence axis.</summary>
    public const string FullSequence = "tv.fullSequence";

    /// <summary>Present and <c>true</c> when the release covers more than one unit.</summary>
    public const string MultiUnit = "tv.multiUnit";

    /// <summary>The title normalized for comparison.</summary>
    public const string NormalizedTitle = "tv.normalizedTitle";

    /// <summary>The revision marker: <c>proper</c>, <c>repack</c> or absent.</summary>
    public const string Revision = "tv.revision";
}

/// <summary>
/// Which of the declared coordinate spaces a release title addressed.
/// </summary>
/// <remarks>
/// The value is derived from the <b>release</b>, never from the library entry. That is the finding that
/// makes this extension hard: an entry the catalog marks as flat-numbered routinely receives a release
/// addressed by the canonical ordinal pair, and it has to resolve. The entry's own declared scheme is a
/// default for rendering and searching, not a filter for matching.
/// </remarks>
public enum TvReleaseKind
{
    /// <summary>Nothing in the title identified a position.</summary>
    Unknown = 0,

    /// <summary>The canonical ordinal pair.</summary>
    SeasonEpisode = 1,

    /// <summary>A calendar date.</summary>
    AirDate = 2,

    /// <summary>A flat ordinal over the whole run.</summary>
    Absolute = 3,

    /// <summary>A whole run of the sequence axis in one release.</summary>
    FullSeason = 4
}

/// <summary>
/// Folds a raw title into a form two independently-written titles can be compared in.
/// </summary>
public static partial class TvTitleNormalizer
{
    // Space-less, matching the convention Movies declares for the host normalizer, so the two kinds carry
    // one encoding of one rule (the P2-7 drift finding). The word-boundary check lives at the match site:
    // a bare StartsWith("the") would strip "theatre" to "atre".
    private static readonly string[] LeadingArticles = ["the", "a", "an"];

    /// <summary>Normalizes a title for equality comparison.</summary>
    /// <param name="title">The raw title.</param>
    /// <returns>The comparison key, or the trimmed input when normalization would empty it.</returns>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var expanded = title.Replace("&", " and ", StringComparison.Ordinal);
        var stripped = NonWordCharacters().Replace(FoldDiacritics(expanded), " ");
        var collapsed = RepeatedWhitespace().Replace(stripped, " ").Trim();

        if (collapsed.Length == 0)
        {
            return title.Trim();
        }

        var lowered = collapsed.ToLowerInvariant();

        foreach (var article in LeadingArticles)
        {
            if (lowered.Length > article.Length
                && lowered.StartsWith(article, StringComparison.Ordinal)
                && lowered[article.Length] == ' ')
            {
                return lowered[(article.Length + 1)..];
            }
        }

        return lowered;
    }

    /// <summary>Produces the string an indexer should actually be asked for.</summary>
    /// <param name="title">The raw title.</param>
    /// <returns>A query string with punctuation reduced but words intact.</returns>
    public static string ToQueryText(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var expanded = title.Replace("&", " and ", StringComparison.Ordinal);
        var stripped = QueryPunctuation().Replace(FoldDiacritics(expanded), " ");
        var collapsed = RepeatedWhitespace().Replace(stripped, " ").Trim();

        return collapsed.Length == 0 ? title.Trim() : collapsed;
    }

    private static string FoldDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
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

    [GeneratedRegex(@"[^\w\s]", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordCharacters();

    [GeneratedRegex(@"[^\w\s'-]", RegexOptions.CultureInvariant)]
    private static partial Regex QueryPunctuation();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedWhitespace();
}

/// <summary>
/// Everything this extension can read out of an episodic release title.
/// </summary>
public sealed record TvParsedTitle
{
    /// <summary>The library entry's title, with separators turned back into spaces.</summary>
    public required string SeriesTitle { get; init; }

    /// <summary>The title folded for comparison.</summary>
    public required string NormalizedTitle { get; init; }

    /// <summary>Which coordinate space the title addressed.</summary>
    public required TvReleaseKind Kind { get; init; }

    /// <summary>The disambiguating year carried by the title, when present.</summary>
    public int? Year { get; init; }

    /// <summary>The outer ordinal.</summary>
    public int? SeasonNumber { get; init; }

    /// <summary>The inner ordinals, in the order the title stated them.</summary>
    public IReadOnlyList<int> EpisodeNumbers { get; init; } = [];

    /// <summary>The flat ordinals, in the order the title stated them.</summary>
    public IReadOnlyList<int> AbsoluteNumbers { get; init; } = [];

    /// <summary>The transmission date.</summary>
    public DateOnly? AirDate { get; init; }

    /// <summary>True when the release covers a whole run of the sequence axis.</summary>
    public bool IsFullSeason { get; init; }

    /// <summary>Resolution token.</summary>
    public string? Resolution { get; init; }

    /// <summary>Source token.</summary>
    public string? Source { get; init; }

    /// <summary>Whether the release is a lossless remux of its source.</summary>
    public bool IsRemux { get; init; }

    /// <summary>Video codec token.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>Audio codec token.</summary>
    public string? AudioCodec { get; init; }

    /// <summary>Release group.</summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>Languages named in the title. Empty means "not stated", never "none".</summary>
    public IReadOnlyList<Language> Languages { get; init; } = [];

    /// <summary>Revision marker: <c>proper</c> or <c>repack</c>.</summary>
    public string? Revision { get; init; }

    /// <summary>True when the title addresses more than one unit.</summary>
    public bool IsMultiUnit => EpisodeNumbers.Count > 1 || AbsoluteNumbers.Count > 1;

    /// <summary>True when the outer ordinal is the reserved out-of-run value.</summary>
    public bool IsOutOfRun => SeasonNumber == (int)TvIds.SpecialsOrdinal;
}

/// <summary>
/// Reads an episodic release title into <see cref="TvParsedTitle"/>.
/// </summary>
/// <remarks>
/// <para>The dispatch order is the load-bearing part. The canonical ordinal pair is tried first because it
/// is the most specific; the calendar date is tried before the flat ordinal because a four-digit year
/// followed by two two-digit groups is unambiguous while a bare number is not; the whole-run form is tried
/// last because every other form also contains an outer ordinal.</para>
/// <para>Nothing here consults a library entry. The predicates read the release, so a title stating a
/// canonical pair resolves as one whatever scheme its entry declares — which is exactly what the survey
/// found the reference implementation doing, and exactly what a per-entry addressing field forces.</para>
/// </remarks>
public static partial class TvTitleParser
{
    private static readonly (string Token, string Value)[] Resolutions =
    [
        ("2160p", "2160p"), ("4k", "2160p"), ("uhd", "2160p"),
        ("1080p", "1080p"), ("1080i", "1080p"),
        ("720p", "720p"), ("576p", "576p"), ("480p", "480p")
    ];

    private static readonly (string Token, string Value)[] Sources =
    [
        ("bluray", "bluray"), ("blu-ray", "bluray"), ("bdrip", "bluray"), ("brrip", "bluray"),
        ("web-dl", "webdl"), ("webdl", "webdl"), ("web dl", "webdl"),
        ("web-rip", "webrip"), ("webrip", "webrip"), ("web", "webdl"),
        ("hdtv", "tv"), ("pdtv", "tv"), ("sdtv", "tv"), ("dsr", "tv"),
        ("rawhd", "raw"), ("raw-hd", "raw"),
        ("dvdrip", "dvd"), ("dvd", "dvd")
    ];

    private static readonly (string Token, string Value)[] VideoCodecs =
    [
        ("x265", "x265"), ("h265", "x265"), ("hevc", "x265"),
        ("x264", "x264"), ("h264", "x264"), ("avc", "x264"),
        ("xvid", "xvid"), ("divx", "divx"), ("av1", "av1")
    ];

    private static readonly (string Token, string Value)[] AudioCodecs =
    [
        ("truehd", "TrueHD"), ("dts-hd", "DTS-HD"), ("dtshd", "DTS-HD"),
        ("atmos", "Atmos"), ("eac3", "EAC3"), ("ddp", "EAC3"), ("dd+", "EAC3"),
        ("dts", "DTS"), ("ac3", "AC3"), ("flac", "FLAC"), ("opus", "Opus"), ("aac", "AAC")
    ];

    private static readonly (string Token, string Code, string Name)[] LanguageTokens =
    [
        ("multi", "mul", "Multiple"), ("german", "de", "German"), ("french", "fr", "French"),
        ("spanish", "es", "Spanish"), ("italian", "it", "Italian"), ("japanese", "ja", "Japanese"),
        ("korean", "ko", "Korean"), ("russian", "ru", "Russian"), ("dutch", "nl", "Dutch"),
        ("nordic", "nor", "Nordic")
    ];

    /// <summary>Attempts to read an episodic release title.</summary>
    /// <param name="releaseTitle">The raw release title.</param>
    /// <param name="parsed">The parsed title, when the text yielded one.</param>
    /// <returns><see langword="true"/> when an entry title and a position could both be isolated.</returns>
    public static bool TryParse(string? releaseTitle, out TvParsedTitle? parsed)
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            return false;
        }

        var text = releaseTitle.Trim();
        var haystack = Separators().Replace(text, " ").ToLowerInvariant();
        var attributes = ReadAttributes(text, haystack);

        // 1. The canonical ordinal pair, including every multi-unit spelling: S01E01E02, S01E01-E02,
        //    S01E01E02E03. This is the only form that can address more than one unit at a time.
        var standard = SeasonEpisode().Match(text);

        if (standard.Success)
        {
            var episodes = ReadNumbers(standard.Groups["eps"].Value);

            if (episodes.Count != 0)
            {
                parsed = Compose(
                    standard.Groups["title"].Value,
                    TvReleaseKind.SeasonEpisode,
                    attributes) with
                {
                    SeasonNumber = ToInt(standard.Groups["season"].Value),
                    EpisodeNumbers = episodes
                };

                return true;
            }
        }

        // 2. The same pair in the alternative spelling.
        var crossed = SeasonEpisodeCrossed().Match(text);

        if (crossed.Success)
        {
            var episodes = ReadNumbers(crossed.Groups["eps"].Value);

            if (episodes.Count != 0)
            {
                parsed = Compose(
                    crossed.Groups["title"].Value,
                    TvReleaseKind.SeasonEpisode,
                    attributes) with
                {
                    SeasonNumber = ToInt(crossed.Groups["season"].Value),
                    EpisodeNumbers = episodes
                };

                return true;
            }
        }

        // 3. The calendar space.
        var daily = AirDatePattern().Match(text);

        if (daily.Success
            && TryReadDate(
                daily.Groups["year"].Value,
                daily.Groups["month"].Value,
                daily.Groups["day"].Value,
                out var airDate))
        {
            parsed = Compose(daily.Groups["title"].Value, TvReleaseKind.AirDate, attributes) with
            {
                AirDate = airDate
            };

            return true;
        }

        // 4. The flat ordinal, in its bracketed-group form and then its bare form.
        var bracketed = BracketedAbsolute().Match(text);

        if (bracketed.Success)
        {
            parsed = Compose(bracketed.Groups["title"].Value, TvReleaseKind.Absolute, attributes) with
            {
                AbsoluteNumbers = ReadRange(bracketed.Groups["abs"].Value, bracketed.Groups["abs2"].Value),
                ReleaseGroup = bracketed.Groups["group"].Value
            };

            return true;
        }

        var absolute = BareAbsolute().Match(text);

        if (absolute.Success)
        {
            parsed = Compose(absolute.Groups["title"].Value, TvReleaseKind.Absolute, attributes) with
            {
                AbsoluteNumbers = ReadRange(absolute.Groups["abs"].Value, absolute.Groups["abs2"].Value)
            };

            return true;
        }

        // 5. A whole run. Tried last because every form above also contains an outer ordinal.
        var fullSeason = FullSeason().Match(text);

        if (fullSeason.Success)
        {
            var season = fullSeason.Groups["season"].Success
                ? fullSeason.Groups["season"].Value
                : fullSeason.Groups["seasonWord"].Value;

            parsed = Compose(fullSeason.Groups["title"].Value, TvReleaseKind.FullSeason, attributes) with
            {
                SeasonNumber = ToInt(season),
                IsFullSeason = true
            };

            return true;
        }

        return false;
    }

    private static TvParsedTitle Compose(string rawTitle, TvReleaseKind kind, TitleAttributes attributes)
    {
        var cleaned = Separators().Replace(rawTitle, " ").Trim(' ', '-', '_');
        int? year = null;

        var yearMatch = TrailingYear().Match(cleaned);

        if (yearMatch.Success)
        {
            year = ToInt(yearMatch.Groups["year"].Value);
            cleaned = cleaned[..yearMatch.Index].Trim(' ', '-', '_');
        }

        return new TvParsedTitle
        {
            SeriesTitle = cleaned,
            NormalizedTitle = TvTitleNormalizer.Normalize(cleaned),
            Kind = kind,
            Year = year,
            Resolution = attributes.Resolution,
            Source = attributes.Source,
            IsRemux = attributes.IsRemux,
            VideoCodec = attributes.VideoCodec,
            AudioCodec = attributes.AudioCodec,
            ReleaseGroup = attributes.ReleaseGroup,
            Languages = attributes.Languages,
            Revision = attributes.Revision
        };
    }

    private static TitleAttributes ReadAttributes(string text, string haystack)
    {
        var isRemux = haystack.Contains("remux", StringComparison.Ordinal);
        var source = FirstToken(haystack, Sources);
        var groupMatch = ReleaseGroupSuffix().Match(text);

        return new TitleAttributes(
            FirstToken(haystack, Resolutions),
            isRemux ? "bluray-remux" : source,
            isRemux,
            FirstToken(haystack, VideoCodecs),
            FirstToken(haystack, AudioCodecs),
            groupMatch.Success ? groupMatch.Groups["group"].Value : null,
            ReadLanguages(haystack),
            ReadRevision(haystack));
    }

    private static string? ReadRevision(string haystack)
    {
        if (haystack.Contains("repack", StringComparison.Ordinal))
        {
            return "repack";
        }

        return haystack.Contains("proper", StringComparison.Ordinal) ? "proper" : null;
    }

    private static IReadOnlyList<Language> ReadLanguages(string haystack)
    {
        var found = new List<Language>();

        foreach (var (token, code, name) in LanguageTokens)
        {
            if (ContainsToken(haystack, token))
            {
                found.Add(new Language(code, name));
            }
        }

        return found;
    }

    private static string? FirstToken(string haystack, (string Token, string Value)[] table)
    {
        foreach (var (token, value) in table)
        {
            if (ContainsToken(haystack, token))
            {
                return value;
            }
        }

        return null;
    }

    private static bool ContainsToken(string haystack, string token)
    {
        var index = haystack.IndexOf(token, StringComparison.Ordinal);

        while (index >= 0)
        {
            var beforeIsBoundary = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var after = index + token.Length;
            var afterIsBoundary = after >= haystack.Length || !char.IsLetterOrDigit(haystack[after]);

            if (beforeIsBoundary && afterIsBoundary)
            {
                return true;
            }

            index = haystack.IndexOf(token, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static IReadOnlyList<int> ReadNumbers(string text)
    {
        var numbers = new List<int>();

        foreach (Match match in NumberRun().Matches(text))
        {
            if (int.TryParse(
                match.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    private static IReadOnlyList<int> ReadRange(string first, string second)
    {
        var start = ToInt(first);

        if (start is not { } from)
        {
            return [];
        }

        if (string.IsNullOrEmpty(second) || ToInt(second) is not { } to || to <= from)
        {
            return [from];
        }

        // A stated range is bounded so that a malformed title cannot make the extension allocate a very
        // large list. Twenty-five units in one release is already far beyond anything real.
        if (to - from > 25)
        {
            return [from];
        }

        var numbers = new List<int>(to - from + 1);

        for (var value = from; value <= to; value++)
        {
            numbers.Add(value);
        }

        return numbers;
    }

    private static bool TryReadDate(string year, string month, string day, out DateOnly date)
    {
        date = default;

        if (ToInt(year) is not { } y || ToInt(month) is not { } m || ToInt(day) is not { } d)
        {
            return false;
        }

        if (m is < 1 or > 12 || d is < 1 or > 31)
        {
            return false;
        }

        date = new DateOnly(y, m, d);
        return true;
    }

    private static int? ToInt(string? text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // The continuation group deliberately admits only "-", "E", "e", "x" or "X" between ordinals, with no
    // separator of any other kind. Allowing a dot would make "S01E01.x264" read as units 1 and 264.
    [GeneratedRegex(
        @"^(?<title>.+?)[\s._\-]+S(?<season>\d{1,4})[\s._\-]*[EeXx](?<eps>\d{1,3}(?:[\-EexX]{1,3}\d{1,3})*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisode();

    [GeneratedRegex(
        @"^(?<title>.+?)[\s._\-]+(?<season>\d{1,2})x(?<eps>\d{1,3}(?:[\s._\-]*[x\-][\s._\-]*\d{1,3})*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeCrossed();

    [GeneratedRegex(
        @"^(?<title>.+?)[\s._\-]+(?<year>(?:19|20)\d{2})[\s._\-](?<month>\d{2})[\s._\-](?<day>\d{2})(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AirDatePattern();

    [GeneratedRegex(
        @"^\[(?<group>[^\]]+)\][\s._\-]*(?<title>.+?)[\s._\-]+-[\s._\-]+(?<abs>\d{1,4})(?:[\-~](?<abs2>\d{1,4}))?(?:v\d)?(?:[\s._\-]|\[|$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BracketedAbsolute();

    [GeneratedRegex(
        @"^(?<title>.+?)[\s._\-]+-[\s._\-]+(?<abs>\d{1,4})(?:[\-~](?<abs2>\d{1,4}))?(?:v\d)?(?:[\s._\-]|$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BareAbsolute();

    [GeneratedRegex(
        @"^(?<title>.+?)[\s._\-]+(?:S(?<season>\d{1,4})|Season[\s._\-]+(?<seasonWord>\d{1,4}))(?![\s._\-]*[EeXx]?\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FullSeason();

    [GeneratedRegex(@"[\s._\-]\(?(?<year>(?:19|20)\d{2})\)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingYear();

    [GeneratedRegex(@"-(?<group>[A-Za-z0-9][A-Za-z0-9_.]{1,24})$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseGroupSuffix();

    [GeneratedRegex(@"\d{1,3}", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRun();

    [GeneratedRegex(@"[._]+", RegexOptions.CultureInvariant)]
    private static partial Regex Separators();

    private readonly record struct TitleAttributes(
        string? Resolution,
        string? Source,
        bool IsRemux,
        string? VideoCodec,
        string? AudioCodec,
        string? ReleaseGroup,
        IReadOnlyList<Language> Languages,
        string? Revision);
}

/// <summary>
/// The television extension's implementation of the stable release-parser contract.
/// </summary>
public sealed class TvReleaseParser : IReleaseParser
{
    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    public ParsedRelease? Parse(string releaseTitle)
    {
        if (!TvTitleParser.TryParse(releaseTitle, out var parsed) || parsed is null)
        {
            return null;
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TvReleaseFields.NormalizedTitle] = parsed.NormalizedTitle,
            [TvReleaseFields.AddressingScheme] = SchemeOf(parsed.Kind)
        };

        if (parsed.SeasonNumber is { } season)
        {
            extra[TvReleaseFields.Season] = season.ToString(CultureInfo.InvariantCulture);
        }

        if (parsed.EpisodeNumbers.Count != 0)
        {
            extra[TvReleaseFields.Episodes] = Join(parsed.EpisodeNumbers);
        }

        if (parsed.AbsoluteNumbers.Count != 0)
        {
            extra[TvReleaseFields.AbsoluteNumbers] = Join(parsed.AbsoluteNumbers);
        }

        if (parsed.AirDate is { } airDate)
        {
            extra[TvReleaseFields.AirDate] = airDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (parsed.IsFullSeason)
        {
            extra[TvReleaseFields.FullSequence] = "true";
        }

        if (parsed.IsMultiUnit)
        {
            extra[TvReleaseFields.MultiUnit] = "true";
        }

        if (parsed.Revision is not null)
        {
            extra[TvReleaseFields.Revision] = parsed.Revision;
        }

        return new ParsedRelease(
            TvIds.MediaKind,
            parsed.SeriesTitle,
            parsed.Year?.ToString(CultureInfo.InvariantCulture),
            parsed.Source,
            parsed.VideoCodec,
            parsed.AudioCodec,
            parsed.Resolution,
            parsed.ReleaseGroup,
            parsed.Languages.Count == 0 ? null : [.. parsed.Languages],
            new ReadOnlyDictionary<string, string>(extra));
    }

    /// <inheritdoc />
    public bool CanParse(string releaseTitle) => TvTitleParser.TryParse(releaseTitle, out _);

    private static string SchemeOf(TvReleaseKind kind) => kind switch
    {
        TvReleaseKind.AirDate => TvAddressingSchemes.Calendar,
        TvReleaseKind.Absolute => TvAddressingSchemes.Flat,
        _ => TvAddressingSchemes.Ordinal
    };

    private static string Join(IReadOnlyList<int> numbers)
    {
        var builder = new StringBuilder();

        foreach (var number in numbers)
        {
            if (builder.Length != 0)
            {
                builder.Append(',');
            }

            builder.Append(number.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
