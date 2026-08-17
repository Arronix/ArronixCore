#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.

using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Tv;

/// <summary>
/// The identifiers this extension declares once and every other file in it refers to.
/// </summary>
public static class TvIds
{
    /// <summary>The extension identifier; must equal the <c>id</c> in <c>plugin.json</c>.</summary>
    public const string PluginIdValue = "tv";

    /// <summary>The media kind identifier; must appear in the manifest's <c>mediaKinds</c>.</summary>
    public const string MediaKindValue = "tv";

    /// <summary>The library entry: what a user adds, and what owns the path and the profiles.</summary>
    public const string SeriesLevelValue = "series";

    /// <summary>The acquisition unit, the completeness unit and the file bearer, all at once.</summary>
    public const string EpisodeLevelValue = "episode";

    /// <summary>The canonical space: an ordinal pair over the catalog's own numbering.</summary>
    public const string AiredSpaceId = "aired";

    /// <summary>A flat ordinal running the length of the whole run, never restarting.</summary>
    public const string AbsoluteSpaceId = "absolute";

    /// <summary>A calendar date. The addressing scheme for a series that runs nightly.</summary>
    public const string AirDateSpaceId = "airdate";

    /// <summary>The release community's own ordinal pair. Provenance-sensitive and often extrapolated.</summary>
    public const string SceneSpaceId = "scene";

    /// <summary>The release community's own flat ordinal. Same caveats as <see cref="SceneSpaceId"/>.</summary>
    public const string SceneAbsoluteSpaceId = "scene-absolute";

    /// <summary>Component of <see cref="AiredSpaceId"/> and <see cref="SceneSpaceId"/>: the outer ordinal.</summary>
    public const string SeasonComponentId = "season";

    /// <summary>Component of <see cref="AiredSpaceId"/> and <see cref="SceneSpaceId"/>: the inner ordinal.</summary>
    public const string EpisodeComponentId = "episode";

    /// <summary>Component of the flat ordinal spaces.</summary>
    public const string AbsoluteComponentId = "absolute";

    /// <summary>The sequence axis over the outer component of the canonical space.</summary>
    public const string SeasonAxisId = "season";

    /// <summary>The single format family.</summary>
    public const string VideoFamilyId = "video";

    /// <summary>Search kind: one episode.</summary>
    public const string UnitSearchKindId = "unit";

    /// <summary>Search kind: a whole run of the sequence axis, in one release.</summary>
    public const string SeasonPackSearchKindId = "season-pack";

    /// <summary>Search kind: an episode addressed by the date it went out.</summary>
    public const string DailySearchKindId = "daily";

    /// <summary>Search kind: everything under one library entry.</summary>
    public const string SeriesSearchKindId = "series";

    /// <summary>Selection facet: whether out-of-run entries are shown.</summary>
    public const string SeasonKindFacetId = "season-kind";

    /// <summary>Monitoring dimension on the library entry: what to do about output that does not exist yet.</summary>
    public const string FutureItemsDimensionId = "future-items";

    /// <summary>Monitoring dimension on the unit: is this one wanted.</summary>
    public const string WantedDimensionId = "wanted";

    /// <summary>External identifier scheme: TheTVDB. Primary.</summary>
    public const string TvdbScheme = "tvdb";

    /// <summary>External identifier scheme: The Movie Database.</summary>
    public const string TmdbScheme = "tmdb";

    /// <summary>External identifier scheme: IMDb.</summary>
    public const string ImdbScheme = "imdb";

    /// <summary>The out-of-run ordinal reserved for entries that sit outside the dense sequence.</summary>
    public const long SpecialsOrdinal = 0;

    /// <summary>The media kind identifier, as a value.</summary>
    public static MediaKindId MediaKind { get; } = MediaKindId.FromString(MediaKindValue);

    /// <summary>The library-entry level identifier, as a value.</summary>
    public static MediaLevelId SeriesLevel { get; } = MediaLevelId.FromString(SeriesLevelValue);

    /// <summary>The unit level identifier, as a value.</summary>
    public static MediaLevelId EpisodeLevel { get; } = MediaLevelId.FromString(EpisodeLevelValue);
}

/// <summary>
/// Field identifiers for the library-entry level.
/// </summary>
public static class TvSeriesFields
{
    /// <summary>The display title. The only field on this level carrying <see cref="FieldSemantics.Title"/>.</summary>
    public const string Title = "title";

