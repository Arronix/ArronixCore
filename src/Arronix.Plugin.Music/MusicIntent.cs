using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music;

/// <summary>
/// The column and input identifiers of this extension's working surfaces.
/// </summary>
public static class MusicWorkbench
{
    /// <summary>The read-only column showing which file a row is about.</summary>
    public const string PathColumnId = "path";

    /// <summary>The editable column carrying the item a row resolves to.</summary>
    public const string TargetColumnId = "target";

    /// <summary>The read-only column showing the proposed item's title.</summary>
    public const string TargetTitleColumnId = "target-title";

    /// <summary>The read-only column showing the proposed item's position.</summary>
    public const string PositionColumnId = "position";

    /// <summary>The editable column carrying the copy's tier.</summary>
    public const string QualityColumnId = "quality";

    /// <summary>The read-only column showing a candidate release's name.</summary>
    public const string CandidateTitleColumnId = "candidate";

    /// <summary>The input naming the work an interactive search is for.</summary>
    public const string WorkInputId = "album";
}

/// <summary>
/// What this extension says may be done with music, and how it may be traversed.
/// </summary>
/// <remarks>
/// <para>
/// Declarations only. Nothing here names a technology, a control or a layout: a front end that renders a
/// grid, a terminal that prints a table and an assistant that reads a summary aloud all honor the same
/// declaration, because every member states what a thing <em>means</em> rather than how it should look.
/// </para>
/// <para>
/// The working surfaces are the interesting half. Assigning a folder of loose files to a running order is
/// a daily task, not an exotic one, and "an action taking a collection of triples" is not a usable answer
/// to it. A declared grid over an extension-supplied proposal is.
/// </para>
/// </remarks>
public static class MusicIntent
{
    /// <summary>Searching for a copy of one work.</summary>
    public const string SearchActionId = "search";

    /// <summary>Re-reading the catalog for one subject.</summary>
    public const string RefreshActionId = "refresh";

    /// <summary>Changing whether an item is wanted.</summary>
    public const string SetMonitorActionId = "monitor.set";

    /// <summary>Choosing which pressing defines what complete means.</summary>
    public const string SelectVariantActionId = "variant.select";

    /// <summary>Re-deriving file and folder names from the current template.</summary>
    public const string RenameActionId = "rename";

    /// <summary>Removing a performer from the library.</summary>
    public const string RemoveActionId = "remove";

    /// <summary>Gets the declaration itself, built once.</summary>
    public static PluginIntentSurface Declaration { get; } = Build();

    private static PluginIntentSurface Build() => new()
    {
        MediaKind = MusicShape.Kind,
        BrowseAxes = BuildBrowseAxes(),
        Sorts = BuildSorts(),
        Filters = BuildFilters(),
        Actions = BuildActions(),
        States = BuildStates(),
        ExternalSurfaces = BuildExternalSurfaces(),
        Workbenches = BuildWorkbenches(),
    };

    private static List<BrowseAxis> BuildBrowseAxes() =>
    [
        new BrowseAxis
        {
            AxisId = "hierarchy",
            Name = "By artist",
            Kind = BrowseAxisKind.Hierarchy,
            LevelId = MusicShape.PerformerLevel,
            IsDefault = true,
        },
        new BrowseAxis
        {
            // A run over a coordinate component, not a level. A grid, a "--medium 2" flag and a spoken
            // "disc two" are all honest renderings of the same declaration.
            AxisId = "by-medium",
            Name = "By medium",
            Kind = BrowseAxisKind.Sequence,
            LevelId = MusicShape.RecordingLevel,
            SequenceAxisId = MusicShape.CarrierAxisId,
        },
        new BrowseAxis
        {
            AxisId = "by-type",
            Name = "By type",
            Kind = BrowseAxisKind.Facet,
            LevelId = MusicShape.WorkLevel,
            FieldId = MusicFields.PrimaryKind,
        },
        new BrowseAxis
        {
            AxisId = "all-albums",
            Name = "All albums",
            Kind = BrowseAxisKind.Flat,
            LevelId = MusicShape.WorkLevel,
        },
    ];

    private static List<SortOption> BuildSorts() =>
    [
        new SortOption(MusicFields.SortName, "Artist", SortDirection.Ascending),
        new SortOption(MusicFields.Title, "Title", SortDirection.Ascending),
        new SortOption(MusicFields.ReleaseDate, "Released", SortDirection.Descending),
        new SortOption(MusicFields.RecordingCount, "Recordings", SortDirection.Descending),
    ];

