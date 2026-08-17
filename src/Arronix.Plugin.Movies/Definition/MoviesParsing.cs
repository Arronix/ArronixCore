#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended declarer.
#pragma warning disable ARX0019 // Definition contracts are experimental; a media extension is their intended declarer.

using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Definition;

/// <summary>
/// The movie release models: how a release title reads into a title, a year and tag evidence.
/// </summary>
/// <remarks>
/// <para>
/// The host's kind-agnostic layer runs first and is not declared here: junk rejection, separator
/// normalization, the shared source, resolution, codec, audio, revision, remux, group, hash and language
/// scanners. This section is exactly the per-kind residue — the ordered title patterns, the movie guards,
/// and the two identifier conventions a movie release spells.
/// </para>
/// <para>
/// <b>A hundred and one rung rows and a seven-row container fallback used to sit here, and they are
/// gone.</b> Their whole job was to collapse evidence into one of thirty names before anything could
/// reason about it, and every row of that table was a small ranking decision living inside a file that is
/// supposed to be about reading text. The evidence half of each row survives as a typed reading and the
/// rung half has nothing left to map to. What replaces them is not a smaller table: it is the video
/// family's own reading of the evidence, which is ordinary code, and it is shared with every other kind
/// whose files are video instead of being written out again per kind.
/// </para>
/// <para>
/// <b>Declared order is the algorithm.</b> The pattern list is tried top to bottom and the first pattern
/// whose expression matches and whose guards pass claims the release. Nothing sorts it.
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
    // The per-kind residue, and nothing else. A guard survives here only when what it matches is a
    // convention of how THIS community writes titles; anything that is a fact about video is read from
    // typed evidence by the shared family model, and four whole classes of guard left that way rather
    // than sideways.
    // ---------------------------------------------------------------------------------------------
    private static IReadOnlyList<GuardPattern> Guards() =>
    [
        // The whole-disc probe. Data at the edge of maintainability, and still data — and it fires on
        // titles carrying no disc token at all, which is exactly why what it contributes is a guess that
        // can inform a reading and can never refuse a release.
        new GuardPattern(MoviesVideoRefinement.WholeDiscGuard,
            @"^(?!.*\b((?<!HD[._ -]|HD)DVD|BDRip|720p|MKV|XviD|WMV|d3g|(BD)?REMUX|^(?=.*1080p)(?=.*HEVC)|[xh][-_. ]?26[45]|German.*[DM]L|((?<=\d{4}).*German.*([DM]L)?)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2)\b))\b)(((?=.*\b(Blu[-_. ]?ray|BD|HD[-_. ]?DVD)\b)(?=.*\b(AVC|HEVC|VC[-_. ]?1|MVC|MPEG[-_. ]?2|BDMV|ISO)\b))|^((?=.*\b(((?=.*\b((.*_)?COMPLETE.*|Dis[ck])\b)(?=.*(Blu[-_. ]?ray|HD[-_. ]?DVD)))|3D[-_. ]?BD|BR[-_. ]?DISK|Full[-_. ]?Blu[-_. ]?ray|^((?=.*((BD|UHD)[-_. ]?(25|50|66|100|ISO)))))))).*"),

        // Bracketed fan-subtitling conventions. A bracketed source marker is a habit of one naming
        // community rather than anything a video file carries.
        new GuardPattern(MoviesVideoRefinement.AnimeDiscGuard, @"bd(?:720|1080|2160)|(?<=[-_. (\[])bd(?=[-_. )\]])"),
        new GuardPattern(MoviesVideoRefinement.AnimeStreamGuard, @"\[WEB\]|[\[\(]WEB[ .]"),

        // A legacy scene spelling for a widescreen broadcast capture, with no typed form anywhere.
        new GuardPattern(MoviesVideoRefinement.HighResolutionWidescreenGuard, @"hr[-_. ]ws"),

        // A resolution word anywhere in the text, as opposed to the one the shared scan settled on. The
        // two answers differ for a bracketed title that spells its raster outside the run the scan reads,
        // and these contribute only where the scan read nothing at all.
        new GuardPattern("text-480p", @"480p"),
        new GuardPattern("text-720p", @"720p"),
        new GuardPattern("text-1080p", @"1080p"),
        new GuardPattern("text-2160p", @"2160p"),

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