    /// <summary>The collation key.</summary>
    public const string SortTitle = "sortTitle";

    /// <summary>Year of first transmission.</summary>
    public const string Year = "year";

    /// <summary>Lifecycle state: continuing, ended or upcoming.</summary>
    public const string Status = "status";

    /// <summary>Which addressing scheme this entry's releases usually use.</summary>
    public const string AddressingScheme = "addressingScheme";

    /// <summary>The broadcaster.</summary>
    public const string Network = "network";

    /// <summary>Synopsis.</summary>
    public const string Overview = "overview";

    /// <summary>Typical duration of one unit.</summary>
    public const string Runtime = "runtime";

    /// <summary>Age rating.</summary>
    public const string Certification = "certification";

    /// <summary>Genres. Multivalued.</summary>
    public const string Genres = "genres";

    /// <summary>Cover artwork.</summary>
    public const string Poster = "poster";

    /// <summary>Date of first transmission.</summary>
    public const string FirstAired = "firstAired";

    /// <summary>TheTVDB identifier.</summary>
    public const string TvdbId = "tvdbId";

    /// <summary>IMDb identifier.</summary>
    public const string ImdbId = "imdbId";

    /// <summary>Whether releases for this entry are addressed in the release community's own numbering.</summary>
    public const string UsesAliasNumbering = "usesAliasNumbering";
}

/// <summary>
/// Field identifiers for the unit level.
/// </summary>
/// <remarks>
/// Note what is absent: every numbering field. Six of them live on the surveyed row — canonical pair,
/// absolute, three alias fields and a confidence flag — and all six are coordinates, not fields. They
/// travel in <see cref="CoordinateSet"/>, which is what lets a unit carry three addressing schemes at once
/// without the schema growing a column per scheme.
/// </remarks>
public static class TvEpisodeFields
{
    /// <summary>The display title. The only field on this level carrying <see cref="FieldSemantics.Title"/>.</summary>
    public const string Title = "title";

    /// <summary>Synopsis.</summary>
    public const string Overview = "overview";

    /// <summary>Date of first transmission. Also the value of the calendar coordinate space.</summary>
    public const string AirDate = "airDate";

    /// <summary>Duration.</summary>
    public const string Runtime = "runtime";

    /// <summary>Still image.</summary>
    public const string Still = "still";

    /// <summary>Whether this unit sits outside the dense run.</summary>
    public const string SequenceKind = "sequenceKind";

    /// <summary>The rendered canonical position, for display and sorting only.</summary>
    public const string Position = "position";
}

/// <summary>
/// Values of <see cref="TvSeriesFields.Status"/>. Also the <c>StateId</c>s of the entry-level states.
/// </summary>
public static class TvStatuses
{
    /// <summary>Still in production, with more output expected.</summary>
    public const string Continuing = "continuing";

    /// <summary>Finished. Nothing further will be produced.</summary>
    public const string Ended = "ended";

    /// <summary>Announced but not yet transmitted.</summary>
    public const string Upcoming = "upcoming";
}

/// <summary>
/// Values of <see cref="TvSeriesFields.AddressingScheme"/>.
/// </summary>
/// <remarks>
/// The single most important fact the survey established: addressing variance is <b>intra-kind</b>. This
/// value sits on the individual library entry, not on the extension, so one library holds three mutually
/// incompatible numbering schemes simultaneously. A design that says "the television extension addresses
/// things by season and episode" is already wrong, and this field is why the shape model carries a bag of
/// coordinate readings instead of a fixed pair of columns.
/// </remarks>
public static class TvAddressingSchemes
{
    /// <summary>Canonical ordinal pair.</summary>
    public const string Ordinal = "ordinal";

    /// <summary>Calendar date.</summary>
    public const string Calendar = "calendar";

    /// <summary>Flat ordinal over the whole run.</summary>
    public const string Flat = "flat";
}

/// <summary>
/// Values of the <see cref="TvIds.SeasonKindFacetId"/> selection facet.
/// </summary>
public static class TvSequenceKinds
{
    /// <summary>Runs inside the dense sequence.</summary>
    public const string Regular = "regular";

    /// <summary>The reserved out-of-run value.</summary>
    public const string OutOfRun = "out-of-run";
}

