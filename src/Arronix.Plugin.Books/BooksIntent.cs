// Shape and intent contracts are experimental; this extension is a reference implementer of both.
#pragma warning disable ARX0013, ARX0016
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Books;

/// <summary>
/// The column and input identifiers of this extension's working surfaces.
/// </summary>
public static class BooksWorkbench
{
    /// <summary>The read-only column showing which file a row is about.</summary>
    public const string PathColumnId = "path";

    /// <summary>The editable column carrying the item a row resolves to.</summary>
    public const string TargetColumnId = "target";

    /// <summary>The editable column carrying which part of the item a file is.</summary>
    public const string PartColumnId = "part";

    /// <summary>The read-only column showing which format family the file belongs to.</summary>
    public const string FlavorColumnId = "flavor";

    /// <summary>The editable column carrying the copy's tier.</summary>
    public const string QualityColumnId = "quality";

    /// <summary>The read-only column showing a candidate release's name.</summary>
    public const string CandidateTitleColumnId = "candidate";
}

/// <summary>
/// What this extension says may be done with books, and how they may be traversed.
/// </summary>
/// <remarks>
/// The browse axes are where the grouping axis earns its place. Traversing the hierarchy gives writers and
/// their works; traversing the collection axis gives reading orders that cross writers entirely, and no
/// hierarchy can produce that view because the relation is many-to-many. Both are declared, neither is
/// privileged, and a front end offers whichever the reader asks for.
/// </remarks>
public static class BooksIntent
{
    /// <summary>Searching for a copy of one work.</summary>
    public const string SearchActionId = "search";

    /// <summary>Re-reading the catalog for one subject.</summary>
    public const string RefreshActionId = "refresh";

    /// <summary>Changing whether an item is wanted.</summary>
    public const string SetMonitorActionId = "monitor.set";

    /// <summary>Choosing which manifestation the library considers canonical.</summary>
    public const string SelectVariantActionId = "variant.select";

    /// <summary>Re-deriving file and folder names from the current template.</summary>
    public const string RenameActionId = "rename";

    /// <summary>Removing a writer from the library.</summary>
    public const string RemoveActionId = "remove";

    /// <summary>Gets the declaration itself, built once.</summary>
    public static PluginIntentSurface Declaration { get; } = Build();

