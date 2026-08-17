// The media-shape contracts are experimental; this extension is one of their four reference implementers.
#pragma warning disable ARX0013
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Books;

/// <summary>
/// The shape of books: writer, work, manifestation.
/// </summary>
/// <remarks>
/// <para>
/// The contrast with recorded music is the point of this shape. Music separates the level a release is
/// acquired at from the level a file satisfies, and puts the variant between them. Books collapse the
/// bottom two: the manifestation is simultaneously the thing competing for selection, the thing a file
/// satisfies and the thing completeness is counted in, so the file anchor and the file unit are the same
/// level. Two kinds, two arrangements, one <see cref="FileBinding"/>.
/// </para>
/// <para>
/// Books also carry the two structures no other surveyed kind needed. The first is a collection that cuts
/// across the hierarchy: a work may belong to several, a collection may span several writers, its
/// positions are labels rather than numbers - "2.5" for an interstitial, "1-3" for an omnibus, empty for a
/// companion - and it exists only as long as something belongs to it. That is a grouping axis, not a
/// level, and modeling it as one would be wrong in five separate ways.
/// </para>
/// <para>
/// The second is two format families in one kind. Written and spoken manifestations are not points on one
/// ladder: a spoken copy is not an upgrade over a written one, it is a different thing. Two families with
/// two ladders and two unknown tiers make "any spoken copy outranks every written one" unrepresentable,
/// which is exactly the failure the surveyed original had to live with.
/// </para>
/// </remarks>
public sealed class BooksShape : IMediaShapeProvider
{
    /// <summary>Gets the media kind this shape declares.</summary>
    public static MediaKindId Kind { get; } = MediaKindId.FromString("books");

    /// <summary>Gets the level a user adds: the writer whose output is being followed.</summary>
    public static MediaLevelId WriterLevel { get; } = MediaLevelId.FromString("author");

    /// <summary>Gets the work level, which a search targets.</summary>
    public static MediaLevelId WorkLevel { get; } = MediaLevelId.FromString("book");

    /// <summary>
    /// Gets the manifestation level: the variant, the completeness unit and the file-bearing level all at
    /// once.
    /// </summary>
    public static MediaLevelId ManifestationLevel { get; } = MediaLevelId.FromString("edition");

    /// <summary>Gets the identifier of the work level's single-position coordinate space.</summary>
    public const string WorkSpaceId = "work";

    /// <summary>Gets the identifier of the manifestation level's label coordinate space.</summary>
    public const string ManifestationSpaceId = "edition";

    /// <summary>Gets the identifier of the cross-cutting collection axis.</summary>
    public const string CollectionAxisId = "book-series";

    /// <summary>Gets the identifier of the written format family.</summary>
    public const string WrittenFamilyId = "ebook";

    /// <summary>Gets the identifier of the spoken format family.</summary>
    public const string SpokenFamilyId = "audiobook";

    /// <summary>Gets the identifier of the search that targets one work.</summary>
    public const string WorkSearchKindId = "book";

    /// <summary>Gets the identifier of the search that targets a writer's whole output.</summary>
    public const string CatalogSearchKindId = "author";

    /// <summary>Gets the external catalog scheme the writer and work levels are keyed by.</summary>
    public const string CatalogScheme = "goodreads";

    /// <summary>Gets the external scheme carrying a manifestation's thirteen-digit book number.</summary>
    public const string BookNumberScheme = "isbn13";

    /// <summary>Gets the external scheme carrying a retailer's identifier for a manifestation.</summary>
    public const string RetailerScheme = "asin";

    /// <inheritdoc />
    public MediaShape Shape => Declaration;