/// <summary>
/// Supplies the television media shape.
/// </summary>
/// <remarks>
/// <para>This is the hard end of the shape model and every difficult construct in the vocabulary is used
/// here at least once:</para>
/// <list type="bullet">
/// <item><description><b>Five coordinate spaces on one level</b>, of which exactly one is canonical, two
/// are provenance-sensitive and may be unverified, and one is a calendar date rather than an ordinal.</description></item>
/// <item><description><b>A sequence axis with a policy record and an exception.</b> The outer ordinal is a
/// component of a coordinate, not a level: it owns no state that is not either derivable from its children
/// or a bare coordinate, which is exactly the test that decides whether an intermediate level earns a row.
/// Its policy record — a monitored bit and artwork per (entry, ordinal) — is declared, not implied.</description></item>
/// <item><description><b>A span constraint.</b> One file may satisfy several units, but never units in
/// different runs. The surveyed implementation throws a dedicated exception for this; declaring it lets the
/// host enforce it for any media kind without knowing what a run is.</description></item>
/// <item><description><b>Three acquisition scopes</b>, including a sequence span — which is not a level, and
/// is the reason the acquisition unit cannot be a single level identifier.</description></item>
/// <item><description><b>One file to many units.</b> Two booleans and a join, rather than a foreign key on
/// either side.</description></item>
/// </list>
/// <para>What it does <i>not</i> use is as informative: no variant axis, no grouping axis, no second format
/// family. Those belong to other kinds, and their absence here costs one omitted property each.</para>
/// </remarks>
public sealed class TvShape : IMediaShapeProvider
{
    private static readonly IReadOnlyList<QualityTier> VideoLadder =
    [
        new QualityTier("SDTV", 1, "tv", "480p"),
        new QualityTier("WEBRip-480p", 2, "webrip", "480p"),
        new QualityTier("WEBDL-480p", 3, "webdl", "480p"),
        new QualityTier("DVD", 4, "dvd", "480p"),
        new QualityTier("Bluray-480p", 5, "bluray", "480p"),
        new QualityTier("HDTV-720p", 6, "tv", "720p"),
        new QualityTier("WEBRip-720p", 7, "webrip", "720p"),
        new QualityTier("WEBDL-720p", 8, "webdl", "720p"),
        new QualityTier("Bluray-720p", 9, "bluray", "720p"),
        new QualityTier("HDTV-1080p", 10, "tv", "1080p"),
        new QualityTier("Raw-HD", 11, "raw", "1080p"),
        new QualityTier("WEBRip-1080p", 12, "webrip", "1080p"),
        new QualityTier("WEBDL-1080p", 13, "webdl", "1080p"),
        new QualityTier("Bluray-1080p", 14, "bluray", "1080p"),
        new QualityTier("Bluray-1080p Remux", 15, "bluray-remux", "1080p"),
        new QualityTier("HDTV-2160p", 16, "tv", "2160p"),
        new QualityTier("WEBRip-2160p", 17, "webrip", "2160p"),
        new QualityTier("WEBDL-2160p", 18, "webdl", "2160p"),
        new QualityTier("Bluray-2160p", 19, "bluray", "2160p"),
        new QualityTier("Bluray-2160p Remux", 20, "bluray-remux", "2160p")
    ];

    private static readonly QualityTier UnknownTier = new("Unknown", 0, "unknown");

    private static readonly MediaShape Declaration = Build();

    /// <summary>The declared quality ladder, ascending by rank. Shared with the quality model.</summary>
    public static IReadOnlyList<QualityTier> Ladder => VideoLadder;

    /// <summary>The tier a release is given when nothing in its title identifies one.</summary>
    public static QualityTier Unknown => UnknownTier;

    /// <summary>Every token name the naming policy will substitute, braces included.</summary>
    public static IReadOnlyList<string> DeclaredTokenNames { get; } =
        [.. Declaration.Tokens.Select(token => token.Name)];

    /// <summary>The token names a template must contain to be valid.</summary>
    public static IReadOnlyList<string> RequiredTokenNames { get; } =
        [.. Declaration.Tokens.Where(token => token.IsRequired).Select(token => token.Name)];

    /// <inheritdoc />
    public MediaShape Shape => Declaration;

