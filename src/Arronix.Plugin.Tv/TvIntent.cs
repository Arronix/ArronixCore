
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Tv;

/// <summary>
/// Workbench and column identifiers, shared by the declaration and the proposal that fills it.
/// </summary>
public static class TvWorkbenches
{
    /// <summary>Assigning loose files on disk to catalog units.</summary>
    public const string ManualImport = "manual-import";

    /// <summary>Choosing among release candidates by hand.</summary>
    public const string InteractiveSearch = "interactive-search";

    /// <summary>Input: the candidate paths, one per line.</summary>
    public const string FilesInput = "files";

    /// <summary>Input: the release candidates, one per line.</summary>
    public const string ReleasesInput = "releases";

    /// <summary>Input: the library entry the proposal is scoped to, as a catalog identifier.</summary>
    public const string ScopeInput = "scope";

    /// <summary>Column: the path being assigned.</summary>
    public const string PathColumn = "path";

    /// <summary>Column: the position read out of the path, rendered.</summary>
    public const string PositionColumn = "position";

    /// <summary>Column: which coordinate space the path addressed.</summary>
    public const string SchemeColumn = "scheme";

    /// <summary>
    /// Column: the units the path satisfies. Multivalued, because one file may satisfy several.
    /// </summary>
    public const string UnitsColumn = "units";

    /// <summary>Column: the quality read out of the path.</summary>
    public const string QualityColumn = "quality";

    /// <summary>Column: the release title being considered.</summary>
    public const string ReleaseColumn = "release";

    /// <summary>Column: whether to grab the release. Editable.</summary>
    public const string GrabColumn = "grab";

    /// <summary>Option source resolving to the units a row may legally be assigned to. Row-scoped.</summary>
    public const string UnitsOptionSource = "units-in-entry";
}

/// <summary>
/// Action identifiers this extension declares.
/// </summary>
public static class TvActions
{
    /// <summary>Search every eligible indexer for the selected units.</summary>
    public const string Search = "search";

    /// <summary>Search for a whole run of the sequence axis in one release.</summary>
    public const string SearchSequenceSpan = "search.sequence-span";

    /// <summary>Search for everything under the library entry.</summary>
    public const string SearchEntry = "search.entry";

    /// <summary>Re-read the catalog record.</summary>
    public const string Refresh = "refresh";

    /// <summary>Set the wanted flag on the selected units.</summary>
    public const string SetMonitor = "monitor.set";

    /// <summary>Apply a bulk monitoring policy to a library entry.</summary>
    public const string ApplyMonitorPolicy = "monitor.apply-policy";

    /// <summary>Set the monitored bit on the sequence axis's policy record.</summary>
    public const string SetSequenceMonitor = "monitor.sequence";

    /// <summary>Apply the naming policy to the files already on disk.</summary>
    public const string Rename = "rename";

    /// <summary>Remove the library entry.</summary>
    public const string Remove = "remove";
}

/// <summary>
/// Declares how episodic media may be browsed, sorted, filtered, acted on and described.
/// </summary>
/// <remarks>
/// <para>Three things here are worth reading twice. A <b>calendar</b> is a sequence browse over a date
/// field, not a bespoke view — the vocabulary has no calendar concept and does not need one. A <b>season
/// browse</b> is a sequence browse over the declared sequence axis, which is how a construct that is neither
/// a level nor a plain coordinate becomes navigable. And the <b>apply-once monitoring policies</b> (all /
/// future / missing / first / none) are actions with an enumerated parameter, not monitoring dimensions:
/// they are things you do once, not states an item is in.</para>
/// <para>Everything below is data. A command line renders it as subcommands and a table; a voice assistant
/// reads the consequence statements aloud before acting. Nothing names a control.</para>
/// </remarks>
public static class TvIntent
{
    /// <summary>Builds the declared intent surface.</summary>
    /// <returns>The surface, ready to register.</returns>
    public static PluginIntentSurface Create() => new()
    {
        MediaKind = TvIds.MediaKind,
        BrowseAxes = BuildBrowseAxes(),
        Sorts = BuildSorts(),
        Filters = BuildFilters(),
        Actions = BuildActions(),
        States = BuildStates(),
        ExternalSurfaces = BuildExternalSurfaces(),
        Workbenches = BuildWorkbenches()
    };

