using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Parsing;
using Arronix.Host.Engines.Quality;
using Arronix.Host.Tests.Support;

// The shape (ARX0013), intent (ARX0016) and definition (ARX0019) contracts are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0019

#pragma warning disable ARX0020 // The typed media surface is experimental; these fixtures build one.

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The definition the parse- and quality-engine tests run against.
/// </summary>
/// <remarks>
/// <para>
/// The identifiers stay media-neutral, per the standing fixture discipline; the ladder and the rung
/// table, however, are deliberately the surveyed application's — Radarr's twenty-nine rungs with the
/// grouped WEBRip/WEBDL weights, and its <c>QualityParser.ParseQualityName</c> branch cascade as ordered
/// rows, exactly as the Movies exhibit renders them. That is what lets these tests assert the engines
/// reproduce the reference answers, including the grouped-rung cutoff the movies review documented as
/// wrongly answered.
/// </para>
/// </remarks>
internal static class ParseEngineFixtures
{
    /// <summary>Builds the full test model.</summary>
    /// <param name="parsing">A parse section override, when a test needs its own.</param>
    /// <param name="quality">A quality section override, when a test needs its own.</param>
    /// <returns>The model.</returns>
    internal static MediaKindModel Model(
        ParseDeclaration? parsing = null, QualityDeclaration? quality = null) => new()
        {
            Parsing = parsing ?? Parsing(),
            Matching = Matching(),
            Querying = Querying(),
            Quality = quality ?? new QualityDeclaration(),
        };

    /// <summary>Builds the parse engine over the test shape and model.</summary>
    /// <param name="parsing">A parse section override, when a test needs its own.</param>
    /// <param name="quality">A quality section override, when a test needs its own.</param>
    /// <returns>The engine.</returns>
    internal static DeclarativeReleaseParser Parser(
        ParseDeclaration? parsing = null, QualityDeclaration? quality = null)
        => new(Shape(), Model(parsing, quality));

    /// <summary>Builds the quality engine over the test shape, whose ladders it reads.</summary>
    /// <returns>The engine.</returns>
    internal static DeclarativeQualityEvaluator QualityEvaluator() => new(Shape());

    /// <summary>Builds the shape: the fused fixture shape carrying the graded ladders.</summary>
    /// <returns>The shape.</returns>
    internal static MediaShape Shape() => ShapeFixtures.Fused() with
    {
        FormatFamilies = [GradedFamily(), CompanionFamily()],
    };

    /// <summary>
    /// The surveyed ladder: twenty-nine rungs, distinct ranks, grouped weights on the four
    /// re-encode/direct-download pairs, and size bands on the rungs that declare one.
    /// </summary>
    /// <returns>The family.</returns>
    internal static FormatFamily GradedFamily() => new()
    {
        FamilyId = "video",
        Name = "Video",
        FileExtensions = [".mkv", ".mp4"],
        Unknown = new QualityTier("Unknown", 0, Weight: 0),
        Ladder =
        [
            T("WORKPRINT", 1, 1),
            T("CAM", 2, 2),
            T("TELESYNC", 3, 3),
            T("TELECINE", 4, 4),
            T("REGIONAL", 5, 5),
            T("DVDSCR", 6, 6),
            new QualityTier("SDTV", 7, Weight: 7, MaxSizeMbPerMinute: 100),
            T("DVD", 8, 8),
            T("DVD-R", 9, 9),
            T("WEBRip-480p", 10, 10, "WEB 480p"),
            T("WEBDL-480p", 11, 10, "WEB 480p"),
            T("Bluray-480p", 12, 11),
            T("Bluray-576p", 13, 12),
            T("HDTV-720p", 14, 13),
            T("WEBRip-720p", 15, 14, "WEB 720p"),
            T("WEBDL-720p", 16, 14, "WEB 720p"),
            T("Bluray-720p", 17, 15),
            T("HDTV-1080p", 18, 16),
            T("WEBRip-1080p", 19, 17, "WEB 1080p"),
            T("WEBDL-1080p", 20, 17, "WEB 1080p"),
            new QualityTier("Bluray-1080p", 21, Weight: 18, MinSizeMbPerMinute: 5),
            T("Remux-1080p", 22, 19),
            T("HDTV-2160p", 23, 20),
            T("WEBRip-2160p", 24, 21, "WEB 2160p"),
            T("WEBDL-2160p", 25, 21, "WEB 2160p"),
            T("Bluray-2160p", 26, 22),
            T("Remux-2160p", 27, 23),
            T("BR-DISK", 28, 24),
            T("Raw-HD", 29, 25),
        ],
    };