    private static MediaShape Build() => new()
    {
        Kind = TvIds.MediaKind,
        Name = "Television",
        PluralName = "Television",
        Levels = [BuildSeriesLevel(), BuildEpisodeLevel()],

        FileBinding = new FileBinding
        {
            // Anchor and unit coincide here. They do not in every kind - a file can hang two levels above
            // the unit it satisfies - which is why the binding carries both rather than one "file-bearing
            // level".
            AnchorLevelId = TvIds.EpisodeLevel,
            UnitLevelId = TvIds.EpisodeLevel,

            // A unit has at most one file...
            AtMostOneFilePerUnit = true,

            // ...but a file may satisfy several units. This is the multi-unit release, and it is the pair
            // of booleans - not a cardinality enum - that says so, because the booleans ARE the uniqueness
            // constraints the store enforces.
            AtMostOneUnitPerFile = false,

            // An ordinal only means something when a unit spans files, which is the opposite arrangement.
            OrdinalIsMeaningful = false,

            // The constraint the surveyed implementation expresses as a thrown exception, declared instead:
            // a file may straddle the inner component but never the outer one.
            SpanConstraints =
            [
                new SpanConstraint(TvIds.AiredSpaceId, TvIds.SeasonComponentId, SpanRule.MustNotSpan),
                new SpanConstraint(TvIds.AiredSpaceId, TvIds.EpisodeComponentId, SpanRule.MaySpan)
            ]
        },

        CoordinateSpaces = BuildCoordinateSpaces(),
        GroupingAxes = [],
        FormatFamilies =
        [
            new FormatFamily
            {
                FamilyId = TvIds.VideoFamilyId,
                Name = "Video",
                FileExtensions =
                [
                    ".mkv", ".mp4", ".avi", ".m4v", ".mpg", ".mpeg", ".mov", ".wmv", ".ts", ".m2ts", ".webm"
                ],
                Ladder = VideoLadder,
                Unknown = UnknownTier,
                CoexistsWithOtherFamilies = false,
                SupportsEmbeddedMetadata = false
            }
        ],
        SelectionFacets =
        [
            new SelectionFacet
            {
                FacetId = TvIds.SeasonKindFacetId,
                Name = "Sequence entries",
                AppliesToLevelId = TvIds.EpisodeLevel,
                Kind = SelectionFacetKind.Enumerated,
                MultiValued = true,
                Values =
                [
                    new FacetValue(TvSequenceKinds.Regular, "In the numbered run"),
                    new FacetValue(TvSequenceKinds.OutOfRun, "Outside the numbered run")
                ],
                DefaultAllowed = [TvSequenceKinds.Regular, TvSequenceKinds.OutOfRun],

                // Visibility, not materialization: excluding out-of-run entries hides rows, it does not
                // delete them. Two of the four surveyed applications chose the destructive reading by
                // default and it is a documented source of user surprise. Declaring the choice is the answer.
                Application = FacetApplication.Visibility
            }
        ],
        SearchKinds = BuildSearchKinds(),
        Tokens = BuildTokens()
    };

    private static IReadOnlyList<CoordinateSpace> BuildCoordinateSpaces() =>
    [
        new CoordinateSpace
        {
            SpaceId = TvIds.AiredSpaceId,
            Name = "Aired",
            Kind = CoordinateKind.Ordinal,
            Components =
            [
                new CoordinateComponent(TvIds.SeasonComponentId, "Season", Required: true),
                new CoordinateComponent(TvIds.EpisodeComponentId, "Episode", Required: true)
            ],

            // Exactly one canonical space per level. Identity and completeness are measured in this one and
            // in no other; the rest are aliases the extension negotiates at runtime.
            IsCanonical = true,
            IsDense = true
        },
        new CoordinateSpace
        {
            SpaceId = TvIds.AbsoluteSpaceId,
            Name = "Absolute",
            Kind = CoordinateKind.Ordinal,
            Components =
            [
                new CoordinateComponent(TvIds.AbsoluteComponentId, "Number", Required: true)
            ],

            // Not dense: out-of-run entries are never given a flat ordinal, so a gap does not mean
            // "missing" here and completeness must not be measured against it.
            IsDense = false
        },
        new CoordinateSpace
        {
            SpaceId = TvIds.AirDateSpaceId,
            Name = "Transmission date",
            Kind = CoordinateKind.Date,

            // A date space has no components, and it is never dense: a series that runs on weekdays has a
            // hole every Saturday and that hole is not a missing unit.
            IsDense = false
        },
        new CoordinateSpace
        {
            SpaceId = TvIds.SceneSpaceId,
            Name = "Release-community numbering",
            Kind = CoordinateKind.Ordinal,
            Components =
            [
                new CoordinateComponent(TvIds.SeasonComponentId, "Season", Required: true),
                new CoordinateComponent(TvIds.EpisodeComponentId, "Episode", Required: true)
            ],

            // Valid only when the string it came from was a release name. The same text resolves
            // differently depending on whether it arrived from an indexer or off a disk, and that
            // difference is provenance, not parsing.
            IsProvenanceSensitive = true,

            // The mapping is partial. Where it is unknown the mapper extrapolates and flags its own
            // confidence, which is why the reading carries a confidence and the space carries permission
            // for one to be less than verified.
            MayBeUnverified = true,
            IsDense = false
        },
        new CoordinateSpace
        {
            SpaceId = TvIds.SceneAbsoluteSpaceId,
            Name = "Release-community flat numbering",
            Kind = CoordinateKind.Ordinal,
            Components =
            [
                new CoordinateComponent(TvIds.AbsoluteComponentId, "Number", Required: true)
            ],
            IsProvenanceSensitive = true,
            MayBeUnverified = true,
            IsDense = false
        }
    ];