    private static IReadOnlyList<BrowseAxis> BuildBrowseAxes() =>
    [
        new BrowseAxis
        {
            AxisId = "entries",
            Name = "All series",
            Kind = BrowseAxisKind.Flat,
            LevelId = TvIds.SeriesLevel,
            IsDefault = true
        },
        new BrowseAxis
        {
            AxisId = "descend",
            Name = "Units under an entry",
            Kind = BrowseAxisKind.Hierarchy,
            LevelId = TvIds.EpisodeLevel
        },
        new BrowseAxis
        {
            AxisId = "by-sequence",
            Name = "By season",
            Kind = BrowseAxisKind.Sequence,
            LevelId = TvIds.EpisodeLevel,

            // The sequence axis, made navigable. It is not a level, so it cannot be descended into; it is
            // not a plain field, so it cannot be facetted on. It is its own browse kind.
            SequenceAxisId = TvIds.SeasonAxisId
        },
        new BrowseAxis
        {
            AxisId = "calendar",
            Name = "Calendar",
            Kind = BrowseAxisKind.Sequence,
            LevelId = TvIds.EpisodeLevel,
            FieldId = TvEpisodeFields.AirDate
        },
        new BrowseAxis
        {
            AxisId = "by-network",
            Name = "By network",
            Kind = BrowseAxisKind.Facet,
            LevelId = TvIds.SeriesLevel,
            FieldId = TvSeriesFields.Network
        },
        new BrowseAxis
        {
            AxisId = "by-status",
            Name = "By status",
            Kind = BrowseAxisKind.Facet,
            LevelId = TvIds.SeriesLevel,
            FieldId = TvSeriesFields.Status
        },
        new BrowseAxis
        {
            AxisId = "by-genre",
            Name = "By genre",
            Kind = BrowseAxisKind.Facet,
            LevelId = TvIds.SeriesLevel,
            FieldId = TvSeriesFields.Genres
        }
    ];

    private static IReadOnlyList<SortOption> BuildSorts() =>
    [
        new SortOption(TvSeriesFields.SortTitle, "Title", SortDirection.Ascending),
        new SortOption(TvSeriesFields.Year, "Year", SortDirection.Descending),
        new SortOption(TvSeriesFields.FirstAired, "First aired", SortDirection.Descending),
        new SortOption(TvSeriesFields.Network, "Network", SortDirection.Ascending),
        new SortOption(TvEpisodeFields.Position, "Position", SortDirection.Ascending),
        new SortOption(TvEpisodeFields.AirDate, "Aired", SortDirection.Descending)
    ];

    private static IReadOnlyList<FilterOption> BuildFilters() =>
    [
        new FilterOption(TvSeriesFields.Title, "Title", FilterOperators.Contains | FilterOperators.Equals),
        new FilterOption(
            TvSeriesFields.Status,
            "Status",
            FilterOperators.Equals | FilterOperators.NotEquals | FilterOperators.In),
        new FilterOption(
            TvSeriesFields.AddressingScheme,
            "Numbering",
            FilterOperators.Equals | FilterOperators.In),
        new FilterOption(TvSeriesFields.Network, "Network", FilterOperators.Equals | FilterOperators.In),
        new FilterOption(TvSeriesFields.Genres, "Genre", FilterOperators.In | FilterOperators.Contains),
        new FilterOption(
            TvSeriesFields.Year,
            "Year",
            FilterOperators.Equals | FilterOperators.GreaterThan | FilterOperators.LessThan
            | FilterOperators.Between),
        new FilterOption(
            TvEpisodeFields.AirDate,
            "Aired",
            FilterOperators.GreaterThan | FilterOperators.LessThan | FilterOperators.Between
            | FilterOperators.IsNull),
        new FilterOption(
            TvEpisodeFields.SequenceKind,
            "Sequence placement",
            FilterOperators.Equals | FilterOperators.In)
    ];