    private static MediaShape Build() => new()
    {
        Kind = Kind,
        Name = "Book",
        PluralName = "Books",
        Levels = [BuildWriter(), BuildWork(), BuildManifestation()],

        // Anchor and unit are the same level, which is the whole contrast with recorded music. The two
        // booleans are the interesting part: a manifestation may be spread over several files - a spoken
        // work split by chapter - and no file is ever shared between manifestations. That combination is
        // the only one in which the link's ordinal carries meaning, and it is declared rather than
        // inferred.
        FileBinding = new FileBinding
        {
            AnchorLevelId = ManifestationLevel,
            UnitLevelId = ManifestationLevel,
            AtMostOneFilePerUnit = false,
            AtMostOneUnitPerFile = true,
            OrdinalIsMeaningful = true,
            SpanConstraints = [],
        },

        CoordinateSpaces = BuildCoordinateSpaces(),
        GroupingAxes = [BuildCollectionAxis()],
        FormatFamilies = [BuildWrittenFamily(), BuildSpokenFamily()],
        SelectionFacets = BuildSelectionFacets(),
        SearchKinds = BuildSearchKinds(),
        Tokens = BuildTokens(),
    };

    private static MediaLevel BuildWriter() => new()
    {
        Id = WriterLevel,
        Name = "Author",
        PluralName = "Authors",
        Parent = null,
        Roles = MediaLevelRoles.LibraryEntry,
        Identity = new LevelIdentity
        {
            // Catalog rows outnumber library rows heavily here: a co-writer, a translator and a narrator
            // all need a record and none of them is a library entry.
            HasCatalogRecord = true,
            HasLibraryRecord = true,
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = CatalogScheme, Name = "Goodreads", IsPrimary = true },
            ],
        },
        CoordinateSpaceIds = [],
        SequenceAxes = [],
        MonitorDimensions =
        [
            new MonitorDimension
            {
                DimensionId = BooksFields.FutureOutput,
                Name = "Future books",
                Kind = MonitorDimensionKind.Enumerated,
                Choices =
                [
                    new FacetValue("all", "All"),
                    new FacetValue("new", "New only"),
                    new FacetValue("none", "None"),
                ],
                DefaultChoice = "new",
            },
        ],
        FormatFamilyIds = [],
        Fields =
        [
            new FieldDescriptor
            {
                FieldId = BooksFields.Name,
                Name = "Name",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Identity | FieldSemantics.Title | FieldSemantics.Sortable
                    | FieldSemantics.Searchable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.SortName,
                Name = "Sort name",
                Description = "Family name first, which is how a shelf is ordered.",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.SortKey,
                Prominence = Prominence.Diagnostic,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Disambiguation,
                Name = "Disambiguation",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Disambiguation,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Overview,
                Name = "Overview",
                ValueKind = FieldValueKind.MultilineText,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Genres,
                Name = "Genres",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Multivalued = true,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.CatalogId,
                Name = "Catalog identifier",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity,
                Prominence = Prominence.Diagnostic,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Artwork,
                Name = "Portrait",
                ValueKind = FieldValueKind.Artwork,
                Semantics = FieldSemantics.Artwork,
                Prominence = Prominence.Secondary,
            },
        ],
    };

    private static MediaLevel BuildWork() => new()
    {
        Id = WorkLevel,
        Name = "Book",
        PluralName = "Books",
        Parent = WriterLevel,

        // A search targets the work, not the manifestation: what is asked for is "this title by this
        // writer", and which manifestation the answer turns out to be is settled on import. The
        // manifestation below carries every other role.
        Roles = MediaLevelRoles.AcquisitionUnit,
        Identity = new LevelIdentity
        {
            HasCatalogRecord = true,
            HasLibraryRecord = true,
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = CatalogScheme, Name = "Goodreads", IsPrimary = true },
            ],
        },
        CoordinateSpaceIds = [WorkSpaceId],
        SequenceAxes = [],
        MonitorDimensions =
        [
            new MonitorDimension
            {
                DimensionId = BooksFields.Wanted,
                Name = "Wanted",
                Kind = MonitorDimensionKind.Toggle,
                DefaultChoice = "true",
            },
        ],
        FormatFamilyIds = [],
        Fields =
        [
            new FieldDescriptor
            {
                FieldId = BooksFields.Title,
                Name = "Title",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Title | FieldSemantics.Sortable | FieldSemantics.Searchable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Subtitle,
                Name = "Subtitle",
                Description = "Stripped before a query is issued, because indexers rarely carry it.",
                ValueKind = FieldValueKind.Text,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.ReleaseDate,
                Name = "First published",
                ValueKind = FieldValueKind.Date,
                Semantics = FieldSemantics.Sortable | FieldSemantics.Filterable | FieldSemantics.Timestamp,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Popularity,
                Name = "Popularity",
                Description = "Rating multiplied by rating count.",
                ValueKind = FieldValueKind.Decimal,
                Semantics = FieldSemantics.Sortable | FieldSemantics.Filterable,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.CollectionName,
                Name = "Series",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Groupable | FieldSemantics.Filterable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                // A label rather than a number, deliberately. "2.5", "1-3" and the empty string are all
                // legal positions and none of them is an integer.
                FieldId = BooksFields.CollectionPosition,
                Name = "Series position",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Sortable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.ManifestationCount,
                Name = "Editions",
                ValueKind = FieldValueKind.Count,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.State,
                Name = "State",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Status | FieldSemantics.Filterable,
                Prominence = Prominence.Primary,
                Choices = StateChoices,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Overview,
                Name = "Overview",
                ValueKind = FieldValueKind.MultilineText,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.CatalogId,
                Name = "Catalog identifier",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity,
                Prominence = Prominence.Diagnostic,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Artwork,
                Name = "Cover",
                ValueKind = FieldValueKind.Artwork,
                Semantics = FieldSemantics.Artwork,
                Prominence = Prominence.Secondary,
            },
        ],
    };

    private static MediaLevel BuildManifestation() => new()
    {
        Id = ManifestationLevel,
        Name = "Edition",
        PluralName = "Editions",
        Parent = WorkLevel,

        // Three roles on one level. Music spreads the same three across three levels; the shape model has
        // to express both, and this is what it looks like when they fuse.
        Roles = MediaLevelRoles.VariantAxis
            | MediaLevelRoles.CompletenessUnit
            | MediaLevelRoles.FileBearing,
        Identity = new LevelIdentity
        {
            HasCatalogRecord = true,
            HasLibraryRecord = false,
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = CatalogScheme, Name = "Goodreads", IsPrimary = true },
                new ExternalIdScheme { Scheme = BookNumberScheme, Name = "ISBN-13" },
                new ExternalIdScheme { Scheme = RetailerScheme, Name = "ASIN" },
            ],
        },

        // Manual only, and that is the contrast with music worth reading twice. Importing a written copy
        // of a work whose spoken manifestation is selected must not silently re-point the work: the two
        // are not competing copies of the same artifact, they are different artifacts, and switching
        // between them changes what the reader is expected to hold.
        Variant = new VariantSelection
        {
            AutoSwitchByDefault = true,
            Triggers = SelectionTrigger.Manual,
            CompletenessIsVariantRelative = true,
        },
        CoordinateSpaceIds = [ManifestationSpaceId],
        SequenceAxes = [],
        MonitorDimensions = [],
        FormatFamilyIds = [WrittenFamilyId, SpokenFamilyId],
        Fields =
        [
            new FieldDescriptor
            {
                FieldId = BooksFields.Title,
                Name = "Title",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Title | FieldSemantics.Sortable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                // The declared discriminator between the two families, as a field rather than only as a
                // property of the file, because it is what a reader filters and groups by.
                FieldId = BooksFields.Flavor,
                Name = "Flavor",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Prominence = Prominence.Primary,
                Choices = FlavorChoices,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.CarrierDescription,
                Name = "Format",
                Description = "The catalog's own words for the carrier.",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Filterable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Issuer,
                Name = "Publisher",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Filterable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.PageCount,
                Name = "Pages",
                ValueKind = FieldValueKind.Count,
                Semantics = FieldSemantics.Sortable | FieldSemantics.Filterable,
                Prominence = Prominence.Secondary,
                Unit = "pages",
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.RunningTime,
                Name = "Running time",
                ValueKind = FieldValueKind.Duration,
                Semantics = FieldSemantics.Sortable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Language,
                Name = "Language",
                ValueKind = FieldValueKind.Language,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.ReleaseDate,
                Name = "Published",
                ValueKind = FieldValueKind.Date,
                Semantics = FieldSemantics.Sortable | FieldSemantics.Timestamp,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.BookNumber,
                Name = "ISBN-13",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity | FieldSemantics.Searchable,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.RetailerId,
                Name = "ASIN",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity | FieldSemantics.Searchable,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.Disambiguation,
                Name = "Disambiguation",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Disambiguation,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.State,
                Name = "State",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Status | FieldSemantics.Filterable,
                Prominence = Prominence.Primary,
                Choices = StateChoices,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.CatalogId,
                Name = "Catalog identifier",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity,
                Prominence = Prominence.Diagnostic,
            },
        ],
    };

    private static List<CoordinateSpace> BuildCoordinateSpaces() =>
    [
        new CoordinateSpace
        {
            // A work occupies exactly one position: itself. Declaring the degenerate case rather than
            // omitting it keeps every level addressable the same way.
            SpaceId = WorkSpaceId,
            Name = "Work",
            Kind = CoordinateKind.Singleton,
            IsCanonical = true,
        },
        new CoordinateSpace
        {
            // Manifestations are addressed by label - "Kindle Edition", "Audible", "1st hardcover" - and
            // labels are equatable rather than orderable. There is no dense run here and therefore no
            // meaningful notion of a gap, which is why the density flag is left off.
            SpaceId = ManifestationSpaceId,
            Name = "Edition",
            Kind = CoordinateKind.Label,
            IsCanonical = true,
        },
    ];

    private static GroupingAxis BuildCollectionAxis() => new()
    {
        AxisId = CollectionAxisId,
        Name = "Series",
        PluralName = "Series",
        MemberLevelId = WorkLevel,

        // Genuinely many-to-many: a work sits in several collections, a collection spans several writers.
        Arity = GroupingArity.ManyToMany,

        // Labeled rather than ordinal - see the position field's remark.
        Position = MemberPosition.Labeled,

        // Single-valued naming tokens need a designated member when the relation is many-valued.
        HasPrimaryMember = true,

        // Not monitorable: a collection has no path, no profile and no added-at date. It is not something
        // a user adds.
        IsMonitorable = false,

        // But it is something a user can follow as a source of new works.
        IsDiscoverySource = true,

        // It dies when its last member leaves, which is the opposite of hierarchical ownership.
        Lifetime = GroupLifetime.RefCounted,

        // And it has its own fetch path, which no other surveyed grouping needed. A group with its own
        // metadata is a record, not a label, and its record now declares its schema here rather than
        // living in a private naming convention: the title the browse view and the {Book Series} token
        // read, and the catalog identifier the fetch path keys on.
        HasOwnMetadata = true,
        Fields =
        [
            new FieldDescriptor
            {
                FieldId = BooksFields.Title,
                Name = "Title",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Identity | FieldSemantics.Title | FieldSemantics.Sortable
                    | FieldSemantics.Searchable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = BooksFields.CatalogId,
                Name = "Catalog identifier",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity,
                Prominence = Prominence.Diagnostic,
            },
        ],
        ExternalIds =
        [
            new ExternalIdScheme { Scheme = CatalogScheme, Name = "Goodreads", IsPrimary = true },
        ],
    };

    private static FormatFamily BuildWrittenFamily() => new()
    {
        FamilyId = WrittenFamilyId,
        Name = "eBook",
        FileExtensions = [".epub", ".mobi", ".azw", ".azw3", ".pdf", ".lit", ".fb2", ".djvu"],
        Ladder =
        [
            new QualityTier("PDF", 1, "text", null, "PDF"),
            new QualityTier("MOBI", 2, "text", null, "MOBI"),
            new QualityTier("AZW3", 3, "text", null, "AZW3"),
            new QualityTier("EPUB", 4, "text", null, "EPUB"),
        ],

        // One unknown tier per family. A single ladder needs two, interleaved, and the second one is the
        // tell that the ladder was carrying a dimension it had no room for.
        Unknown = new QualityTier("Unknown Text", 0),

        // Both families may be held for the same work at the same time. They are alternatives to choose
        // between, not competitors on one scale.
        CoexistsWithOtherFamilies = true,
        SupportsEmbeddedMetadata = true,
    };

    private static FormatFamily BuildSpokenFamily() => new()
    {
        FamilyId = SpokenFamilyId,
        Name = "Audiobook",
        FileExtensions = [".m4b", ".mp3", ".m4a", ".flac", ".ogg", ".opus", ".aac", ".aa", ".aax"],
        Ladder =
        [
            new QualityTier("MP3", 1, "audio", null, "MP3"),
            new QualityTier("AAC", 2, "audio", null, "AAC"),
            new QualityTier("M4B", 3, "audio", null, "AAC"),
            new QualityTier("FLAC", 4, "audio", null, "FLAC"),
        ],
        Unknown = new QualityTier("Unknown Audio", 0),
        CoexistsWithOtherFamilies = true,
        SupportsEmbeddedMetadata = true,
    };

    private static List<SelectionFacet> BuildSelectionFacets() =>
    [
        new SelectionFacet
        {
            FacetId = BooksFacets.Language,
            Name = "Languages",
            AppliesToLevelId = ManifestationLevel,
            Kind = SelectionFacetKind.Enumerated,
            Values =
            [
                new FacetValue("eng", "English"),
                new FacetValue("fra", "French"),
                new FacetValue("deu", "German"),
                new FacetValue("spa", "Spanish"),
                new FacetValue("und", "Unknown"),
            ],
            MultiValued = true,
            DefaultAllowed = ["eng", "und"],
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            // A threshold, not an enumeration. The surveyed original needed exactly this and an
            // enumerated-only vocabulary could not express it.
            FacetId = BooksFacets.MinimumPopularity,
            Name = "Minimum popularity",
            AppliesToLevelId = WorkLevel,
            Kind = SelectionFacetKind.Threshold,
            ThresholdDirection = ThresholdDirection.AtLeast,
            DefaultNumber = 350,
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            FacetId = BooksFacets.MinimumPages,
            Name = "Minimum pages",
            AppliesToLevelId = ManifestationLevel,
            Kind = SelectionFacetKind.Threshold,
            ThresholdDirection = ThresholdDirection.AtLeast,
            DefaultNumber = 0,
            Unit = "pages",
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            FacetId = BooksFacets.SkipUndated,
            Name = "Skip books with no publication date",
            AppliesToLevelId = WorkLevel,
            Kind = SelectionFacetKind.Flag,
            DefaultFlag = true,
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            FacetId = BooksFacets.SkipUnidentified,
            Name = "Skip editions with no public identifier",
            AppliesToLevelId = ManifestationLevel,
            Kind = SelectionFacetKind.Flag,
            DefaultFlag = false,
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            FacetId = BooksFacets.SkipPartsAndSets,
            Name = "Skip omnibuses and single parts",
            AppliesToLevelId = WorkLevel,
            Kind = SelectionFacetKind.Flag,
            DefaultFlag = true,
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            FacetId = BooksFacets.SkipCollectionSecondary,
            Name = "Skip companion entries",
            AppliesToLevelId = WorkLevel,
            Kind = SelectionFacetKind.Flag,
            DefaultFlag = false,

            // The one facet declared as hiding rather than removing. It is a presentation preference about
            // works that genuinely exist, and removing rows for it would destroy user state on a whim.
            Application = FacetApplication.Visibility,
        },
    ];

    private static List<SearchKind> BuildSearchKinds() =>
    [
        new SearchKind
        {
            SearchKindId = WorkSearchKindId,
            Name = "Book",
            TargetLevelId = WorkLevel,
            Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
            RequiredTerms = [SearchTerm.WorkTitle, SearchTerm.Creator],
            OptionalTerms = [SearchTerm.FreeText, SearchTerm.Year, SearchTerm.ExternalIdentifier],

            // Written and spoken copies live under different interop category trees - spoken copies sit
            // under audio, not under books - so a kind carrying both families declares both bands. Nothing
            // in the platform has to know why.
            Categories = AllCategories,
        },
        new SearchKind
        {
            SearchKindId = CatalogSearchKindId,
            Name = "Author",
            TargetLevelId = WorkLevel,
            Scope = new AcquisitionScope
            {
                Kind = AcquisitionScopeKind.Ancestor,
                AncestorLevelId = WriterLevel,
            },
            RequiredTerms = [SearchTerm.Creator],
            OptionalTerms = [SearchTerm.FreeText],
            Categories = AllCategories,
        },
    ];

    private static List<NamingToken> BuildTokens() =>
    [
        new NamingToken("{Author Name}", "The writer credited with the work.", "Ursula K. Le Guin", true),
        new NamingToken("{Author Sort Name}", "The writer's shelving form.", "Le Guin, Ursula K."),
        new NamingToken("{Book Title}", "The full title of the work.", "The Left Hand of Darkness", true),
        new NamingToken("{Book TitleNoSub}", "The title with the subtitle removed.", "The Dispossessed"),
        new NamingToken("{Book Subtitle}", "The part of the title after the colon.", "An Ambiguous Utopia"),
        new NamingToken("{Book Series}", "The collection the work belongs to.", "Hainish Cycle"),
        new NamingToken("{Book SeriesPosition}", "Its label within that collection.", "4"),
        new NamingToken("{Edition Title}", "The manifestation's own title.", "The Left Hand of Darkness"),
        new NamingToken("{Edition Format}", "How the manifestation is carried.", "EPUB"),
        new NamingToken("{Release Year}", "The year the work was first published.", "1969"),
        new NamingToken("{Edition Year}", "The year this manifestation was issued.", "2017"),
        new NamingToken("{Part Number}", "Which part of the manifestation a file carries.", "03"),
        new NamingToken("{Part Count}", "How many parts the manifestation is spread over.", "12"),
    ];

    /// <summary>Gets the newznab interop categories a book search is dispatched under.</summary>
    /// <remarks>
    /// The 7000 band is books and the 3030 entry is spoken word, which sits under audio rather than under
    /// books. That is not a mistake in the taxonomy, it is the taxonomy, and carrying it verbatim is what
    /// keeps every deployed indexer on earth answerable without a mapping table.
    /// </remarks>
    internal static IReadOnlyList<CategoryId> AllCategories { get; } =
    [
        CategoryId.FromInt(7000),
        CategoryId.FromInt(7020),
        CategoryId.FromInt(7050),
        CategoryId.FromInt(3030),
    ];

    /// <summary>Gets the interop categories carrying written copies.</summary>
    internal static IReadOnlyList<CategoryId> WrittenCategories { get; } =
    [
        CategoryId.FromInt(7000),
        CategoryId.FromInt(7020),
        CategoryId.FromInt(7050),
    ];

    /// <summary>Gets the interop categories carrying spoken copies.</summary>
    internal static IReadOnlyList<CategoryId> SpokenCategories { get; } =
    [
        CategoryId.FromInt(3030),
    ];

    private static IReadOnlyList<FacetValue> FlavorChoices { get; } =
    [
        new FacetValue(WrittenFamilyId, "eBook"),
        new FacetValue(SpokenFamilyId, "Audiobook"),
    ];

    private static IReadOnlyList<FacetValue> StateChoices { get; } =
    [
        new FacetValue(BooksStates.Missing, "Missing"),
        new FacetValue(BooksStates.Partial, "Partial"),
        new FacetValue(BooksStates.Held, "In library"),
        new FacetValue(BooksStates.Upgradable, "Upgradable"),
        new FacetValue(BooksStates.Unpublished, "Not yet published"),
    ];

    /// <summary>Gets the declaration itself, built once.</summary>
    /// <remarks>
    /// Declared last on purpose. Static initializers run in textual order, and the builder reads the
    /// shared choice and category lists below it; hoisting this property above them would silently build
    /// the whole shape out of nulls.
    /// </remarks>
    public static MediaShape Declaration { get; } = Build();
}