    private static MediaLevel BuildSeriesLevel() => new()
    {
        Id = TvIds.SeriesLevel,
        Name = "Series",
        PluralName = "Series",
        Parent = null,

        // The library entry and nothing else. It is not the acquisition unit - a search targets units, with
        // the entry as an ancestor scope - and it bears no file.
        Roles = MediaLevelRoles.LibraryEntry,

        Identity = new LevelIdentity
        {
            // The surveyed implementation has no catalog/library split at this level and pays for it: the
            // same entry cannot be shared between a library row and an import-list row. Three of the four
            // applications invented the split independently, so it is declared here from the start.
            HasCatalogRecord = true,
            HasLibraryRecord = true,

            // Catalog identifiers get merged upstream, and a merge must not orphan a library entry.
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = TvIds.TvdbScheme, Name = "TheTVDB", IsPrimary = true },
                new ExternalIdScheme { Scheme = TvIds.TmdbScheme, Name = "TMDb" },
                new ExternalIdScheme { Scheme = TvIds.ImdbScheme, Name = "IMDb" }
            ]
        },

        // The entry itself carries no position. Positions live on the unit.
        CoordinateSpaceIds = [],
        SequenceAxes = [],
        Fields = BuildSeriesFields(),
        MonitorDimensions =
        [
            new MonitorDimension
            {
                DimensionId = TvIds.FutureItemsDimensionId,
                Name = "Future output",
                Kind = MonitorDimensionKind.Enumerated,
                Choices =
                [
                    new FacetValue("all", "Want everything"),
                    new FacetValue("new", "Want only what has not aired yet"),
                    new FacetValue("none", "Want nothing new")
                ],
                DefaultChoice = "all"
            }
        ],
        FormatFamilyIds = [],
        Variant = null
    };

    private static MediaLevel BuildEpisodeLevel() => new()
    {
        Id = TvIds.EpisodeLevel,
        Name = "Episode",
        PluralName = "Episodes",
        Parent = TvIds.SeriesLevel,

        // Three roles on one level. A search targets it, completeness counts it, and a file satisfies it.
        Roles = MediaLevelRoles.AcquisitionUnit
            | MediaLevelRoles.CompletenessUnit
            | MediaLevelRoles.FileBearing,

        Identity = new LevelIdentity
        {
            HasCatalogRecord = true,
            HasLibraryRecord = true,
            SupportsIdentifierRedirects = false,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = TvIds.TvdbScheme, Name = "TheTVDB", IsPrimary = true }
            ]
        },

        // All five spaces are admitted here, and a given unit populates whichever of them are known. That is
        // the bag: six numbering fields on the surveyed row, three of them nullable and one carrying a
        // confidence flag, expressed as readings rather than as columns.
        CoordinateSpaceIds =
        [
            TvIds.AiredSpaceId,
            TvIds.AbsoluteSpaceId,
            TvIds.AirDateSpaceId,
            TvIds.SceneSpaceId,
            TvIds.SceneAbsoluteSpaceId
        ],

        SequenceAxes =
        [
            new SequenceAxis
            {
                AxisId = TvIds.SeasonAxisId,
                Name = "Season",
                PluralName = "Seasons",

                // The axis names a component of the canonical ordinal space. It is emphatically not a level:
                // it was a table for twenty migrations, earned nothing, and was demoted into exactly this.
                SpaceId = TvIds.AiredSpaceId,
                ComponentIndex = 0,

                // ...but it does carry per-(entry, ordinal) state: a monitored bit and artwork. That is the
                // half of the demoted level that survived, and the host has to be told it exists.
                HasPolicyRecord = true,

                Exceptions =
                [
                    // Replaces every hard-coded "outer ordinal greater than zero" test in naming and
                    // statistics. Out-of-run entries exist, are addressable, and must not be counted as
                    // missing when they are absent.
                    new SequenceException(TvIds.SpecialsOrdinal, "Specials", ExcludedFromCompleteness: true)
                ]
            }
        ],

        Fields = BuildEpisodeFields(),
        MonitorDimensions =
        [
            new MonitorDimension
            {
                DimensionId = TvIds.WantedDimensionId,
                Name = "Wanted",
                Kind = MonitorDimensionKind.Toggle
            }
        ],
        FormatFamilyIds = [TvIds.VideoFamilyId],
        Variant = null
    };

    private static IReadOnlyList<SearchKind> BuildSearchKinds() =>
    [
        new SearchKind
        {
            SearchKindId = TvIds.UnitSearchKindId,
            Name = "Episode",
            TargetLevelId = TvIds.EpisodeLevel,
            Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
            RequiredTerms = [SearchTerm.Ordinal],
            OptionalTerms = [SearchTerm.ExternalIdentifier, SearchTerm.WorkTitle, SearchTerm.FreeText],
            Categories = TelevisionCategories()
        },
        new SearchKind
        {
            SearchKindId = TvIds.SeasonPackSearchKindId,
            Name = "Season pack",
            TargetLevelId = TvIds.EpisodeLevel,

            // A pack is a span of the sequence axis. It is not a level, which is precisely why the scope
            // has to be able to name an axis: "which level is the acquisition unit" cannot express it.
            Scope = new AcquisitionScope
            {
                Kind = AcquisitionScopeKind.SequenceSpan,
                SequenceAxisId = TvIds.SeasonAxisId
            },
            RequiredTerms = [SearchTerm.Ordinal],
            OptionalTerms = [SearchTerm.WorkTitle, SearchTerm.FreeText, SearchTerm.ExternalIdentifier],
            Categories = TelevisionCategories()
        },
        new SearchKind
        {
            SearchKindId = TvIds.DailySearchKindId,
            Name = "By transmission date",
            TargetLevelId = TvIds.EpisodeLevel,
            Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },

            // Deliberately no required term. Almost no indexer accepts a date parameter for television, so
            // requiring one would make this search kind ineligible everywhere; in practice the date is
            // rendered into the free text. Declaring zero required terms and letting the category gate do
            // the work is explicitly permitted, and it is the honest description of what happens.
            RequiredTerms = [],
            OptionalTerms = [SearchTerm.Date, SearchTerm.WorkTitle, SearchTerm.FreeText],
            Categories = TelevisionCategories()
        },
        new SearchKind
        {
            SearchKindId = TvIds.SeriesSearchKindId,
            Name = "Whole series",
            TargetLevelId = TvIds.EpisodeLevel,
            Scope = new AcquisitionScope
            {
                Kind = AcquisitionScopeKind.Ancestor,
                AncestorLevelId = TvIds.SeriesLevel
            },
            RequiredTerms = [SearchTerm.WorkTitle],
            OptionalTerms = [SearchTerm.FreeText, SearchTerm.ExternalIdentifier],
            Categories = TelevisionCategories()
        }
    ];

    // Deliberately a method rather than a static property. Static initializers run in source order, and the
    // shape itself is built by one of them - so a property declared below the shape would still be null when
    // the search kinds were constructed, and "required" would not catch it because it is assigned.
    private static IReadOnlyList<CategoryId> TelevisionCategories() =>
    [
        CategoryId.FromInt(5000),
        CategoryId.FromInt(5020),
        CategoryId.FromInt(5030),
        CategoryId.FromInt(5040),
        CategoryId.FromInt(5045),
        CategoryId.FromInt(5050),
        CategoryId.FromInt(5070),
        CategoryId.FromInt(5080)
    ];

    private static IReadOnlyList<FieldDescriptor> BuildSeriesFields() =>
    [
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Title,
            Name = "Title",
            ValueKind = FieldValueKind.Text,
            Semantics = FieldSemantics.Title | FieldSemantics.Searchable | FieldSemantics.Sortable
                | FieldSemantics.Filterable,
            Prominence = Prominence.Primary
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.SortTitle,
            Name = "Sort title",
            ValueKind = FieldValueKind.Text,
            Semantics = FieldSemantics.SortKey | FieldSemantics.Sortable,
            Prominence = Prominence.Diagnostic
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Year,
            Name = "Year",
            ValueKind = FieldValueKind.Integer,
            Semantics = FieldSemantics.Sortable | FieldSemantics.Filterable | FieldSemantics.Groupable
                | FieldSemantics.Disambiguation,
            Prominence = Prominence.Primary
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Status,
            Name = "Status",
            ValueKind = FieldValueKind.Enumerated,
            Semantics = FieldSemantics.Status | FieldSemantics.Filterable | FieldSemantics.Groupable,
            Prominence = Prominence.Secondary,
            Choices =
            [
                new FacetValue(TvStatuses.Continuing, "Continuing"),
                new FacetValue(TvStatuses.Ended, "Ended"),
                new FacetValue(TvStatuses.Upcoming, "Upcoming")
            ]
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.AddressingScheme,
            Name = "Numbering",
            Description = "Which of the declared coordinate spaces this entry's releases usually use.",
            ValueKind = FieldValueKind.Enumerated,
            Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
            Prominence = Prominence.Detail,
            Editable = true,
            Choices =
            [
                new FacetValue(TvAddressingSchemes.Ordinal, "Season and episode"),
                new FacetValue(TvAddressingSchemes.Calendar, "Transmission date"),
                new FacetValue(TvAddressingSchemes.Flat, "Flat numbering")
            ]
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Network,
            Name = "Network",
            ValueKind = FieldValueKind.Text,
            Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
            Prominence = Prominence.Secondary
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Overview,
            Name = "Overview",
            ValueKind = FieldValueKind.MultilineText,
            Prominence = Prominence.Detail
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Runtime,
            Name = "Runtime",
            ValueKind = FieldValueKind.Duration,
            Semantics = FieldSemantics.Sortable | FieldSemantics.Filterable,
            Prominence = Prominence.Detail,
            Unit = "minutes"
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Certification,
            Name = "Certification",
            ValueKind = FieldValueKind.Text,
            Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
            Prominence = Prominence.Detail
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Genres,
            Name = "Genres",
            ValueKind = FieldValueKind.Text,
            Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
            Prominence = Prominence.Secondary,
            Multivalued = true
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.Poster,
            Name = "Poster",
            ValueKind = FieldValueKind.Artwork,
            Semantics = FieldSemantics.Artwork,
            Prominence = Prominence.Primary
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.FirstAired,
            Name = "First aired",
            ValueKind = FieldValueKind.Date,
            Semantics = FieldSemantics.Timestamp | FieldSemantics.Sortable | FieldSemantics.Filterable,
            Prominence = Prominence.Detail
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.TvdbId,
            Name = "TheTVDB id",
            ValueKind = FieldValueKind.ExternalIdentifier,
            Semantics = FieldSemantics.Identity,
            Prominence = Prominence.Diagnostic
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.ImdbId,
            Name = "IMDb id",
            ValueKind = FieldValueKind.ExternalIdentifier,
            Semantics = FieldSemantics.Identity,
            Prominence = Prominence.Diagnostic
        },
        new FieldDescriptor
        {
            FieldId = TvSeriesFields.UsesAliasNumbering,
            Name = "Uses release-community numbering",
            Description = "Whether releases for this entry are addressed in the provenance-sensitive space.",
            ValueKind = FieldValueKind.Boolean,
            Semantics = FieldSemantics.Filterable,
            Prominence = Prominence.Diagnostic,
            Editable = true
        }
    ];

    private static IReadOnlyList<FieldDescriptor> BuildEpisodeFields() =>
    [
        new FieldDescriptor
        {
            FieldId = TvEpisodeFields.Title,
            Name = "Title",
            ValueKind = FieldValueKind.Text,
            Semantics = FieldSemantics.Title | FieldSemantics.Searchable,
            Prominence = Prominence.Primary
        },
        new FieldDescriptor
        {
            FieldId = TvEpisodeFields.Position,
            Name = "Position",
            Description = "The canonical coordinate, rendered.",
            ValueKind = FieldValueKind.Ordinal,
            Semantics = FieldSemantics.Sortable | FieldSemantics.SortKey,
            Prominence = Prominence.Primary
        },
        new FieldDescriptor
        {
            FieldId = TvEpisodeFields.AirDate,
            Name = "Aired",
            ValueKind = FieldValueKind.Date,
            Semantics = FieldSemantics.Timestamp | FieldSemantics.Sortable | FieldSemantics.Filterable,
            Prominence = Prominence.Secondary
        },
        new FieldDescriptor
        {
            FieldId = TvEpisodeFields.Overview,
            Name = "Overview",
            ValueKind = FieldValueKind.MultilineText,
            Prominence = Prominence.Detail
        },
        new FieldDescriptor
        {
            FieldId = TvEpisodeFields.Runtime,
            Name = "Runtime",
            ValueKind = FieldValueKind.Duration,
            Semantics = FieldSemantics.Sortable,
            Prominence = Prominence.Detail,
            Unit = "minutes"
        },
        new FieldDescriptor
        {
            FieldId = TvEpisodeFields.Still,
            Name = "Still",
            ValueKind = FieldValueKind.Artwork,
            Semantics = FieldSemantics.Artwork,
            Prominence = Prominence.Detail
        },
        new FieldDescriptor
        {
            FieldId = TvEpisodeFields.SequenceKind,
            Name = "Sequence placement",
            Description = "Whether the unit's outer ordinal is a declared sequence exception.",
            ValueKind = FieldValueKind.Enumerated,
            Semantics = FieldSemantics.Status | FieldSemantics.Filterable | FieldSemantics.Groupable,
            Prominence = Prominence.Diagnostic,
            Choices =
            [
                new FacetValue(TvSequenceKinds.Regular, "In the numbered run"),
                new FacetValue(TvSequenceKinds.OutOfRun, "Outside the numbered run")
            ]
        }
    ];

    private static IReadOnlyList<NamingToken> BuildTokens() =>
    [
        new NamingToken("{Series Title}", "The library entry's title", "The Expanse", IsRequired: true),
        new NamingToken("{Series CleanTitle}", "The title with punctuation removed", "The Expanse"),
        new NamingToken("{Series TitleYear}", "The title with its year", "The Expanse (2015)"),
        new NamingToken("{Series TitleThe}", "The title with a leading article moved to the end", "Expanse, The"),
        new NamingToken("{season}", "The outer ordinal", "1"),
        new NamingToken("{season:00}", "The outer ordinal, zero-padded to two digits", "01"),
        new NamingToken("{episode}", "The inner ordinal", "4"),
        new NamingToken("{episode:00}", "The inner ordinal, zero-padded to two digits", "04"),
        new NamingToken("{absolute:000}", "The flat ordinal, zero-padded to three digits", "017"),
        new NamingToken("{Air-Date}", "The transmission date", "2015-12-14"),
        new NamingToken("{Episode Title}", "The unit's title", "CQB", IsRequired: true),
        new NamingToken("{Episode CleanTitle}", "The unit's title with punctuation removed", "CQB"),
        new NamingToken("{Season Title}", "The sequence policy record's title", "Season 1"),
        new NamingToken("{Quality Full}", "Quality with its revision", "WEBDL-1080p Proper"),
        new NamingToken("{Quality Title}", "Quality without its revision", "WEBDL-1080p"),
        new NamingToken("{MediaInfo Simple}", "Video codec and audio channels", "x264 5.1"),
        new NamingToken("{MediaInfo Full}", "Video codec, audio codec, channels and languages", "x264 DTS 5.1 [EN]"),
        new NamingToken("{Release Group}", "The group that produced the release", "NTb"),
        new NamingToken("{Custom Formats}", "Custom formats the release matched", "Dolby Vision"),
        new NamingToken("{TvdbId}", "TheTVDB identifier", "280619"),
        new NamingToken("{ImdbId}", "IMDb identifier", "tt3230854"),
        new NamingToken("{Original Filename}", "The file name as it arrived", "the.expanse.s01e04.1080p.mkv")
    ];
}
