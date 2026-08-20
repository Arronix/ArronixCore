
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Releases;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;

namespace Arronix.Plugin.Movies.Definition;

/// <summary>Interprets movie release, file, and folder names as typed video-backed releases.</summary>
/// <remarks>
/// This is ordinary executable C#. Pattern order is explicit control flow, not a public declaration graph
/// consumed by a host-side DSL. Format-owned interpretation can populate <see cref="Video"/> independently;
/// absence remains null until a video recognizer establishes it.
/// </remarks>
public sealed class MovieReleaseParser : IReleaseParser<Release<Video>>
{
    /// <summary>Matches supported movie edition and cut labels.</summary>
    public const string EditionRegex =
        @"\(?\b(?<edition>(((Recut.|Extended.|Ultimate.)?(Director.?s|Collector.?s|Theatrical|Ultimate|Extended|Despecialized|(Special|Rouge|Final|Assembly|Imperial|Diamond|Signature|Hunter|Rekall)(?=(.(Cut|Edition|Version)))|\d{2,3}(th)?.Anniversary)(?:.(Cut|Edition|Version))?(.(Extended|Uncensored|Remastered|Unrated|Uncut|Open.?Matte|IMAX|Fan.?Edit))?|((Uncensored|Remastered|Unrated|Uncut|Open?.Matte|IMAX|Fan.?Edit|Restored|((2|3|4)in1))))))\b\)?";

    private static readonly Regex AnimeSubgroupYear = Pattern(
        @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|x|\d+|\]|\W\d+)))+.*?(?<hash>\[\w{8}\])?(?:$|\.)");

    private static readonly Regex AnimeSubgroupVersionedHash = Pattern(
        @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)((v)(?:\d{1,2})(?:([-_. ])))(\[.*)?(?:[\[(][^])])?.*?(?<hash>\[\w{8}\])(?:$|\.)");

    private static readonly Regex AnimeSubgroupDoubleBracketHash = Pattern(
        @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)(\[.*).*?(?<hash>\[\w{8}\])(?:$|\.)");

    private static readonly Regex AnimeSubgroupBracketHash = Pattern(
        @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+)(?:[\[(][^])]).*?(?<hash>\[\w{8}\])(?:$|\.)");

    private static readonly Regex GermanOrTrueFrenchWithoutYear = Pattern(
        @"^(?<title>(?![(\[]).+?)((\W|_))(" + EditionRegex
        + @".{1,3})?(?:(?<!(19|20)\d{2}.*?)(?<!(?:Good|The)[_ .-])(German|TrueFrench))(.+?)(?=((19|20)\d{2}|$))(?<year>(19|20)\d{2}(?!p|i|\d+|\]|\W\d+))?(\W+|_|$)(?!\\)");

    private static readonly Regex EditionThenYear = Pattern(
        @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*" + EditionRegex
        + @".{1,3}(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)");

    private static readonly Regex TitleThenYear = Pattern(
        @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|(1(8|9)|20)\d{2}|\]|\W(1(8|9)|20)\d{2})))+(\W+|_|$)(?!\\)");

    private static readonly Regex PassThePopcorn = Pattern(
        @"^(?<title>.+?)?(?:(?:[-_\W](?<![()\[!]))*(?<year>(\[\w *\])))+(\W+|_|$)(?!\\)");

    private static readonly Regex BracketedYear = Pattern(
        @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?!\\)");

    private static readonly Regex LastResortYear = Pattern(
        @"^(?<title>.+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)");

    private static readonly Regex YearThenTitleFolder = Pattern(
        @"^(?:(?:[-_\W](?<![)!]))*(?<year>(19|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?<title>.+?)?$");

    /// <inheritdoc />
    public static ReleaseParseResult<Release<Video>> Parse(ReleaseParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.Text))
        {
            return ReleaseParseResult<Release<Video>>.Rejected("The input contains no release name.");
        }

        var working = context.Text.Trim().Replace('_', ' ');
        var claim = FindClaim(working, context.Source);

        if (claim is null)
        {
            return ReleaseParseResult<Release<Video>>.Rejected("No movie title pattern accepted the input.");
        }

        int? year = int.TryParse(
            claim.Match.Groups["year"].Value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedYear)
            ? parsedYear
            : null;

        var edition = CleanOptional(claim.Match.Groups["edition"].Value);
        var release = new Release<Video>(
            CleanTitle(claim.Match.Groups["title"].Value),
            year,
            edition);

        var trace = InterpretationTrace<Release<Video>>.Empty
            .Add(item => item.Title, ObservationKind.Claimed, nameof(MovieReleaseParser), claim.PatternId);

        if (year is not null)
        {
            trace = trace.Add(
                item => item.Year,
                ObservationKind.Claimed,
                nameof(MovieReleaseParser),
                claim.Match.Groups["year"].Value);
        }

        if (edition is not null)
        {
            trace = trace.Add(
                item => item.Edition,
                ObservationKind.Claimed,
                nameof(MovieReleaseParser),
                claim.Match.Groups["edition"].Value);
        }

        return ReleaseParseResult<Release<Video>>.Accepted(release, context.ExternalIds, trace);
    }

    private static ParseClaim? FindClaim(string text, MatchSource source) =>
        Try("anime-subgroup-year", AnimeSubgroupYear, text)
        ?? Try("anime-subgroup-versioned-hash", AnimeSubgroupVersionedHash, text)
        ?? Try("anime-subgroup-double-bracket-hash", AnimeSubgroupDoubleBracketHash, text)
        ?? Try("anime-subgroup-bracket-hash", AnimeSubgroupBracketHash, text)
        ?? Try("german-truefrench-no-year", GermanOrTrueFrenchWithoutYear, text)
        ?? Try("edition-then-year", EditionThenYear, text)
        ?? Try("title-then-year", TitleThenYear, text)
        ?? Try("pass-the-popcorn", PassThePopcorn, text)
        ?? Try("bracketed-year", BracketedYear, text)
        ?? Try("last-resort-year", LastResortYear, text)
        ?? (source == MatchSource.FolderName
            ? Try("year-then-title-folder", YearThenTitleFolder, text)
            : null);

    private static ParseClaim? Try(string patternId, Regex expression, string text)
    {
        try
        {
            var match = expression.Match(text);
            return match.Success && !string.IsNullOrWhiteSpace(match.Groups["title"].Value)
                ? new ParseClaim(patternId, match)
                : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static string CleanTitle(string captured)
    {
        var title = captured.Trim(' ', '-', '_');
        var separated = title.Contains('.', StringComparison.Ordinal)
            && !title.EndsWith(".", StringComparison.Ordinal)
                ? title + "."
                : title;
        title = Movies.RespaceDottedAcronym(separated);

        var builder = new StringBuilder(title.Length);
        var previousWasSpace = false;
        foreach (var character in title)
        {
            var isSpace = character == ' ';
            if (!isSpace || !previousWasSpace)
            {
                builder.Append(character);
            }

            previousWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    private static string? CleanOptional(string value)
    {
        var cleaned = value.Replace('.', ' ').Trim(' ', '(', ')');
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static Regex Pattern(string expression) => new(
        expression,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private sealed record ParseClaim(string PatternId, Match Match);
}
