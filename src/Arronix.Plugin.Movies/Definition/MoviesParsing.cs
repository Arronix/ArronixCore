#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended declarer.
#pragma warning disable ARX0019 // Definition contracts are experimental; a media extension is their intended declarer.

using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Definition;

/// <summary>
/// The movie release models: how a release title reads into a title, a year and tag evidence, and how
/// that evidence resolves to a ladder rung.
/// </summary>
/// <remarks>
/// <para>
/// The host's kind-agnostic layer runs first and is not declared here: junk rejection, separator
/// normalization, the shared source, resolution, codec, audio, revision, remux, group, hash and language
/// scanners. This section is exactly the per-kind residue — the ordered title patterns, the movie guards,
/// the two identifier conventions a movie release spells, and the rung-resolution decision table.
/// </para>
/// <para>
/// <b>Declared order is the algorithm.</b> The pattern list is tried top to bottom and the first pattern
/// whose expression matches and whose guards pass claims the release; the rung table is evaluated top to
/// bottom and the first row whose predicate holds wins. Nothing sorts either list.
/// </para>
/// <para>
/// What was 2,179 lines of imperative parser is this file plus the host engines. The one residue that is
/// genuinely an algorithm rather than data — rejoining a dotted acronym so that <c>S.W.A.T.2003</c> keeps
/// its dots while <c>Mission.Impossible.3</c> loses them — is bound as the host strategy
/// <c>dotted-title-respace</c> on the definition root, not smuggled in here.
/// </para>
/// </remarks>
public static class MoviesParsing
{
    /// <summary>
    /// The edition alternation, declared once and referenced by three patterns and the edition guard, so
    /// the pattern that detects an edition and the two that use one as a title terminator cannot drift.
    /// </summary>
    public const string EditionRegex =
        @"\(?\b(?<edition>(((Recut.|Extended.|Ultimate.)?(Director.?s|Collector.?s|Theatrical|Ultimate|Extended|Despecialized|(Special|Rouge|Final|Assembly|Imperial|Diamond|Signature|Hunter|Rekall)(?=(.(Cut|Edition|Version)))|\d{2,3}(th)?.Anniversary)(?:.(Cut|Edition|Version))?(.(Extended|Uncensored|Remastered|Unrated|Uncut|Open.?Matte|IMAX|Fan.?Edit))?|((Uncensored|Remastered|Unrated|Uncut|Open?.Matte|IMAX|Fan.?Edit|Restored|((2|3|4)in1))))))\b\)?";

    /// <summary>Builds the parse declaration.</summary>
    /// <returns>The declaration. Pure data; no logic, no I/O.</returns>
    public static ParseDeclaration Build() => new()
    {
        Normalization = new NormalizationOptions
        {
            // Radarr's NormalizeRegex stop words. The expression carries the lookarounds the word list
            // alone cannot: a stop word that is the whole title survives, and a single-letter acronym part
            // ("A.I.") is never mistaken for the article.
            LeadingArticles = ["the", "a", "an"],
            StopWords = ["a", "à", "an", "the", "and", "or", "of"],
            StopWordExpression =
                @"((?:\b|_)(?<!^|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])([aà](?!$|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])|an|the|and|or|of)(?!$)(?:\b|_))|\W|_",

            // German umlaut rows are transliterations rather than diacritic folds: folding "ü" to "u"
            // collides with a different word, so the row states the conventional spelling instead.
            Transliterations =
            [
                new("ä", "ae"), new("ö", "oe"), new("ü", "ue"),
                new("Ä", "Ae"), new("Ö", "Oe"), new("Ü", "Ue"), new("ß", "ss")
            ],
            FoldDiacritics = true,

            // "1917" and "300" are titles, not years.
            NumericTitlesPassThrough = true,

            // The guard two of the four surveyed forks lack: a normalization that eats the whole title
            // falls back to the raw form rather than yielding an empty comparison key.
            EmptyResultFallsBackToRaw = true,

            // The query-text form: drop a leading article, spell the ampersand, drop apostrophes, and
            // collapse everything else to single spaces.
            QueryRewrites =
            [
                new RewriteRule(@"^the\s", ""),
                new RewriteRule(@"&", "and"),
                new RewriteRule(@"['.`´‘’]", ""),
                new RewriteRule(@"\W", " "),
                new RewriteRule(@"\s+", " ")
            ]
        },

        // Radarr has none. (Sonarr's fourteen ParserCommon rows are television's.)
        PreRewrites = [],

        TitlePatterns = Patterns(),
        Guards = Guards(),
        TokenTables = TokenTables(),
        RungResolution = RungTable(),

        // Movies budgets zero per-kind code escapes: the plugin stays a pure definition.
        EscapeIds = []
    };