    private static List<FilterOption> BuildFilters() =>
    [
        new FilterOption(
            MusicFields.PrimaryKind,
            "Type",
            FilterOperators.Equals | FilterOperators.In),
        new FilterOption(
            MusicFields.Provenance,
            "Release status",
            FilterOperators.Equals | FilterOperators.In),
        new FilterOption(
            MusicFields.State,
            "State",
            FilterOperators.Equals | FilterOperators.In),
        new FilterOption(
            MusicFields.ReleaseDate,
            "Released",
            FilterOperators.Between | FilterOperators.GreaterThan | FilterOperators.LessThan),
        new FilterOption(
            MusicFields.Genres,
            "Genre",
            FilterOperators.Contains | FilterOperators.In),
    ];

    private static List<ActionDescriptor> BuildActions() =>
    [
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Search,
            ActionId = SearchActionId,
            Name = "Search",
            Description = "Look for a copy of this album.",
            Scope = ActionScope.Item,
            TargetLevelId = MusicShape.WorkLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.None,
            LongRunning = true,
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Refresh,
            ActionId = RefreshActionId,
            Name = "Refresh",
            Description = "Re-read this artist from the catalog.",
            Scope = ActionScope.Item,
            TargetLevelId = MusicShape.PerformerLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.None,
            LongRunning = true,
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.SetMonitoring,
            ActionId = SetMonitorActionId,
            Name = "Set wanted",
            Scope = ActionScope.Selection,
            TargetLevelId = MusicShape.WorkLevel,
            Consequence = Consequence.Safe,
            Confirmation = ConfirmationRequirement.None,
            Parameters =
            [
                new ActionParameter(
                    MusicFields.Wanted,
                    "Wanted",
                    FieldValueKind.Boolean,
                    true,
                    [],
                    "true")
                {
                    StandardParameter = StandardMediaActionParameter.Wanted
                },
            ],
        },
        new ActionDescriptor
        {
            // The declared form of the surprise. Choosing a pressing changes what the platform counts
            // against, so the consequence is stated in a sentence any front end can present rather than
            // discovered from a changed number.
            StandardAction = StandardMediaAction.SelectVariant,
            ActionId = SelectVariantActionId,
            Name = "Use this release",
            Description = "Make this pressing the one completeness is measured against.",
            Scope = ActionScope.Item,
            TargetLevelId = MusicShape.PressingLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.Acknowledge,
            ConsequenceStatement =
                "The number of recordings this album is expected to have will change, and tracks already "
                + "held may become surplus or missing.",
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Rename,
            ActionId = RenameActionId,
            Name = "Rename files",
            Scope = ActionScope.Item,
            TargetLevelId = MusicShape.WorkLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.Acknowledge,
            ConsequenceStatement = "Files on disk will be moved to match the current template.",
            LongRunning = true,
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Remove,
            ActionId = RemoveActionId,
            Name = "Remove",
            Scope = ActionScope.Item,
            TargetLevelId = MusicShape.PerformerLevel,
            Consequence = Consequence.Destructive,
            Confirmation = ConfirmationRequirement.TypeToConfirm,
            ConsequenceStatement = "The artist and everything the library knows about them will be removed.",
        },
    ];

    private static List<StateDescriptor> BuildStates() =>
    [
        new StateDescriptor
        {
            StateId = MusicStates.Missing,
            Name = "Missing",
            Tone = StateTone.Attention,
            SourceFieldId = MusicFields.State,
            Description = "Wanted, issued, and nothing on disk satisfies it.",
        },
        new StateDescriptor
        {
            StateId = MusicStates.Held,
            Name = "In library",
            Tone = StateTone.Positive,
            SourceFieldId = MusicFields.State,
        },
        new StateDescriptor
        {
            StateId = MusicStates.Upgradable,
            Name = "Upgradable",
            Tone = StateTone.Neutral,
            SourceFieldId = MusicFields.State,
            Description = "Held, but below the ceiling the profile asks for.",
        },
        new StateDescriptor
        {
            StateId = MusicStates.Unissued,
            Name = "Not yet issued",
            Tone = StateTone.Neutral,
            SourceFieldId = MusicFields.State,
            Description = "Its absence is not a gap.",
        },
    ];

    private static List<ExternalSurfaceDescriptor> BuildExternalSurfaces() =>
    [
        // Point at, never inject. The platform opens it, runs it through a shell command or reads the
        // address aloud; nothing of this extension's runs anywhere near the front end.
        new ExternalSurfaceDescriptor(
            "catalog-artist",
            "View on MusicBrainz",
            MusicShape.PerformerLevel,
            "https://musicbrainz.org/artist/{" + MusicFields.CatalogId + "}"),
        new ExternalSurfaceDescriptor(
            "catalog-album",
            "View on MusicBrainz",
            MusicShape.WorkLevel,
            "https://musicbrainz.org/release-group/{" + MusicFields.CatalogId + "}"),
    ];

    private static List<WorkbenchDescriptor> BuildWorkbenches() =>
    [
        new WorkbenchDescriptor
        {
            WorkbenchId = MusicItemSource.ManualImportWorkbenchId,
            Name = "Manual import",
            Description = "Assign loose files to the running order of a release.",
            Subject = WorkbenchSubject.LooseFiles,
            TargetLevelId = MusicShape.RecordingLevel,
            Inputs =
            [
                new ActionParameter(
                    MusicItemSource.FolderInputId,
                    "Folder",
                    FieldValueKind.FilePath,
                    true,
                    []),
                new ActionParameter(
                    MusicItemSource.FilesInputId,
                    "Files",
                    FieldValueKind.FilePath,
                    false,
                    []),
                new ActionParameter(
                    MusicItemSource.PressingInputId,
                    "Release",
                    FieldValueKind.Reference,
                    false,
                    [],
                    null,
                    "pressings"),
            ],
            Columns =
            [
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.PathColumnId,
                        Name = "File",
                        ValueKind = FieldValueKind.FilePath,
                        Prominence = Prominence.Primary,
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.TargetColumnId,
                        Name = "Track",
                        ValueKind = FieldValueKind.Reference,
                        Prominence = Prominence.Primary,
                        Editable = true,
                    },
                    Editable = true,

                    // Row-scoped, because the recordings still free for this file are not the ones still
                    // free for the next.
                    OptionSourceId = "free-recordings",
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.TargetTitleColumnId,
                        Name = "Title",
                        ValueKind = FieldValueKind.Text,
                        Prominence = Prominence.Secondary,
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.PositionColumnId,
                        Name = "Position",
                        ValueKind = FieldValueKind.Ordinal,
                        Prominence = Prominence.Secondary,
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.QualityColumnId,
                        Name = "Quality",
                        ValueKind = FieldValueKind.Enumerated,
                        Prominence = Prominence.Secondary,
                        Editable = true,
                    },
                    Editable = true,
                    OptionSourceId = "audio-tiers",
                },
            ],
            CommitLabel = "Import",
            CommitConsequence = Consequence.Costly,
            CommitConfirmation = ConfirmationRequirement.Acknowledge,
            AllowsRowExclusion = true,
        },
        new WorkbenchDescriptor
        {
            WorkbenchId = MusicItemSource.InteractiveSearchWorkbenchId,
            Name = "Interactive search",
            Description = "Choose a candidate release by hand.",
            Subject = WorkbenchSubject.ReleaseCandidates,
            TargetLevelId = MusicShape.WorkLevel,
            Inputs =
            [
                new ActionParameter(
                    MusicWorkbench.WorkInputId,
                    "Album",
                    FieldValueKind.Reference,
                    true,
                    []),
            ],
            Columns =
            [
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.CandidateTitleColumnId,
                        Name = "Release",
                        ValueKind = FieldValueKind.Text,
                        Prominence = Prominence.Primary,
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.TargetColumnId,
                        Name = "Pressing",
                        ValueKind = FieldValueKind.Reference,
                        Prominence = Prominence.Secondary,
                        Editable = true,
                    },
                    Editable = true,
                    OptionSourceId = "pressings",
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = MusicWorkbench.QualityColumnId,
                        Name = "Quality",
                        ValueKind = FieldValueKind.Enumerated,
                        Prominence = Prominence.Secondary,
                    },
                },
            ],
            CommitLabel = "Grab",
            CommitConsequence = Consequence.Costly,
            CommitConfirmation = ConfirmationRequirement.None,
            AllowsRowExclusion = true,
        },
    ];
}
