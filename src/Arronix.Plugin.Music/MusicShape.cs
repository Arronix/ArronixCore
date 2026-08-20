using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music;

/// <summary>
/// The shape of recorded music: performer, work, pressing, recording.
/// </summary>
/// <remarks>
/// <para>
/// This shape exists to prove three things the simpler kinds cannot. First, the level a release is
/// <em>acquired</em> at is not the level a file <em>satisfies</em>: a torrent or an archive delivers one
/// work, and the file rows hang there, while the items that count towards "complete" sit two levels
/// below. <see cref="FileBinding.AnchorLevelId"/> and <see cref="FileBinding.UnitLevelId"/> therefore
/// differ, and file ownership survives a change of selected pressing.
/// </para>
/// <para>
/// Second, one work has competing manifestations - the original pressing, the remaster with bonus
/// recordings, the regional edition with a different running order - and exactly one of them defines what
/// "complete" means at any moment. That is a level carrying <see cref="MediaLevelRoles.VariantAxis"/>,
/// with a declared permission for the platform to switch the selection when files arrive.
/// </para>
/// <para>
/// Third, a work is internally partitioned into an ordered run - the physical carriers a long work was
/// pressed across - which is a coordinate component and a sequence axis, never a level and never a row.
/// Unlike the broadcast kind's equivalent run it carries no policy record: nothing is monitored, named or
/// scheduled per carrier, only numbered.
/// </para>
/// </remarks>
public sealed class MusicShape : IMediaShapeProvider
{
    /// <summary>Gets the media kind this shape declares.</summary>
    public static MediaKindId Kind { get; } = MediaKindId.FromString("music");

    /// <summary>Gets the level a user adds: the performer whose output is being followed.</summary>
    public static MediaLevelId PerformerLevel { get; } = MediaLevelId.FromString("artist");

    /// <summary>Gets the level a search targets and a file record hangs from.</summary>
    public static MediaLevelId WorkLevel { get; } = MediaLevelId.FromString("album");

    /// <summary>Gets the variant level: one competing pressing of the work.</summary>
    public static MediaLevelId PressingLevel { get; } = MediaLevelId.FromString("release");

    /// <summary>Gets the level completeness is counted in, and the level a file satisfies.</summary>
    public static MediaLevelId RecordingLevel { get; } = MediaLevelId.FromString("track");

    /// <summary>Gets the identifier of the canonical, dense, two-component coordinate space.</summary>
    public const string CarrierPositionSpaceId = "medium-track";

    /// <summary>Gets the identifier of the flat alias space over the whole work.</summary>
    public const string AbsolutePositionSpaceId = "absolute";

    /// <summary>Gets the identifier of the label space used by carriers with lettered sides.</summary>
    public const string SidePositionSpaceId = "side-position";

    /// <summary>Gets the identifier of the ordered run of carriers within one pressing.</summary>
    public const string CarrierAxisId = "medium";

    /// <summary>Gets the identifier of the single format family.</summary>
    public const string AudioFamilyId = "audio";

    /// <summary>Gets the identifier of the search that targets one work.</summary>
    public const string WorkSearchKindId = "album";

    /// <summary>Gets the identifier of the search that targets a performer's whole output.</summary>
    public const string CatalogSearchKindId = "discography";

    /// <summary>Gets the external catalog scheme every level is keyed by.</summary>
    public const string CatalogScheme = "musicbrainz";

    /// <inheritdoc />
    public MediaShape Shape => Declaration;

    private static MediaShape Build() => new()
    {
        Kind = Kind,
        Name = "Music",
        PluralName = "Music",
        Levels = [BuildPerformer(), BuildWork(), BuildPressing(), BuildRecording()],

        // The decisive cell of the acceptance table. The file row hangs at the level the release was
        // acquired at, two levels above the items it satisfies, so re-selecting a pressing re-points the
        // items without orphaning a single file.
        FileBinding = new FileBinding
        {
            AnchorLevelId = WorkLevel,
            UnitLevelId = RecordingLevel,
            AtMostOneFilePerUnit = true,
            AtMostOneUnitPerFile = false,
            OrdinalIsMeaningful = false,
            SpanConstraints = [],
        },

        CoordinateSpaces = BuildCoordinateSpaces(),
        GroupingAxes = [],
        FormatFamilies = [BuildAudioFamily()],
        SelectionFacets = BuildSelectionFacets(),
        SearchKinds = BuildSearchKinds(),
        Tokens = BuildTokens(),
    };