    /// <summary>A second, incomparable family, for the cross-family rules.</summary>
    /// <returns>The family.</returns>
    internal static FormatFamily CompanionFamily() => new()
    {
        FamilyId = "companion",
        Name = "Companion",
        FileExtensions = [".cmp"],
        Unknown = new QualityTier("Companion-Unknown", 0, Weight: 0),
        Ladder =
        [
            T("Standard", 1, 1),
            T("Archival", 2, 2),
        ],
    };

    /// <summary>
    /// The parse section: two title patterns (one provenance-restricted), a range pattern, the guard
    /// set and the full rung table from the Movies exhibit.
    /// </summary>
    /// <returns>The parse declaration.</returns>
    internal static ParseDeclaration Parsing() => new()
    {
        TitlePatterns =
        [
            new TitlePattern
            {
                PatternId = "numbered-run",
                Regex = @"^(?<title>.+?)[ ._]#(?<from>\d{1,3})-(?<to>\d{1,3})(?:[ ._]|$)",
                Captures =
                [
                    new CaptureBinding("title", CaptureTarget.TitleText),
                ],
                Expansion = new RangeExpansion { FromGroup = "from", ToGroup = "to", MaxSpan = 25 },
                Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.SequenceSpan },
            },
            new TitlePattern
            {
                PatternId = "title-then-year",
                Regex = @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|(1(8|9)|20)\d{2}|\]|\W(1(8|9)|20)\d{2})))+(\W+|_|$)(?!\\)",
                Captures =
                [
                    new CaptureBinding("title", CaptureTarget.TitleText),
                    new CaptureBinding("year", CaptureTarget.TitleYear),
                ],
                Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
            },

            // Folders only: "2001 - A Space Odyssey". Provenance-restricted, exactly the surveyed
            // folder-name flag as data; Parse(string) reads release names, so it may never fire here.
            new TitlePattern
            {
                PatternId = "year-then-title-folder",
                Regex = @"^(?:(?:[-_\W](?<![)!]))*(?<year>(19|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?<title>.+?)?$",
                Captures =
                [
                    new CaptureBinding("title", CaptureTarget.TitleText),
                    new CaptureBinding("year", CaptureTarget.TitleYear),
                ],
                Sources = [MatchSource.FolderName],
            },
        ],

        Guards =
        [
            new GuardPattern("raw-hd", @"\b(?<rawhd>RawHD|Raw[-_. ]HD)\b"),
            new GuardPattern("mpeg2", @"\b(?<mpeg2>MPEG[-_. ]?2)\b"),
            new GuardPattern(
                "br-disk",
                @"^(?!.*\b((?<!HD[._ -]|HD)DVD|BDRip|720p|MKV|XviD|WMV|d3g|(BD)?REMUX|^(?=.*1080p)(?=.*HEVC)|[xh][-_. ]?26[45]|German.*[DM]L|((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2)\b))\b)(((?=.*\b(Blu[-_. ]?ray|BD|HD[-_. ]?DVD)\b)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2|BDMV|ISO)\b))|^((?=.*\b(((?=.*\b((.*_)?COMPLETE.*|Dis[ck])\b)(?=.*(Blu[-_. ]?ray|HD[-_. ]?DVD)))|3D[-_. ]?BD|BR[-_. ]?DISK|Full[-_. ]?Blu[-_. ]?ray|^((?=.*((BD|UHD)[-_. ]?(25|50|66|100|ISO)))))))).*"),
            new GuardPattern("anime-bd", @"bd(?:720|1080|2160)|(?<=[-_. (\[])bd(?=[-_. )\]])"),
            new GuardPattern("anime-web", @"\[WEB\]|[\[\(]WEB[ .]"),
            new GuardPattern("hr-ws", @"hr[-_. ]ws"),
            new GuardPattern("xvid-divx", @"\b(?:(?<xvid>X-?vid)|(?<divx>divx))\b"),
            new GuardPattern("x264-alone", @"\b(?<x264>x264)\b"),
            new GuardPattern("bracket-webdl", @"\[WEBDL\]", GuardInput.Raw),
            new GuardPattern("bracket-hdtv", @"\[HDTV\]", GuardInput.Raw),
            new GuardPattern("px-848x480", @"848x480"),
            new GuardPattern("px-1280x720", @"1280x720"),
            new GuardPattern("px-1920x1080", @"1920x1080"),
            new GuardPattern("word-dvd", @"dvd"),
            new GuardPattern("word-bluray", @"bluray"),
            new GuardPattern("bluray720p-unseparated", @"bluray720p"),
            new GuardPattern("bluray1080p-unseparated", @"bluray1080p"),
            new GuardPattern("bluray2160p-unseparated", @"bluray2160p"),
            new GuardPattern("hdtv-spaced", @"(?<hdtv>HD[-_. ]TV)"),
            new GuardPattern("sdtv-spaced", @"(?<sdtv>SD[-_. ]TV)"),
        ],

        TokenTables =
        [
            new TokenTable
            {
                TableId = "embedded-ids",
                Rows =
                [
                    new TokenRow(@"(?<imdbid>tt\d{7,8})", "imdbId", Constraint: "length 9..10"),
                    new TokenRow(@"tmdb(id)?-(?<tmdbid>\d+)", "tmdbId", Constraint: "numeric"),
                ],
            },
        ],

        RungResolution = RungTable(),
    };

    /// <summary>
    /// The rung table: <c>QualityParser.ParseQualityName</c> as ordered rows, verbatim from the Movies
    /// exhibit. Order is semantic — raw streams and whole discs before rips, pre-release before
    /// broadcast, weak signals last, container fallback only when every row is silent.
    /// </summary>
    /// <returns>The table.</returns>
    internal static RungResolutionTable RungTable() => new()
    {
        UnknownTierId = "Unknown",
        Rules =
        [
            R("rawhd", "Raw-HD", G("raw-hd"), G("br-disk", negated: true)),
            R("bluray-disk", "BR-DISK", Src("bluray"), G("br-disk")),
            R("bluray-xvid", "Bluray-480p", Src("bluray"), G("xvid-divx")),
            R("bluray-2160-remux", "Remux-2160p", Src("bluray"), Res(2160), Remux()),
            R("bluray-2160", "Bluray-2160p", Src("bluray"), Res(2160)),
            R("bluray-1080-remux", "Remux-1080p", Src("bluray"), Res(1080), Remux()),
            R("bluray-1080", "Bluray-1080p", Src("bluray"), Res(1080)),
            R("bluray-720", "Bluray-720p", Src("bluray"), Res(720)),
            R("bluray-576", "Bluray-576p", Src("bluray"), Res(576)),
            R("bluray-sd", "Bluray-480p", Src("bluray"), ResIn(360, 480, 540)),
            R("bluray-remux-nores", "Remux-1080p", Src("bluray"), Remux()),
            R("bluray-nores", "Bluray-720p", Src("bluray")),

            R("webdl-2160", "WEBDL-2160p", Src("webdl"), Res(2160)),
            R("webdl-1080", "WEBDL-1080p", Src("webdl"), Res(1080)),
            R("webdl-720", "WEBDL-720p", Src("webdl"), Res(720)),
            R("webdl-bracket", "WEBDL-720p", Src("webdl"), G("bracket-webdl")),
            R("webdl-nores", "WEBDL-480p", Src("webdl")),

            R("webrip-2160", "WEBRip-2160p", Src("webrip"), Res(2160)),
            R("webrip-1080", "WEBRip-1080p", Src("webrip"), Res(1080)),
            R("webrip-720", "WEBRip-720p", Src("webrip"), Res(720)),
            R("webrip-nores", "WEBRip-480p", Src("webrip")),

            R("screener", "DVDSCR", Src("scr")),
            R("cam", "CAM", Src("cam")),

            // The surveyed rung that keeps its identity while adopting whatever resolution the release
            // stated — the tier-plus-carried-resolution composition a pure tier identifier could not say.
            Carry("telesync", "TELESYNC", Src("ts")),
            R("telecine", "TELECINE", Src("tc")),
            R("workprint", "WORKPRINT", Src("wp")),
            R("regional", "REGIONAL", Src("regional")),

            R("hdtv-mpeg2", "Raw-HD", Src("hdtv"), G("mpeg2")),
            R("hdtv-2160", "HDTV-2160p", Src("hdtv"), Res(2160)),
            R("hdtv-1080", "HDTV-1080p", Src("hdtv"), Res(1080)),
            R("hdtv-720", "HDTV-720p", Src("hdtv"), Res(720)),
            R("hdtv-bracket", "HDTV-720p", Src("hdtv"), G("bracket-hdtv")),
            R("hdtv-nores", "SDTV", Src("hdtv")),

            R("bdrip-2160", "Bluray-2160p", SrcIn("bdrip", "brrip"), Res(2160)),
            R("bdrip-1080", "Bluray-1080p", SrcIn("bdrip", "brrip"), Res(1080)),
            R("bdrip-720", "Bluray-720p", SrcIn("bdrip", "brrip"), Res(720)),
            R("bdrip-576", "Bluray-576p", SrcIn("bdrip", "brrip"), Res(576)),
            R("bdrip-nores", "Bluray-480p", SrcIn("bdrip", "brrip")),

            R("dvdr", "DVD-R", Src("dvdr")),
            R("dvd", "DVD", Src("dvd")),

            R("pdtv-1080", "HDTV-1080p", SrcIn("pdtv", "sdtv", "dsr", "tvrip"), Res(1080)),
            R("pdtv-720", "HDTV-720p", SrcIn("pdtv", "sdtv", "dsr", "tvrip"), Res(720)),
            R("pdtv-hrws", "HDTV-720p", SrcIn("pdtv", "sdtv", "dsr", "tvrip"), G("hr-ws")),
            R("pdtv-sd", "SDTV", SrcIn("pdtv", "sdtv", "dsr", "tvrip")),

            R("orphan-remux-sd", "Bluray-480p", NoSource(), Remux(), ResIn(360, 480, 540)),
            R("orphan-remux-720", "Bluray-720p", NoSource(), Remux(), Res(720)),
            R("orphan-remux-2160", "Remux-2160p", NoSource(), Remux(), Res(2160)),
            R("orphan-remux", "Remux-1080p", NoSource(), Remux(), ResAny()),

            R("anime-bd-sd", "DVD", NoSource(), G("anime-bd"), ResIn(360, 480, 540, 576)),
            R("anime-bd-1080-remux", "Remux-1080p", NoSource(), G("anime-bd"), Res(1080), Remux()),
            R("anime-bd-1080", "Bluray-1080p", NoSource(), G("anime-bd"), Res(1080)),
            R("anime-bd-2160-remux", "Remux-2160p", NoSource(), G("anime-bd"), Res(2160), Remux()),
            R("anime-bd-2160", "Bluray-2160p", NoSource(), G("anime-bd"), Res(2160)),
            R("anime-bd-remux-no720", "Remux-1080p", NoSource(), G("anime-bd"), Remux(), NotRes(720)),
            R("anime-bd", "Bluray-720p", NoSource(), G("anime-bd")),
            R("anime-web-sd", "WEBDL-480p", NoSource(), G("anime-web"), ResIn(360, 480, 540, 576)),
            R("anime-web-1080", "WEBDL-1080p", NoSource(), G("anime-web"), Res(1080)),
            R("anime-web-2160", "WEBDL-2160p", NoSource(), G("anime-web"), Res(2160)),
            R("anime-web", "WEBDL-720p", NoSource(), G("anime-web")),

            R("res-alone-2160", "HDTV-2160p", NoSource(), Res(2160)),
            R("res-alone-1080", "HDTV-1080p", NoSource(), Res(1080)),
            R("res-alone-720", "HDTV-720p", NoSource(), Res(720)),
            R("res-alone-576", "Bluray-576p", NoSource(), Res(576)),
            R("res-alone-sd", "SDTV", NoSource(), ResIn(360, 480, 540)),

            R("weak-848x480-dvd", "DVD", NoSource(), G("px-848x480"), G("word-dvd")),
            R("weak-848x480-bluray", "Bluray-480p", NoSource(), G("px-848x480"), G("word-bluray")),
            R("weak-848x480", "SDTV", NoSource(), G("px-848x480")),
            R("weak-1280x720-bluray", "Bluray-720p", NoSource(), G("px-1280x720"), G("word-bluray")),
            R("weak-1280x720", "HDTV-720p", NoSource(), G("px-1280x720")),
            R("weak-1920x1080-bluray", "Bluray-1080p", NoSource(), G("px-1920x1080"), G("word-bluray")),
            R("weak-1920x1080", "HDTV-1080p", NoSource(), G("px-1920x1080")),
            R("weak-bluray720p", "Bluray-720p", NoSource(), G("bluray720p-unseparated")),
            R("weak-bluray1080p", "Bluray-1080p", NoSource(), G("bluray1080p-unseparated")),
            R("weak-bluray2160p", "Bluray-2160p", NoSource(), G("bluray2160p-unseparated")),
            R("weak-x264", "SDTV", NoSource(), G("x264-alone")),
            R("weak-sdtv-spaced", "SDTV", NoSource(), G("sdtv-spaced")),
            R("weak-hdtv-spaced", "HDTV-720p", NoSource(), G("hdtv-spaced")),
        ],

        ContainerFallbacks =
        [
            new ExtensionTierRule(".mkv", "WEBDL-720p"),
            new ExtensionTierRule(".mk3d", "WEBDL-720p"),
            new ExtensionTierRule(".m2ts", "Bluray-720p"),
            new ExtensionTierRule(".img", "DVD"),
            new ExtensionTierRule(".iso", "DVD"),
            new ExtensionTierRule(".vob", "DVD"),
            new ExtensionTierRule("*", "SDTV"),
        ],
    };

    // -- tiny row constructors, mirroring the exhibit's ----------------------------------------------

    internal static RungRule R(string id, string tier, params PredicateAtom[] atoms) =>
        new() { RuleId = id, TierId = tier, When = new TagPredicate([.. atoms]) };

    internal static RungRule Carry(string id, string tier, params PredicateAtom[] atoms) =>
        new() { RuleId = id, TierId = tier, When = new TagPredicate([.. atoms]), CarryStatedResolution = true };

    internal static PredicateAtom Src(string source) =>
        new() { Subject = "tags.SourceGroup", Op = PredicateOp.Equals, Values = [source] };

    internal static PredicateAtom SrcIn(params string[] sources) =>
        new() { Subject = "tags.SourceGroup", Op = PredicateOp.In, Values = [.. sources] };

    internal static PredicateAtom NoSource() =>
        new() { Subject = "tags.SourceGroup", Op = PredicateOp.Present, Negated = true };

    internal static PredicateAtom Res(int height) =>
        new()
        {
            Subject = "tags.StatedResolution",
            Op = PredicateOp.Equals,
            Values = [height.ToString(CultureInfo.InvariantCulture)],
        };

    internal static PredicateAtom ResIn(params int[] heights) =>
        new()
        {
            Subject = "tags.StatedResolution",
            Op = PredicateOp.In,
            Values = [.. heights.Select(static height => height.ToString(CultureInfo.InvariantCulture))],
        };

    internal static PredicateAtom NotRes(int height) =>
        new()
        {
            Subject = "tags.StatedResolution",
            Op = PredicateOp.Equals,
            Values = [height.ToString(CultureInfo.InvariantCulture)],
            Negated = true,
        };

    internal static PredicateAtom ResAny() =>
        new() { Subject = "tags.StatedResolution", Op = PredicateOp.Present };

    internal static PredicateAtom Remux() =>
        new() { Subject = "tags.IsRemux", Op = PredicateOp.Equals, Values = ["true"] };

    internal static PredicateAtom G(string guard, bool negated = false) =>
        new() { Subject = $"guard:{guard}", Op = PredicateOp.GuardMatches, Negated = negated };

    private static QualityTier T(string name, int rank, int weight, string? group = null) =>
        new(name, rank, Weight: weight, GroupName: group);

    private static MatchDeclaration Matching() => new()
    {
        Entry = new EntryResolution
        {
            IdentifierOrder = ["fixture"],
            Layers = [new MatchLayer { LayerId = "title", KeyTemplate = "{title}", NormalizerId = "plain" }],
        },
        Units = [new UnitResolutionRule { Spaces = [new SpaceAttempt { SpaceId = "only" }] }],
        Confidence = [],
    };

    private static QueryDeclaration Querying() => new()
    {
        Tiers = [new QueryTierTemplate { TierId = "text", SearchKindId = "unit" }],
        Grammar = CoordinateGrammar.None,
    };
}