    // ---------------------------------------------------------------------------------------------
    // The ordered pattern list. Verbatim expressions; the order is Radarr's.
    // ---------------------------------------------------------------------------------------------
    private static IReadOnlyList<TitlePattern> Patterns() =>
    [
        P("anime-subgroup-year",
            @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|x|\d+|\]|\W\d+)))+.*?(?<hash>\[\w{8}\])?(?:$|\.)",
            [Title(), TitleYear(), Tag("subgroup", MoviesReleaseTags.SubGroup), Tag("hash", MoviesReleaseTags.ReleaseHash)]),

        P("anime-subgroup-versioned-hash",
            @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)((v)(?:\d{1,2})(?:([-_. ])))(\[.*)?(?:[\[(][^])])?.*?(?<hash>\[\w{8}\])(?:$|\.)",
            [Title(), Tag("subgroup", MoviesReleaseTags.SubGroup), Tag("hash", MoviesReleaseTags.ReleaseHash)]),

        P("anime-subgroup-double-bracket-hash",
            @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)(\[.*).*?(?<hash>\[\w{8}\])(?:$|\.)",
            [Title(), Tag("subgroup", MoviesReleaseTags.SubGroup), Tag("hash", MoviesReleaseTags.ReleaseHash)]),

        P("anime-subgroup-bracket-hash",
            @"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+)(?:[\[(][^])]).*?(?<hash>\[\w{8}\])(?:$|\.)",
            [Title(), Tag("subgroup", MoviesReleaseTags.SubGroup), Tag("hash", MoviesReleaseTags.ReleaseHash)]),

        // German and TrueFrench releases that omit the year: the language tag terminates the title
        // instead. Only these two languages — adding French broke every film with a French title.
        P("german-truefrench-no-year",
            @"^(?<title>(?![(\[]).+?)((\W|_))(" + EditionRegex + @".{1,3})?(?:(?<!(19|20)\d{2}.*?)(?<!(?:Good|The)[_ .-])(German|TrueFrench))(.+?)(?=((19|20)\d{2}|$))(?<year>(19|20)\d{2}(?!p|i|\d+|\]|\W\d+))?(\W+|_|$)(?!\\)",
            [Title(), TitleYear(), Tag("edition", MoviesReleaseTags.Edition)]),

        // Edition before year: "Mission.Impossible.3.Special.Edition.2011".
        P("edition-then-year",
            @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*" + EditionRegex + @".{1,3}(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)",
            [Title(), TitleYear(), Tag("edition", MoviesReleaseTags.Edition)]),

        // The ordinary shape, and the one that claims most releases. The year's lookahead rejects a
        // following "p" or "i" — a resolution is never a year — and rejects a following year, so
        // "2019.2020" reads as a title containing 2019.
        P("title-then-year",
            @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|(1(8|9)|20)\d{2}|\]|\W(1(8|9)|20)\d{2})))+(\W+|_|$)(?!\\)",
            [Title(), TitleYear()]),

        // One tracker names its torrents "Star.Wars[PassThePopcorn]" with no year at all, so the
        // bracketed tag is the terminator and the year capture is deliberately not bound.
        P("pass-the-popcorn",
            @"^(?<title>.+?)?(?:(?:[-_\W](?<![()\[!]))*(?<year>(\[\w *\])))+(\W+|_|$)(?!\\)",
            [Title()]),

        P("bracketed-year",
            @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?!\\)",
            [Title(), TitleYear()]),

        // Last, and it will claim anything with a four-digit number in it — including a title that opens
        // with a bracket, which every earlier pattern refuses.
        P("last-resort-year",
            @"^(?<title>.+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)",
            [Title(), TitleYear()]),

        // Folders only: "2001 - A Space Odyssey". Radarr's isDir flag, as declared provenance.
        P("year-then-title-folder",
            @"^(?:(?:[-_\W](?<![)!]))*(?<year>(19|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?<title>.+?)?$",
            [Title(), TitleYear()],
            sources: [MatchSource.FolderName])
    ];

    // ---------------------------------------------------------------------------------------------
    // Named guards, declared once and referenced by identifier. Verbatim expressions.
    // ---------------------------------------------------------------------------------------------
    private static IReadOnlyList<GuardPattern> Guards() =>
    [
        new GuardPattern("raw-hd", @"\b(?<rawhd>RawHD|Raw[-_. ]HD)\b"),
        new GuardPattern("mpeg2", @"\b(?<mpeg2>MPEG[-_. ]?2)\b", GuardInput.Normalized, CaseSensitive: true),

        // The whole-disc probe. Data at the edge of maintainability, and still data.
        new GuardPattern("br-disk",
            @"^(?!.*\b((?<!HD[._ -]|HD)DVD|BDRip|720p|MKV|XviD|WMV|d3g|(BD)?REMUX|^(?=.*1080p)(?=.*HEVC)|[xh][-_. ]?26[45]|German.*[DM]L|((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2)\b))\b)(((?=.*\b(Blu[-_. ]?ray|BD|HD[-_. ]?DVD)\b)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2|BDMV|ISO)\b))|^((?=.*\b(((?=.*\b((.*_)?COMPLETE.*|Dis[ck])\b)(?=.*(Blu[-_. ]?ray|HD[-_. ]?DVD)))|3D[-_. ]?BD|BR[-_. ]?DISK|Full[-_. ]?Blu[-_. ]?ray|^((?=.*((BD|UHD)[-_. ]?(25|50|66|100|ISO)))))))).*"),

        // A German dual-language disc encode is a remux the shared remux scan cannot see: it never spells
        // the word. Declared as its own guard so the three remux rows can each carry a twin.
        new GuardPattern("german-remux",
            @"((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[_. -]?1|MVC|MPEG[_. -]?2))(?=.*Blu-?ray)"),

        new GuardPattern("anime-bd", @"bd(?:720|1080|2160)|(?<=[-_. (\[])bd(?=[-_. )\]])"),
        new GuardPattern("anime-web", @"\[WEB\]|[\[\(]WEB[ .]"),
        new GuardPattern("hr-ws", @"hr[-_. ]ws"),
        new GuardPattern("xvid-divx", @"\b(?:(?<xvid>X-?vid)|(?<divx>divx))\b"),
        new GuardPattern("x264-alone", @"\b(?<x264>x264)\b"),

        // Bracketed literals are case-sensitive and are read from the raw text, exactly as the source is.
        new GuardPattern("bracket-webdl", @"\[WEBDL\]", GuardInput.Raw, CaseSensitive: true),
        new GuardPattern("bracket-hdtv", @"\[HDTV\]", GuardInput.Raw, CaseSensitive: true),

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

        // A resolution word anywhere in the text, as opposed to the one the shared scan settled on. The
        // surveyed branches probe the text a second time, and the two answers differ when a title states
        // more than one resolution.
        new GuardPattern("text-480p", @"480p"),
        new GuardPattern("text-720p", @"720p"),
        new GuardPattern("text-1080p", @"1080p"),
        new GuardPattern("text-2160p", @"2160p"),

        // Container evidence, read from the raw text's trailing suffix. Used only where the release
        // stated a resolution and nothing else; the silent case is the table's container fallback.
        new GuardPattern("container-web", @"\.(mkv|mk3d)$", GuardInput.Raw),
        new GuardPattern("container-disc", @"\.m2ts$", GuardInput.Raw),
        new GuardPattern("container-dvd", @"\.(img|iso|vob)$", GuardInput.Raw),

        new GuardPattern("edition", EditionRegex)
    ];

    // ---------------------------------------------------------------------------------------------
    // Per-kind token additions over the host scanners: the two identifier conventions a movie release
    // spells that the shared vocabulary does not know.
    // ---------------------------------------------------------------------------------------------
    private static IReadOnlyList<TokenTable> TokenTables() =>
    [
        new TokenTable
        {
            TableId = "movie-embedded-ids",
            Rows =
            [
                // tt plus seven or eight digits. Anything shorter is a coincidence; anything longer is
                // not an identifier — which is exactly what the length constraint says.
                new TokenRow(@"(?<imdbid>tt\d{7,8})", MoviesReleaseTags.ImdbId, Constraint: "length 9..10"),
                new TokenRow(@"tmdb(id)?-(?<tmdbid>\d+)", MoviesReleaseTags.TmdbId)
            ]
        }
    ];

    // ---------------------------------------------------------------------------------------------
    // The rung-resolution decision table: the 610-line branch cascade of MovieQualityTokenParser as
    // ordered rows. Order is semantic — whole discs and raw streams first, then each source's own
    // resolution ladder, then the sourceless conventions, then the weak signals.
    // ---------------------------------------------------------------------------------------------
    private static RungResolutionTable RungTable() => new()
    {
        UnknownTierId = "Unknown",
        Rules =
        [
            // Raw streams and whole discs outrank every rip.
            R("rawhd", "Raw-HD", G("raw-hd"), G("br-disk", negated: true)),
            R("bluray-disk", "BR-DISK", Src("bluray"), G("br-disk")),

            // An XviD or DivX encode of a disc is standard definition whatever the disc was.
            R("bluray-xvid", "Bluray-480p", Src("bluray"), G("xvid-divx")),

            R("bluray-2160-remux", "Remux-2160p", Src("bluray"), Res(2160), Remux()),
            R("bluray-2160-remux-german", "Remux-2160p", Src("bluray"), Res(2160), G("german-remux")),
            R("bluray-2160", "Bluray-2160p", Src("bluray"), Res(2160)),
            R("bluray-1080-remux", "Remux-1080p", Src("bluray"), Res(1080), Remux()),
            R("bluray-1080-remux-german", "Remux-1080p", Src("bluray"), Res(1080), G("german-remux")),
            R("bluray-1080", "Bluray-1080p", Src("bluray"), Res(1080)),
            R("bluray-720", "Bluray-720p", Src("bluray"), Res(720)),
            R("bluray-576", "Bluray-576p", Src("bluray"), Res(576)),
            R("bluray-sd", "Bluray-480p", Src("bluray"), ResIn(360, 480, 540)),

            // A remux naming a disc but no resolution is 1080p, never 720p: a 720p remux is not a thing
            // and reading one as such would let it outrank a genuine 1080p disc.
            R("bluray-remux-nores", "Remux-1080p", Src("bluray"), Remux()),
            R("bluray-remux-nores-german", "Remux-1080p", Src("bluray"), G("german-remux")),
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
            R("telesync", "TELESYNC", Src("ts")),
            R("telecine", "TELECINE", Src("tc")),
            R("workprint", "WORKPRINT", Src("wp")),
            R("regional", "REGIONAL", Src("regional")),

            // An MPEG-2 broadcast capture has not been re-encoded, so it is a raw stream, not an HDTV rip.
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
            R("pdtv-text-1080", "HDTV-1080p", SrcIn("pdtv", "sdtv", "dsr", "tvrip"), G("text-1080p")),
            R("pdtv-720", "HDTV-720p", SrcIn("pdtv", "sdtv", "dsr", "tvrip"), Res(720)),
            R("pdtv-text-720", "HDTV-720p", SrcIn("pdtv", "sdtv", "dsr", "tvrip"), G("text-720p")),
            R("pdtv-hrws", "HDTV-720p", SrcIn("pdtv", "sdtv", "dsr", "tvrip"), G("hr-ws")),
            R("pdtv-sd", "SDTV", SrcIn("pdtv", "sdtv", "dsr", "tvrip")),

            // A remux with no source at all: rounded to Blu-ray, because nothing else is ever remuxed.
            R("orphan-remux-sd", "Bluray-480p", NoSource(), Remux(), ResIn(360, 480, 540)),
            R("orphan-remux-720", "Bluray-720p", NoSource(), Remux(), Res(720)),
            R("orphan-remux-2160", "Remux-2160p", NoSource(), Remux(), Res(2160)),
            R("orphan-remux", "Remux-1080p", NoSource(), Remux(), ResAny()),

            // Anime bracket conventions. Each probes the text for a resolution word as well as reading
            // the one the shared scan settled on, exactly as the surveyed branches do.
            R("anime-bd-sd", "DVD", NoSource(), G("anime-bd"), ResIn(360, 480, 540, 576)),
            R("anime-bd-text-sd", "DVD", NoSource(), G("anime-bd"), G("text-480p")),
            R("anime-bd-1080-remux", "Remux-1080p", NoSource(), G("anime-bd"), Res(1080), Remux()),
            R("anime-bd-1080", "Bluray-1080p", NoSource(), G("anime-bd"), Res(1080)),
            R("anime-bd-text-1080-remux", "Remux-1080p", NoSource(), G("anime-bd"), G("text-1080p"), Remux()),
            R("anime-bd-text-1080", "Bluray-1080p", NoSource(), G("anime-bd"), G("text-1080p")),
            R("anime-bd-2160-remux", "Remux-2160p", NoSource(), G("anime-bd"), Res(2160), Remux()),
            R("anime-bd-2160", "Bluray-2160p", NoSource(), G("anime-bd"), Res(2160)),
            R("anime-bd-text-2160-remux", "Remux-2160p", NoSource(), G("anime-bd"), G("text-2160p"), Remux()),
            R("anime-bd-text-2160", "Bluray-2160p", NoSource(), G("anime-bd"), G("text-2160p")),
            R("anime-bd-remux-no720", "Remux-1080p", NoSource(), G("anime-bd"), Remux(), NotRes(720)),
            R("anime-bd", "Bluray-720p", NoSource(), G("anime-bd")),

            R("anime-web-sd", "WEBDL-480p", NoSource(), G("anime-web"), ResIn(360, 480, 540, 576)),
            R("anime-web-text-sd", "WEBDL-480p", NoSource(), G("anime-web"), G("text-480p")),
            R("anime-web-1080", "WEBDL-1080p", NoSource(), G("anime-web"), Res(1080)),
            R("anime-web-text-1080", "WEBDL-1080p", NoSource(), G("anime-web"), G("text-1080p")),
            R("anime-web-2160", "WEBDL-2160p", NoSource(), G("anime-web"), Res(2160)),
            R("anime-web-text-2160", "WEBDL-2160p", NoSource(), G("anime-web"), G("text-2160p")),
            R("anime-web", "WEBDL-720p", NoSource(), G("anime-web")),

            // A resolution and nothing else. The container is the only remaining evidence, so it is read
            // before the default: a bare "540p" inside a Matroska file is a stream download, not a
            // broadcast capture.
            R("res-web-2160", "WEBDL-2160p", NoSource(), G("container-web"), Res(2160)),
            R("res-web-1080", "WEBDL-1080p", NoSource(), G("container-web"), Res(1080)),
            R("res-web-720", "WEBDL-720p", NoSource(), G("container-web"), Res(720)),
            R("res-web-576", "Bluray-576p", NoSource(), G("container-web"), Res(576)),
            R("res-web-sd", "WEBDL-480p", NoSource(), G("container-web"), ResIn(360, 480, 540)),
            R("res-disc-2160", "Bluray-2160p", NoSource(), G("container-disc"), Res(2160)),
            R("res-disc-1080", "Bluray-1080p", NoSource(), G("container-disc"), Res(1080)),
            R("res-disc-720", "Bluray-720p", NoSource(), G("container-disc"), Res(720)),
            R("res-disc-576", "Bluray-576p", NoSource(), G("container-disc"), Res(576)),
            R("res-disc-sd", "Bluray-480p", NoSource(), G("container-disc"), ResIn(360, 480, 540)),
            R("res-dvd-container", "DVD", NoSource(), G("container-dvd"), ResAny()),

            // Resolution alone lands on the broadcast rung: right most often, and never the top of the
            // ladder, so an unlabeled file never blocks a real one.
            R("res-alone-2160", "HDTV-2160p", NoSource(), Res(2160)),
            R("res-alone-1080", "HDTV-1080p", NoSource(), Res(1080)),
            R("res-alone-720", "HDTV-720p", NoSource(), Res(720)),
            R("res-alone-576", "Bluray-576p", NoSource(), Res(576)),
            R("res-alone-sd", "SDTV", NoSource(), ResIn(360, 480, 540)),

            // Weak signals, last: an x264 encode with nothing else is standard definition by convention,
            // because every high-definition encoder states its resolution. Then the pixel-form
            // resolutions, then the unseparated spellings the word-boundary scan cannot see.
            R("weak-x264", "SDTV", NoSource(), G("x264-alone")),
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
            R("weak-sdtv-spaced", "SDTV", NoSource(), G("sdtv-spaced")),
            R("weak-hdtv-spaced", "HDTV-720p", NoSource(), G("hdtv-spaced"))
        ],

        // Consulted only when every row above is silent. Radarr's thirty-container table collapsed to the
        // three answers that are not standard definition plus the default class.
        ContainerFallbacks =
        [
            new ExtensionTierRule(".mkv", "WEBDL-720p"),
            new ExtensionTierRule(".mk3d", "WEBDL-720p"),
            new ExtensionTierRule(".m2ts", "Bluray-720p"),
            new ExtensionTierRule(".img", "DVD"),
            new ExtensionTierRule(".iso", "DVD"),
            new ExtensionTierRule(".vob", "DVD"),

            // Every other recognized media container implies standard definition.
            new ExtensionTierRule("*", "SDTV")
        ]
    };

    // -- row constructors. Build-time sugar; every value built is pure data. -----------------------
    private static TitlePattern P(
        string id,
        string regex,
        IReadOnlyList<CaptureBinding> captures,
        IReadOnlyList<MatchSource>? sources = null) => new()
        {
            PatternId = id,
            Regex = regex,
            Captures = captures,
            Sources = sources ?? [],
            Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single }
        };

    private static CaptureBinding Title() => new("title", CaptureTarget.TitleText);

    private static CaptureBinding TitleYear() => new("year", CaptureTarget.TitleYear);

    private static CaptureBinding Tag(string group, string key) =>
        new(group, CaptureTarget.Tag, Key: key);

    private static RungRule R(string id, string tier, params PredicateAtom[] atoms) =>
        new() { RuleId = id, TierId = tier, When = new TagPredicate([.. atoms]) };

    private static PredicateAtom Src(string source) =>
        new() { Subject = "tags.SourceGroup", Op = PredicateOp.Equals, Values = [source] };

    private static PredicateAtom SrcIn(params string[] sources) =>
        new() { Subject = "tags.SourceGroup", Op = PredicateOp.In, Values = [.. sources] };

    private static PredicateAtom NoSource() =>
        new() { Subject = "tags.SourceGroup", Op = PredicateOp.Present, Negated = true };

    private static PredicateAtom Res(int height) =>
        new() { Subject = "tags.StatedResolution", Op = PredicateOp.Equals, Values = [Text(height)] };

    private static PredicateAtom ResIn(params int[] heights) =>
        new() { Subject = "tags.StatedResolution", Op = PredicateOp.In, Values = [.. heights.Select(Text)] };

    private static PredicateAtom NotRes(int height) =>
        new() { Subject = "tags.StatedResolution", Op = PredicateOp.Equals, Values = [Text(height)], Negated = true };

    private static PredicateAtom ResAny() =>
        new() { Subject = "tags.StatedResolution", Op = PredicateOp.Present };

    private static PredicateAtom Remux() =>
        new() { Subject = "tags.IsRemux", Op = PredicateOp.Equals, Values = ["true"] };

    private static PredicateAtom G(string guardId, bool negated = false) =>
        new() { Subject = "guard:" + guardId, Op = PredicateOp.GuardMatches, Negated = negated };

    private static string Text(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The tag keys the movie parse declaration writes, so a consumer reads the same spelling the
/// declaration wrote.
/// </summary>
/// <remarks>
/// Every one of these lands under the engine's <c>parse.tag.</c> prefix in the parsed release's metadata
/// and is reachable from a declared predicate as <c>tags.&lt;key&gt;</c>.
/// </remarks>
public static class MoviesReleaseTags
{
    /// <summary>The cut or edition the release names, when it names one.</summary>
    public const string Edition = "edition";

    /// <summary>An embedded The Movie Database identifier.</summary>
    public const string TmdbId = "tmdbId";

    /// <summary>An embedded IMDb identifier.</summary>
    public const string ImdbId = "imdbId";

    /// <summary>The eight-character checksum a fan-subtitling release carries.</summary>
    public const string ReleaseHash = "releaseHash";

    /// <summary>The bracketed sub-group prefix a fan-subtitling release opens with.</summary>
    public const string SubGroup = "subGroup";
}