    private static IReadOnlyList<ActionDescriptor> BuildActions() =>
    [
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Search,
            ActionId = TvActions.Search,
            Name = "Search",
            Description = "Ask every eligible indexer for the selected units.",
            Scope = ActionScope.Selection,
            TargetLevelId = TvIds.EpisodeLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.None,
            LongRunning = true
        },
        new ActionDescriptor
        {
            ActionId = TvActions.SearchSequenceSpan,
            Name = "Search for a season pack",
            Description = "Ask for a whole run of the sequence axis in one release.",
            Scope = ActionScope.Selection,
            TargetLevelId = TvIds.EpisodeLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.None,
            LongRunning = true
        },
        new ActionDescriptor
        {
            ActionId = TvActions.SearchEntry,
            Name = "Search for everything",
            Scope = ActionScope.Item,
            TargetLevelId = TvIds.SeriesLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.Acknowledge,
            ConsequenceStatement = "Every eligible indexer will be asked for every missing unit under this "
                + "entry. On a long-running entry that is a great many requests.",
            LongRunning = true
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Refresh,
            ActionId = TvActions.Refresh,
            Name = "Refresh",
            Scope = ActionScope.Selection,
            TargetLevelId = TvIds.SeriesLevel,
            Consequence = Consequence.Costly,
            Confirmation = ConfirmationRequirement.None,
            LongRunning = true
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.SetMonitoring,
            ActionId = TvActions.SetMonitor,
            Name = "Set wanted",
            Scope = ActionScope.Selection,
            TargetLevelId = TvIds.EpisodeLevel,
            Consequence = Consequence.Safe,
            Confirmation = ConfirmationRequirement.None,
            Parameters =
            [
                new ActionParameter("wanted", "Wanted", FieldValueKind.Boolean, Required: true, Choices: [])
                {
                    StandardParameter = StandardMediaActionParameter.Wanted
                }
            ]
        },
        new ActionDescriptor
        {
            ActionId = TvActions.SetSequenceMonitor,
            Name = "Set wanted for a whole season",
            Description = "Writes the monitored bit on the sequence axis's policy record.",
            Scope = ActionScope.Item,
            TargetLevelId = TvIds.SeriesLevel,
            Consequence = Consequence.Safe,
            Confirmation = ConfirmationRequirement.None,
            Parameters =
            [
                new ActionParameter(
                    TvIds.SeasonComponentId,
                    "Season",
                    FieldValueKind.Integer,
                    Required: true,
                    Choices: []),
                new ActionParameter("wanted", "Wanted", FieldValueKind.Boolean, Required: true, Choices: [])
            ]
        },
        new ActionDescriptor
        {
            ActionId = TvActions.ApplyMonitorPolicy,
            Name = "Apply a monitoring policy",
            Description = "A one-off bulk edit of the wanted flags under this entry.",

            // Apply-once policies are actions with an enumerated parameter, not monitoring dimensions. The
            // distinction matters: a dimension is a state an item is in, a policy is a thing you do once and
            // whose effect is then indistinguishable from having done it by hand.
            Scope = ActionScope.Item,
            TargetLevelId = TvIds.SeriesLevel,
            Consequence = Consequence.Destructive,
            Confirmation = ConfirmationRequirement.Acknowledge,
            ConsequenceStatement = "Existing wanted flags under this entry are overwritten.",
            Parameters =
            [
                new ActionParameter(
                    "policy",
                    "Policy",
                    FieldValueKind.Enumerated,
                    Required: true,
                    Choices:
                    [
                        new FacetValue("all", "Everything"),
                        new FacetValue("future", "Only what has not aired yet"),
                        new FacetValue("missing", "Only what is missing"),
                        new FacetValue("first", "Only the first run"),
                        new FacetValue("latest", "Only the latest run"),
                        new FacetValue("none", "Nothing")
                    ],
                    DefaultValue: "all")
            ]
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Rename,
            ActionId = TvActions.Rename,
            Name = "Rename files",
            Scope = ActionScope.Selection,
            TargetLevelId = TvIds.SeriesLevel,
            Consequence = Consequence.Destructive,
            Confirmation = ConfirmationRequirement.Acknowledge,
            ConsequenceStatement = "Files on disk will be moved to new paths.",
            LongRunning = true
        },
        new ActionDescriptor
        {
            StandardAction = StandardMediaAction.Remove,
            ActionId = TvActions.Remove,
            Name = "Remove from library",
            Scope = ActionScope.Selection,
            TargetLevelId = TvIds.SeriesLevel,
            Consequence = Consequence.Irreversible,
            Confirmation = ConfirmationRequirement.TypeToConfirm,
            ConsequenceStatement = "The entry, every unit under it and their library state are removed. "
                + "Files on disk are removed only if you ask for that as well.",
            Parameters =
            [
                new ActionParameter(
                    "deleteFiles",
                    "Also delete files from disk",
                    FieldValueKind.Boolean,
                    Required: false,
                    Choices: [],
                    DefaultValue: "false")
            ]
        }
    ];

    private static IReadOnlyList<StateDescriptor> BuildStates() =>
    [
        new StateDescriptor
        {
            StateId = TvStatuses.Continuing,
            Name = "Continuing",
            Description = "More output is expected.",
            Tone = StateTone.Positive,
            SourceFieldId = TvSeriesFields.Status
        },
        new StateDescriptor
        {
            StateId = TvStatuses.Ended,
            Name = "Ended",
            Description = "Nothing further will be produced.",
            Tone = StateTone.Neutral,
            SourceFieldId = TvSeriesFields.Status
        },
        new StateDescriptor
        {
            StateId = TvStatuses.Upcoming,
            Name = "Upcoming",
            Description = "Announced but not yet transmitted.",
            Tone = StateTone.Attention,
            SourceFieldId = TvSeriesFields.Status
        },
        new StateDescriptor
        {
            StateId = TvSequenceKinds.OutOfRun,
            Name = "Outside the numbered run",
            Description = "A declared sequence exception. Absence is not a gap.",
            Tone = StateTone.Neutral,
            SourceFieldId = TvEpisodeFields.SequenceKind
        }
    ];

    private static IReadOnlyList<ExternalSurfaceDescriptor> BuildExternalSurfaces() =>
    [
        new ExternalSurfaceDescriptor(
            "tvdb",
            "TheTVDB",
            TvIds.SeriesLevel,
            "https://thetvdb.com/dereferrer/series/{tvdbId}"),
        new ExternalSurfaceDescriptor(
            "imdb",
            "IMDb",
            TvIds.SeriesLevel,
            "https://www.imdb.com/title/{imdbId}/")
    ];

    private static IReadOnlyList<WorkbenchDescriptor> BuildWorkbenches() =>
    [
        new WorkbenchDescriptor
        {
            WorkbenchId = TvWorkbenches.ManualImport,
            Name = "Manual import",
            Description = "Assign files that could not be matched automatically. One row may name several "
                + "units, because one file may satisfy several.",
            Subject = WorkbenchSubject.LooseFiles,
            TargetLevelId = TvIds.EpisodeLevel,
            Inputs =
            [
                new ActionParameter(
                    TvWorkbenches.FilesInput,
                    "Files",
                    FieldValueKind.FilePath,
                    Required: true,
                    Choices: []),
                new ActionParameter(
                    TvWorkbenches.ScopeInput,
                    "Library entry",
                    FieldValueKind.Reference,
                    Required: false,
                    Choices: [])
            ],
            Columns =
            [
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.PathColumn,
                        Name = "File",
                        ValueKind = FieldValueKind.FilePath,
                        Prominence = Prominence.Primary
                    }
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.SchemeColumn,
                        Name = "Numbering",
                        ValueKind = FieldValueKind.Enumerated,
                        Prominence = Prominence.Detail,
                        Choices =
                        [
                            new FacetValue(TvAddressingSchemes.Ordinal, "Season and episode"),
                            new FacetValue(TvAddressingSchemes.Calendar, "Transmission date"),
                            new FacetValue(TvAddressingSchemes.Flat, "Flat numbering")
                        ]
                    }
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.PositionColumn,
                        Name = "Read as",
                        ValueKind = FieldValueKind.Text,
                        Prominence = Prominence.Secondary
                    }
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.UnitsColumn,
                        Name = "Units",
                        ValueKind = FieldValueKind.Reference,

                        // Multivalued, and that is the whole point: one file may satisfy several units, so a
                        // single-reference column could not express the case this media kind exists to prove.
                        Multivalued = true,
                        Prominence = Prominence.Primary,
                        Editable = true
                    },
                    Editable = true,

                    // Row-scoped: the legal units for one file are not the legal units for another, because
                    // each row may have resolved to a different library entry.
                    OptionSourceId = TvWorkbenches.UnitsOptionSource
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.QualityColumn,
                        Name = "Quality",
                        ValueKind = FieldValueKind.Quality,
                        Prominence = Prominence.Secondary
                    }
                }
            ],
            CommitLabel = "Import",
            CommitConsequence = Consequence.Destructive,
            CommitConfirmation = ConfirmationRequirement.Acknowledge,
            AllowsRowExclusion = true
        },
        new WorkbenchDescriptor
        {
            WorkbenchId = TvWorkbenches.InteractiveSearch,
            Name = "Interactive search",
            Description = "Choose a release by hand instead of letting the decision engine choose.",
            Subject = WorkbenchSubject.ReleaseCandidates,
            TargetLevelId = TvIds.EpisodeLevel,
            Inputs =
            [
                new ActionParameter(
                    TvWorkbenches.ReleasesInput,
                    "Candidates",
                    FieldValueKind.Text,
                    Required: true,
                    Choices: []),
                new ActionParameter(
                    TvWorkbenches.ScopeInput,
                    "Library entry",
                    FieldValueKind.Reference,
                    Required: false,
                    Choices: [])
            ],
            Columns =
            [
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.ReleaseColumn,
                        Name = "Release",
                        ValueKind = FieldValueKind.Text,
                        Prominence = Prominence.Primary
                    }
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.PositionColumn,
                        Name = "Satisfies",
                        ValueKind = FieldValueKind.Text,
                        Prominence = Prominence.Secondary
                    }
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.QualityColumn,
                        Name = "Quality",
                        ValueKind = FieldValueKind.Quality,
                        Prominence = Prominence.Secondary
                    }
                },
                new WorkbenchColumn
                {
                    Field = new FieldDescriptor
                    {
                        FieldId = TvWorkbenches.GrabColumn,
                        Name = "Grab",
                        ValueKind = FieldValueKind.Boolean,
                        Prominence = Prominence.Primary,
                        Editable = true
                    },
                    Editable = true
                }
            ],
            CommitLabel = "Grab",
            CommitConsequence = Consequence.Costly,
            CommitConfirmation = ConfirmationRequirement.None,
            AllowsRowExclusion = true
        }
    ];
}