    private static PluginIntentSurface Build() => new()
    {
        MediaKind = BooksShape.Kind,
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
            Name = "By author",
            Kind = BrowseAxisKind.Hierarchy,
            LevelId = BooksShape.WriterLevel,
            IsDefault = true,
        },
        new BrowseAxis
        {
            // The view the hierarchy cannot produce: a reading order that spans writers.
            AxisId = "by-series",
            Name = "By series",
            Kind = BrowseAxisKind.Grouping,
            LevelId = BooksShape.WorkLevel,
            GroupingAxisId = BooksShape.CollectionAxisId,
        },
        new BrowseAxis
        {
            AxisId = "by-flavor",
            Name = "By format",
            Kind = BrowseAxisKind.Facet,
            LevelId = BooksShape.ManifestationLevel,
            FieldId = BooksFields.Flavor,
        },
        new BrowseAxis
        {
            AxisId = "all-books",
            Name = "All books",
            Kind = BrowseAxisKind.Flat,
            LevelId = BooksShape.WorkLevel,
        },
    ];

    private static List<SortOption> BuildSorts() =>
    [
        new SortOption(BooksFields.SortName, "Author", SortDirection.Ascending),
        new SortOption(BooksFields.Title, "Title", SortDirection.Ascending),
        new SortOption(BooksFields.ReleaseDate, "Published", SortDirection.Descending),
        new SortOption(BooksFields.Popularity, "Popularity", SortDirection.Descending),
        new SortOption(BooksFields.CollectionPosition, "Series position", SortDirection.Ascending),
        new SortOption(BooksFields.PageCount, "Length", SortDirection.Descending),
    ];

    private static List<FilterOption> BuildFilters() =>
    [
        new FilterOption(BooksFields.Flavor, "Format", FilterOperators.Equals | FilterOperators.In),
        new FilterOption(BooksFields.Language, "Language", FilterOperators.Equals | FilterOperators.In),
        new FilterOption(BooksFields.State, "State", FilterOperators.Equals | FilterOperators.In),
        new FilterOption(
            BooksFields.CollectionName,
            "Series",
            FilterOperators.Equals | FilterOperators.Contains | FilterOperators.IsNull),
        new FilterOption(
            BooksFields.PageCount,
            "Pages",
            FilterOperators.Between | FilterOperators.GreaterThan | FilterOperators.LessThan),
        new FilterOption(
            BooksFields.ReleaseDate,
            "Published",
            FilterOperators.Between | FilterOperators.GreaterThan | FilterOperators.LessThan),
    ];

    private static List<ActionDescriptor> BuildActions() =>
    [
        new ActionDescriptor
        {
            ActionId = SearchActionId,
            Name = "Search",
            Description = "Look for a copy of this book.",
            Scope = ActionScope.Item,
            TargetLevelId = BooksShape.WorkLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.None,
            LongRunning = true,
            Parameters =
            [
                new ActionParameter(
                    BooksFields.Flavor,
                    "Format",
                    FieldValueKind.Enumerated,
                    false,
                    [
                        new FacetValue(BooksShape.WrittenFamilyId, "eBook"),
                        new FacetValue(BooksShape.SpokenFamilyId, "Audiobook"),
                    ]),
            ],
        },
        new ActionDescriptor
        {
            ActionId = RefreshActionId,
            Name = "Refresh",
            Description = "Re-read this author from the catalog.",
            Scope = ActionScope.Item,
            TargetLevelId = BooksShape.WriterLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.Acknowledge,

            // The destructive half of a materialization facet, said out loud. A refresh applies the
            // selection policy, and works the policy now excludes stop existing.
            ConsequenceStatement =
                "Books the current metadata profile excludes will be removed, unless they were added by "
                + "hand or have files on disk.",
            LongRunning = true,
        },
        new ActionDescriptor
        {
            ActionId = SetMonitorActionId,
            Name = "Set wanted",
            Scope = ActionScope.Selection,
            TargetLevelId = BooksShape.WorkLevel,
            Consequence = Consequence.Safe,
            Confirmation = ConfirmationRequirement.None,
            Parameters =
            [
                new ActionParameter(
                    BooksFields.Wanted,
                    "Wanted",
                    FieldValueKind.Boolean,
                    true,
                    [],
                    "true"),
            ],
        },
        new ActionDescriptor
        {
            ActionId = SelectVariantActionId,
            Name = "Use this edition",
            Description = "Make this edition the one the library considers canonical.",
            Scope = ActionScope.Item,
            TargetLevelId = BooksShape.ManifestationLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.Acknowledge,
            ConsequenceStatement =
                "Searches will use this edition's title and format, and files held against the previous "
                + "edition will no longer count towards this book.",
        },
        new ActionDescriptor
        {
            ActionId = RenameActionId,
            Name = "Rename files",
            Scope = ActionScope.Item,
            TargetLevelId = BooksShape.WorkLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.Acknowledge,
            ConsequenceStatement = "Files on disk will be moved to match the current template.",
            LongRunning = true,
        },
        new ActionDescriptor
        {
            ActionId = RemoveActionId,
            Name = "Remove",
            Scope = ActionScope.Item,
            TargetLevelId = BooksShape.WriterLevel,
            Consequence = Consequence.Destructive,
            Confirmation = ConfirmationRequirement.TypeToConfirm,
            ConsequenceStatement =
                "The author and everything the library knows about them will be removed.",
        },
    ];

    private static List<StateDescriptor> BuildStates() =>
    [
        new StateDescriptor
        {
            StateId = BooksStates.Missing,
            Name = "Missing",
            Tone = StateTone.Attention,
            SourceFieldId = BooksFields.State,
        },
        new StateDescriptor
        {
            // The state only a kind whose items span several files can be in.
            StateId = BooksStates.Partial,
            Name = "Partial",
            Tone = StateTone.InProgress,
            SourceFieldId = BooksFields.State,
            Description = "Some of the parts this edition is spread over are present and others are not.",
        },
        new StateDescriptor
        {
            StateId = BooksStates.Held,
            Name = "In library",
            Tone = StateTone.Positive,
            SourceFieldId = BooksFields.State,
        },
        new StateDescriptor
        {
            StateId = BooksStates.Upgradable,
            Name = "Upgradable",
            Tone = StateTone.Neutral,
            SourceFieldId = BooksFields.State,
        },
        new StateDescriptor
        {
            StateId = BooksStates.Unpublished,
            Name = "Not yet published",
            Tone = StateTone.Neutral,
            SourceFieldId = BooksFields.State,
        },
    ];

    private static List<ExternalSurfaceDescriptor> BuildExternalSurfaces() =>
    [
        new ExternalSurfaceDescriptor(
            "catalog-author",
            "View on Goodreads",
            BooksShape.WriterLevel,
            "https://www.goodreads.com/author/show/{" + BooksFields.CatalogId + "}"),
        new ExternalSurfaceDescriptor(
            "catalog-book",
            "View on Goodreads",
            BooksShape.WorkLevel,
            "https://www.goodreads.com/book/show/{" + BooksFields.CatalogId + "}"),
    ];

    private static List<WorkbenchDescriptor> BuildWorkbenches() =>
    [
        new WorkbenchDescriptor
        {
            WorkbenchId = BooksItemSource.ManualImportWorkbenchId,
            Name = "Manual import",
            Description = "Assign loose files to an edition, and to the parts it is spread over.",
            Subject = WorkbenchSubject.LooseFiles,
            TargetLevelId = BooksShape.ManifestationLevel,
            Inputs =
            [
                new ActionParameter(
                    BooksItemSource.FolderInputId,
                    "Folder",
                    FieldValueKind.FilePath,
                    true,
                    []),
                new ActionParameter(
                    BooksItemSource.FilesInputId,
                    "Files",
                    FieldValueKind.FilePath,
                    false,
                    []),
                new ActionParameter(
                    BooksItemSource.ManifestationInputId,
                    "Edition",
                    FieldValueKind.Reference,
                    false,
                    [],
                    null,
                    "editions"),
            ],
            Columns =
            [
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.PathColumnId,
                        Name = "File",
                        ValueKind = FieldValueKind.FilePath,
                        Prominence = Prominence.Primary,
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.TargetColumnId,
                        Name = "Edition",
                        ValueKind = FieldValueKind.Reference,
                        Prominence = Prominence.Primary,
                        Editable = true,
                    },
                    Editable = true,
                    OptionSourceId = "editions",
                },
                new WorkbenchColumn
                {
                    // The column that exists only because a unit may span files in an order that matters.
                    // A kind whose items are satisfied by exactly one file has nothing to put here.
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.PartColumnId,
                        Name = "Part",
                        ValueKind = FieldValueKind.Integer,
                        Prominence = Prominence.Secondary,
                        Editable = true,
                    },
                    Editable = true,
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.FlavorColumnId,
                        Name = "Format",
                        ValueKind = FieldValueKind.Enumerated,
                        Prominence = Prominence.Secondary,
                        Choices =
                        [
                            new FacetValue(BooksShape.WrittenFamilyId, "eBook"),
                            new FacetValue(BooksShape.SpokenFamilyId, "Audiobook"),
                        ],
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.QualityColumnId,
                        Name = "Quality",
                        ValueKind = FieldValueKind.Enumerated,
                        Prominence = Prominence.Secondary,
                        Editable = true,
                    },
                    Editable = true,
                    OptionSourceId = "tiers-in-family",
                },
            ],
            CommitLabel = "Import",
            CommitConsequence = Consequence.Costly,
            CommitConfirmation = ConfirmationRequirement.Acknowledge,
            AllowsRowExclusion = true,
        },
        new WorkbenchDescriptor
        {
            WorkbenchId = BooksItemSource.InteractiveSearchWorkbenchId,
            Name = "Interactive search",
            Description = "Choose a candidate release by hand.",
            Subject = WorkbenchSubject.ReleaseCandidates,
            TargetLevelId = BooksShape.WorkLevel,
            Inputs =
            [
                new ActionParameter(
                    BooksItemSource.WorkInputId,
                    "Book",
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
                        FieldId = BooksWorkbench.CandidateTitleColumnId,
                        Name = "Release",
                        ValueKind = FieldValueKind.Text,
                        Prominence = Prominence.Primary,
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.TargetColumnId,
                        Name = "Edition",
                        ValueKind = FieldValueKind.Reference,
                        Prominence = Prominence.Secondary,
                        Editable = true,
                    },
                    Editable = true,
                    OptionSourceId = "editions",
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.FlavorColumnId,
                        Name = "Format",
                        ValueKind = FieldValueKind.Enumerated,
                        Prominence = Prominence.Secondary,
                        Choices =
                        [
                            new FacetValue(BooksShape.WrittenFamilyId, "eBook"),
                            new FacetValue(BooksShape.SpokenFamilyId, "Audiobook"),
                        ],
                    },
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = BooksWorkbench.QualityColumnId,
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