    private static MediaLevel BuildPerformer() => new()
    {
        Id = PerformerLevel,
        Name = "Artist",
        PluralName = "Artists",
        Parent = null,
        Roles = MediaLevelRoles.LibraryEntry,
        Identity = new LevelIdentity
        {
            // A performer record is shared: a compilation credits a hundred of them and the user has added
            // none. The library half is the added-and-followed half, and it is a strict subset.
            HasCatalogRecord = true,
            HasLibraryRecord = true,
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = CatalogScheme, Name = "MusicBrainz", IsPrimary = true },
                new ExternalIdScheme { Scheme = "discogs", Name = "Discogs" },
            ],
        },
        CoordinateSpaceIds = [],
        SequenceAxes = [],
        MonitorDimensions =
        [
            new MonitorDimension
            {
                DimensionId = MusicFields.FutureOutput,
                Name = "Future releases",
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
                FieldId = MusicFields.Name,
                Name = "Name",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Identity | FieldSemantics.Title | FieldSemantics.Sortable
                    | FieldSemantics.Searchable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.SortName,
                Name = "Sort name",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.SortKey,
                Prominence = Prominence.Diagnostic,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Disambiguation,
                Name = "Disambiguation",
                Description = "What tells two performers of the same name apart.",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Disambiguation,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.PerformerKind,
                Name = "Type",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Prominence = Prominence.Secondary,
                Choices =
                [
                    new FacetValue("person", "Person"),
                    new FacetValue("group", "Group"),
                    new FacetValue("orchestra", "Orchestra"),
                    new FacetValue("choir", "Choir"),
                    new FacetValue("character", "Character"),
                    new FacetValue("other", "Other"),
                ],
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Genres,
                Name = "Genres",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Multivalued = true,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Overview,
                Name = "Overview",
                ValueKind = FieldValueKind.MultilineText,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.CatalogId,
                Name = "MusicBrainz identifier",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity,
                Prominence = Prominence.Diagnostic,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Artwork,
                Name = "Image",
                ValueKind = FieldValueKind.Artwork,
                Semantics = FieldSemantics.Artwork,
                Prominence = Prominence.Secondary,
            },
        ],
    };

    private static MediaLevel BuildWork() => new()
    {
        Id = WorkLevel,
        Name = "Album",
        PluralName = "Albums",
        Parent = PerformerLevel,

        // AcquisitionUnit because a grab targets the whole work; FileBearing because the file row hangs
        // here. Note what is absent: CompletenessUnit. That is the point of this shape.
        Roles = MediaLevelRoles.AcquisitionUnit | MediaLevelRoles.FileBearing,
        Identity = new LevelIdentity
        {
            HasCatalogRecord = true,
            HasLibraryRecord = true,
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = CatalogScheme, Name = "MusicBrainz", IsPrimary = true },
            ],
        },
        CoordinateSpaceIds = [],
        SequenceAxes = [],
        MonitorDimensions =
        [
            new MonitorDimension
            {
                DimensionId = MusicFields.Wanted,
                Name = "Wanted",
                Kind = MonitorDimensionKind.Toggle,
                DefaultChoice = "true",
            },
        ],
        FormatFamilyIds = [AudioFamilyId],
        Fields =
        [
            new FieldDescriptor
            {
                FieldId = MusicFields.Title,
                Name = "Title",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Title | FieldSemantics.Sortable | FieldSemantics.Searchable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Disambiguation,
                Name = "Disambiguation",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Disambiguation,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.ReleaseDate,
                Name = "Released",
                ValueKind = FieldValueKind.Date,
                Semantics = FieldSemantics.Sortable | FieldSemantics.Filterable | FieldSemantics.Timestamp,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.PrimaryKind,
                Name = "Type",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Prominence = Prominence.Secondary,
                Choices = PrimaryKindChoices,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.SecondaryKinds,
                Name = "Secondary types",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Multivalued = true,
                Prominence = Prominence.Detail,
                Choices = SecondaryKindChoices,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.RecordingCount,
                Name = "Recordings",
                Description = "Counted against the selected pressing, never against the union of all of them.",
                ValueKind = FieldValueKind.Count,
                Semantics = FieldSemantics.Progress,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.State,
                Name = "State",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Status | FieldSemantics.Filterable,
                Prominence = Prominence.Primary,
                Choices = StateChoices,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.CatalogId,
                Name = "MusicBrainz identifier",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity,
                Prominence = Prominence.Diagnostic,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Artwork,
                Name = "Cover",
                ValueKind = FieldValueKind.Artwork,
                Semantics = FieldSemantics.Artwork,
                Prominence = Prominence.Secondary,
            },
        ],
    };

    private static MediaLevel BuildPressing() => new()
    {
        Id = PressingLevel,
        Name = "Release",
        PluralName = "Releases",
        Parent = WorkLevel,
        Roles = MediaLevelRoles.VariantAxis,
        Identity = new LevelIdentity
        {
            // Catalog-only: the user never adds a pressing, the platform records which one is selected
            // on the work above it.
            HasCatalogRecord = true,
            HasLibraryRecord = false,
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = CatalogScheme, Name = "MusicBrainz", IsPrimary = true },
            ],
        },

        // The whole of the surprise, declared. Importing files that belong to a different pressing may
        // re-point the work at that pressing, which silently changes what "complete" counts against. It is
        // surprising when it emerges from an implementation and unsurprising when a kind declares it.
        Variant = new VariantSelection
        {
            AutoSwitchByDefault = true,
            Triggers = SelectionTrigger.OnImport | SelectionTrigger.OnRefresh,
            CompletenessIsVariantRelative = true,
        },
        CoordinateSpaceIds = [],
        SequenceAxes = [],
        MonitorDimensions = [],
        FormatFamilyIds = [],
        Fields =
        [
            new FieldDescriptor
            {
                FieldId = MusicFields.Title,
                Name = "Title",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Title | FieldSemantics.Sortable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Provenance,
                Name = "Status",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Groupable,
                Prominence = Prominence.Secondary,
                Choices = ProvenanceChoices,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Issuer,
                Name = "Label",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Filterable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Country,
                Name = "Country",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Filterable,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.CarrierFormat,
                Name = "Format",
                Description = "How the pressing was carried, and across how many carriers.",
                ValueKind = FieldValueKind.Text,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.CarrierCount,
                Name = "Carriers",
                ValueKind = FieldValueKind.Count,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.RecordingCount,
                Name = "Recordings",
                ValueKind = FieldValueKind.Count,
                Semantics = FieldSemantics.Sortable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.ReleaseDate,
                Name = "Released",
                ValueKind = FieldValueKind.Date,
                Semantics = FieldSemantics.Sortable | FieldSemantics.Timestamp,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Disambiguation,
                Name = "Disambiguation",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Disambiguation,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.CatalogId,
                Name = "MusicBrainz identifier",
                ValueKind = FieldValueKind.ExternalIdentifier,
                Semantics = FieldSemantics.Identity,
                Prominence = Prominence.Diagnostic,
            },
        ],
    };

    private static MediaLevel BuildRecording() => new()
    {
        Id = RecordingLevel,
        Name = "Track",
        PluralName = "Tracks",
        Parent = PressingLevel,
        Roles = MediaLevelRoles.CompletenessUnit | MediaLevelRoles.FileBearing,
        Identity = new LevelIdentity
        {
            HasCatalogRecord = true,
            HasLibraryRecord = false,
            SupportsIdentifierRedirects = true,
            ExternalIds =
            [
                new ExternalIdScheme { Scheme = CatalogScheme, Name = "MusicBrainz", IsPrimary = true },
            ],
        },
        CoordinateSpaceIds = [CarrierPositionSpaceId, AbsolutePositionSpaceId, SidePositionSpaceId],

        // A run over the outermost component of the canonical space, with no policy record: unlike the
        // broadcast kind's equivalent run, nothing is monitored, named, foldered or scheduled per carrier.
        SequenceAxes =
        [
            new SequenceAxis
            {
                AxisId = CarrierAxisId,
                Name = "Medium",
                PluralName = "Media",
                SpaceId = CarrierPositionSpaceId,
                ComponentIndex = 0,
                HasPolicyRecord = false,
                Exceptions = [],
            },
        ],
        MonitorDimensions = [],
        FormatFamilyIds = [AudioFamilyId],
        Fields =
        [
            new FieldDescriptor
            {
                FieldId = MusicFields.Title,
                Name = "Title",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Title | FieldSemantics.Searchable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Position,
                Name = "Position",
                ValueKind = FieldValueKind.Ordinal,
                Semantics = FieldSemantics.Sortable,
                Prominence = Prominence.Primary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Duration,
                Name = "Duration",
                ValueKind = FieldValueKind.Duration,
                Semantics = FieldSemantics.Sortable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                // The credited performer of one recording need not be the performer of the work above it.
                // A compilation has a different one per recording, and naming reads this rather than the
                // work's when they disagree.
                FieldId = MusicFields.Performer,
                Name = "Credited artist",
                ValueKind = FieldValueKind.Text,
                Semantics = FieldSemantics.Filterable | FieldSemantics.Searchable,
                Prominence = Prominence.Secondary,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.Explicit,
                Name = "Explicit",
                ValueKind = FieldValueKind.Boolean,
                Semantics = FieldSemantics.Filterable,
                Prominence = Prominence.Detail,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.State,
                Name = "State",
                ValueKind = FieldValueKind.Enumerated,
                Semantics = FieldSemantics.Status | FieldSemantics.Filterable,
                Prominence = Prominence.Primary,
                Choices = StateChoices,
            },
            new FieldDescriptor
            {
                FieldId = MusicFields.CatalogId,
                Name = "MusicBrainz identifier",
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
            SpaceId = CarrierPositionSpaceId,
            Name = "Medium and track",
            Kind = CoordinateKind.Ordinal,
            Components =
            [
                new CoordinateComponent(MusicComponents.Carrier, "Medium", true),
                new CoordinateComponent(MusicComponents.Position, "Track", true),
            ],
            IsCanonical = true,
            IsDense = true,
        },
        new CoordinateSpace
        {
            SpaceId = AbsolutePositionSpaceId,
            Name = "Absolute position",
            Kind = CoordinateKind.Ordinal,
            Components = [new CoordinateComponent(MusicComponents.Absolute, "Position", true)],
            IsDense = true,
        },
        new CoordinateSpace
        {
            // "A1", "B3" - lettered sides do not parse as numbers and are equatable rather than orderable,
            // which is exactly what the label kind is for.
            SpaceId = SidePositionSpaceId,
            Name = "Side position",
            Kind = CoordinateKind.Label,
        },
    ];

    private static FormatFamily BuildAudioFamily() => new()
    {
        FamilyId = AudioFamilyId,
        Name = "Audio",
        FileExtensions =
        [
            ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".alac", ".wav", ".wv", ".ape", ".wma", ".mpc",
        ],
        Ladder =
        [
            new QualityTier("MP3-128", 1),
            new QualityTier("MP3-192", 2),
            new QualityTier("MP3-256", 3),
            new QualityTier("MP3-320", 4),
            new QualityTier("MP3-VBR", 5),
            new QualityTier("AAC-256", 6),
            new QualityTier("AAC-320", 7),
            new QualityTier("Vorbis", 8),
            new QualityTier("ALAC", 9),
            new QualityTier("FLAC", 10),
            new QualityTier("FLAC 24bit", 11),
            new QualityTier("WAV", 12),
        ],
        Unknown = new QualityTier("Unknown Audio", 0),
        CoexistsWithOtherFamilies = false,
        SupportsEmbeddedMetadata = true,
    };

    private static List<SelectionFacet> BuildSelectionFacets() =>
    [
        // The third profile axis, after "how good must a copy be" and "which releases are acceptable":
        // which works of this performer should exist at all. Materialization rather than Visibility is
        // declared deliberately - the surveyed original removes excluded rows on refresh, and saying so is
        // better than the user discovering it.
        new SelectionFacet
        {
            FacetId = MusicFacets.PrimaryKind,
            Name = "Primary type",
            AppliesToLevelId = WorkLevel,
            Kind = SelectionFacetKind.Enumerated,
            Values = PrimaryKindChoices,
            MultiValued = true,
            DefaultAllowed = ["album"],
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            FacetId = MusicFacets.SecondaryKind,
            Name = "Secondary type",
            AppliesToLevelId = WorkLevel,
            Kind = SelectionFacetKind.Enumerated,
            Values = SecondaryKindChoices,
            MultiValued = true,
            DefaultAllowed = ["studio"],
            Application = FacetApplication.Materialization,
        },
        new SelectionFacet
        {
            FacetId = MusicFacets.Provenance,
            Name = "Release status",
            AppliesToLevelId = PressingLevel,
            Kind = SelectionFacetKind.Enumerated,
            Values = ProvenanceChoices,
            MultiValued = true,
            DefaultAllowed = ["official"],
            Application = FacetApplication.Materialization,
        },
    ];

    private static List<SearchKind> BuildSearchKinds() =>
    [
        new SearchKind
        {
            SearchKindId = WorkSearchKindId,
            Name = "Album",
            TargetLevelId = WorkLevel,
            Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
            RequiredTerms = [SearchTerm.WorkTitle, SearchTerm.Creator],
            OptionalTerms = [SearchTerm.FreeText, SearchTerm.Year, SearchTerm.ExternalIdentifier],
            Categories = AudioCategories,
        },
        new SearchKind
        {
            // The breadth no level can express: everything this performer ever issued, in one archive.
            SearchKindId = CatalogSearchKindId,
            Name = "Discography",
            TargetLevelId = WorkLevel,
            Scope = new AcquisitionScope
            {
                Kind = AcquisitionScopeKind.Ancestor,
                AncestorLevelId = PerformerLevel,
            },
            RequiredTerms = [SearchTerm.Creator],
            OptionalTerms = [SearchTerm.FreeText],
            Categories = AudioCategories,
        },
    ];

    private static List<NamingToken> BuildTokens() =>
    [
        new NamingToken("{Artist Name}", "The performer credited with the work.", "Radiohead", true),
        new NamingToken("{Artist Sort Name}", "The performer's sorting form.", "Radiohead"),
        new NamingToken("{Album Title}", "The title of the work.", "OK Computer", true),
        new NamingToken("{Album Type}", "The work's primary classification.", "Album"),
        new NamingToken("{Release Year}", "The year the work was first issued.", "1997"),
        new NamingToken("{Medium Number}", "The carrier this recording sits on.", "1"),
        new NamingToken("{Medium Format}", "How the pressing was carried.", "CD"),
        new NamingToken("{Track Number}", "The recording's position on its carrier.", "05"),
        new NamingToken("{Track Title}", "The title of the recording.", "Let Down", true),
        new NamingToken("{Track Artist}", "The performer credited on this recording.", "Radiohead"),
    ];

    /// <summary>Gets the newznab interop categories a music search is dispatched under.</summary>
    internal static IReadOnlyList<CategoryId> AudioCategories { get; } =
    [
        CategoryId.FromInt(3000),
        CategoryId.FromInt(3010),
        CategoryId.FromInt(3040),
        CategoryId.FromInt(3050),
    ];

    private static IReadOnlyList<FacetValue> PrimaryKindChoices { get; } =
    [
        new FacetValue("album", "Album"),
        new FacetValue("ep", "EP"),
        new FacetValue("single", "Single"),
        new FacetValue("broadcast", "Broadcast"),
        new FacetValue("other", "Other"),
    ];

    private static IReadOnlyList<FacetValue> SecondaryKindChoices { get; } =
    [
        new FacetValue("studio", "Studio"),
        new FacetValue("compilation", "Compilation"),
        new FacetValue("soundtrack", "Soundtrack"),
        new FacetValue("live", "Live"),
        new FacetValue("remix", "Remix"),
        new FacetValue("spokenword", "Spokenword"),
        new FacetValue("interview", "Interview"),
        new FacetValue("demo", "Demo"),
        new FacetValue("mixtape", "Mixtape"),
    ];

    private static IReadOnlyList<FacetValue> ProvenanceChoices { get; } =
    [
        new FacetValue("official", "Official"),
        new FacetValue("promotion", "Promotion"),
        new FacetValue("bootleg", "Bootleg"),
        new FacetValue("pseudo", "Pseudo-release"),
    ];

    private static IReadOnlyList<FacetValue> StateChoices { get; } =
    [
        new FacetValue(MusicStates.Missing, "Missing"),
        new FacetValue(MusicStates.Held, "In library"),
        new FacetValue(MusicStates.Upgradable, "Upgradable"),
        new FacetValue(MusicStates.Unissued, "Not yet issued"),
    ];

    /// <summary>Gets the declaration itself, built once.</summary>
    /// <remarks>
    /// Declared last on purpose. Static initializers run in textual order, and the builder reads the
    /// shared choice and category lists below it; hoisting this property above them would silently build
    /// the whole shape out of nulls.
    /// </remarks>
    public static MediaShape Declaration { get; } = Build();
}
