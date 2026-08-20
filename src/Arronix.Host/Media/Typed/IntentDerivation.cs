using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed.Compilation;

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Turns an item type and its typed definition values into the intent surface.
/// </summary>
/// <remarks>
/// <para>
/// Almost all of a kind's traversals, orderings, filters and states are a restatement of what the entity
/// already says. A groupable property is a facet traversal; a sortable one is an ordering whose useful end
/// its type decides; a filterable one is a filter whose operators its type decides; a date is a sequence;
/// a status enumeration is one state per member. Writing those by hand is a table that agrees with the
/// entity on the day it is written and never again.
/// </para>
/// <para>
/// What is genuinely written is small and is exactly the part derivation cannot know: what the
/// unpartitioned traversal should be called, which orderings run the other way, which filterable field is
/// not worth a traversal of its own, and what a state means for the user.
/// </para>
/// </remarks>
internal static class IntentDerivation
{
    /// <summary>
    /// Derives the intent surface of one media kind.
    /// </summary>
    /// <param name="kind">The media kind identifier.</param>
    /// <param name="item">The item type's reading.</param>
    /// <param name="shape">The derived structure.</param>
    /// <param name="declaration">The compiled typed definition values.</param>
    /// <param name="compiledShapes">The build-time-generated item, group, and row projections.</param>
    /// <returns>The intent surface.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static PluginIntentSurface Derive(
        MediaKindId kind,
        ItemTypeReader item,
        MediaShape shape,
        TypedDeclaration declaration,
        CompiledShapeCatalog compiledShapes)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(declaration);

        var levelId = shape.Levels[0].Id;

        return new PluginIntentSurface
        {
            MediaKind = kind,
            BrowseAxes = DeriveAxes(item, shape, declaration, levelId),
            Sorts = DeriveSorts(item, declaration),
            Filters = DeriveFilters(item),
            States = DeriveStates(item, declaration),
            Actions = DeriveActions(declaration, levelId),

            // None, and deliberately. Every surveyed external surface was a catalog's own address grammar
            // spelled inside a media kind; a surface at a catalog belongs to whoever owns the identifier.
            ExternalSurfaces = [],

            Workbenches = [.. declaration.Workbenches.Select(draft => DeriveWorkbench(draft, compiledShapes))]
        };
    }

    private static IReadOnlyList<BrowseAxis> DeriveAxes(
        ItemTypeReader item,
        MediaShape shape,
        TypedDeclaration declaration,
        MediaLevelId levelId)
    {
        var axes = new List<BrowseAxis>
        {
            new()
            {
                AxisId = declaration.DefaultBrowseAxisId,
                Name = declaration.DefaultBrowseName,
                Kind = BrowseAxisKind.Flat,
                LevelId = levelId,
                IsDefault = true
            }
        };

        foreach (var axis in shape.GroupingAxes)
        {
            axes.Add(new BrowseAxis
            {
                AxisId = axis.AxisId,
                Name = axis.PluralName,
                Kind = BrowseAxisKind.Grouping,
                LevelId = levelId,
                GroupingAxisId = axis.AxisId
            });
        }

        foreach (var candidate in item.Fields)
        {
            if (declaration.HiddenAxisFieldIds.Contains(candidate.FieldId))
            {
                continue;
            }

            if (candidate.Carries(FieldSemantics.Groupable))
            {
                axes.Add(new BrowseAxis
                {
                    AxisId = $"by-{candidate.FieldId}",
                    Name = $"By {candidate.Descriptor.Name.ToLowerInvariant()}",
                    Kind = BrowseAxisKind.Facet,
                    LevelId = levelId,
                    FieldId = candidate.FieldId
                });
            }

            if (candidate.Carries(FieldSemantics.Timestamp))
            {
                axes.Add(new BrowseAxis
                {
                    AxisId = $"by-{candidate.FieldId}-sequence",
                    Name = candidate.Descriptor.Name,
                    Kind = BrowseAxisKind.Sequence,
                    LevelId = levelId,
                    FieldId = candidate.FieldId
                });
            }
        }

        return axes;
    }

    private static IReadOnlyList<SortOption> DeriveSorts(ItemTypeReader item, TypedDeclaration declaration) =>
        [.. item.Fields
            .Where(static candidate => candidate.Carries(FieldSemantics.Sortable))
            .Select(candidate => new SortOption(
                candidate.FieldId,
                candidate.Descriptor.Name,
                declaration.SortOverrides.TryGetValue(candidate.FieldId, out var direction)
                    ? direction
                    : DefaultDirection(candidate.Descriptor.ValueKind)))];

    private static IReadOnlyList<FilterOption> DeriveFilters(ItemTypeReader item) =>
        [.. item.Fields
            .Where(static candidate => candidate.Carries(FieldSemantics.Filterable))
            .Select(static candidate => new FilterOption(
                candidate.FieldId,
                candidate.Descriptor.Name,
                candidate.FilterOperators))];

    private static IReadOnlyList<StateDescriptor> DeriveStates(
        ItemTypeReader item,
        TypedDeclaration declaration)
    {
        var status = item.Status;

        if (status is null)
        {
            return [];
        }

        return
        [
            .. status.Descriptor.Choices.Select(choice => new StateDescriptor
            {
                StateId = choice.Value,
                Name = choice.Name,
                Tone = declaration.StateTones.TryGetValue(choice.Value, out var tone) ? tone : StateTone.Neutral,
                SourceFieldId = status.FieldId
            })
        ];
    }

    private static IReadOnlyList<ActionDescriptor> DeriveActions(
        TypedDeclaration declaration,
        MediaLevelId levelId)
    {
        var singular = declaration.Singular ?? "item";
        var plural = declaration.Plural ?? "items";
        var availability = declaration.Selections.Single(selection =>
            string.Equals(selection.FacetId, declaration.AvailabilitySelectionId, StringComparison.Ordinal));

        var actions = new List<ActionDescriptor>
        {
            Standard(StandardMediaAction.Search, "Search", ActionScope.Selection, Consequence.Costly, levelId)
                with { LongRunning = true },
            Standard(
                StandardMediaAction.SearchMissing,
                $"Search for missing {plural.ToLowerInvariant()}",
                ActionScope.Level,
                Consequence.Costly,
                levelId) with
            {
                LongRunning = true,
                Confirmation = ConfirmationRequirement.Acknowledge,
                ConsequenceStatement = $"Every wanted {singular.ToLowerInvariant()} without a satisfactory file is searched for.",
                Parameters =
                    [BooleanParameter(StandardMediaActionParameter.IncludeUnavailable, "Include unavailable items")]
            },
            Standard(
                StandardMediaAction.SearchCutoffUnmet,
                "Search for upgrades",
                ActionScope.Level,
                Consequence.Costly,
                levelId) with
            {
                LongRunning = true,
                Confirmation = ConfirmationRequirement.Acknowledge,
                ConsequenceStatement = $"Every {singular.ToLowerInvariant()} below its release cutoff is searched for."
            },
            Standard(StandardMediaAction.Refresh, "Refresh", ActionScope.Selection, Consequence.Costly, levelId)
                with { LongRunning = true },
            Standard(StandardMediaAction.Rescan, "Rescan folders", ActionScope.Selection, Consequence.Safe, levelId)
                with { LongRunning = true },
            Standard(StandardMediaAction.SetMonitoring, "Set wanted", ActionScope.Selection, Consequence.Safe, levelId)
                with
                {
                    Parameters =
                    [
                        BooleanParameter(
                            StandardMediaActionParameter.Wanted,
                            "Wanted",
                            defaultValue: true,
                            required: true)
                    ]
                },
            Standard(
                StandardMediaAction.SetAvailability,
                "Set minimum availability",
                ActionScope.Selection,
                Consequence.Safe,
                levelId) with { Parameters = [SelectionParameter(availability)] },
            Standard(StandardMediaAction.Rename, "Rename files", ActionScope.Selection, Consequence.Destructive, levelId)
                with
                {
                    LongRunning = true,
                    Confirmation = ConfirmationRequirement.Acknowledge,
                    ConsequenceStatement = "Files on disk will be moved to paths produced by the active naming policy."
                },
            Standard(
                StandardMediaAction.Add,
                $"Add a {singular.ToLowerInvariant()}",
                ActionScope.Kind,
                Consequence.Safe,
                levelId) with
            {
                Parameters =
                [
                    new ActionParameter(
                        WireParameterId(StandardMediaActionParameter.Identifier),
                        "Catalog identifier",
                        FieldValueKind.ExternalIdentifier,
                        true,
                        [],
                        null,
                        $"identity.{DerivedNames.Identifier(IdentifierRole.PrimaryWork.ToString())}")
                    {
                        StandardParameter = StandardMediaActionParameter.Identifier
                    },
                    new ActionParameter(
                        WireParameterId(StandardMediaActionParameter.Monitoring),
                        "What to take on",
                        FieldValueKind.Enumerated,
                        false,
                        [
                            .. EnumOrder.Names(typeof(MonitoringScope)).Select(member => new FacetValue(
                                DerivedNames.Identifier(member),
                                DerivedNames.Label(member)))
                        ],
                        DerivedNames.Identifier(MonitoringScope.Item.ToString()))
                    {
                        StandardParameter = StandardMediaActionParameter.Monitoring
                    },
                    SelectionParameter(availability),
                    BooleanParameter(
                        StandardMediaActionParameter.SearchImmediately,
                        "Search immediately",
                        defaultValue: true)
                ]
            },
            Standard(StandardMediaAction.Remove, "Remove from library", ActionScope.Selection, Consequence.Irreversible, levelId)
                with
                {
                    Confirmation = ConfirmationRequirement.TypeToConfirm,
                    ConsequenceStatement = "The library state is removed. Files are removed only when explicitly requested.",
                    Parameters =
                    [
                        BooleanParameter(StandardMediaActionParameter.DeleteFiles, "Also delete files"),
                        BooleanParameter(StandardMediaActionParameter.Exclude, "Also exclude from curation")
                    ]
                },
            Standard(
                StandardMediaAction.Exclude,
                $"Exclude this {singular.ToLowerInvariant()}",
                ActionScope.Selection,
                Consequence.Safe,
                levelId) with
            {
                Confirmation = ConfirmationRequirement.Acknowledge,
                ConsequenceStatement = $"The {singular.ToLowerInvariant()} will be skipped by curation until the exclusion is cleared."
            },
            Standard(
                StandardMediaAction.ClearExclusion,
                $"Clear {singular.ToLowerInvariant()} exclusion",
                ActionScope.Selection,
                Consequence.Safe,
                levelId)
        };

        foreach (var group in declaration.Groups)
        {
            if (group.IsMonitorable)
            {
                actions.Add(new ActionDescriptor
                {
                    StandardAction = StandardMediaAction.SetGroupMonitoring,
                    ActionId = StandardActionIds.GroupMonitoring(group.AxisId),
                    Name = $"Set wanted for {group.Plural?.ToLowerInvariant() ?? "groups"}",
                    Scope = ActionScope.Group,
                    TargetGroupAxisId = group.AxisId,
                    Consequence = Consequence.Costly,
                    Confirmation = ConfirmationRequirement.Acknowledge,
                    ConsequenceStatement = $"Every member of the selected {group.Singular?.ToLowerInvariant() ?? "group"} is updated.",
                    LongRunning = true,
                    Parameters =
                    [
                        BooleanParameter(StandardMediaActionParameter.Wanted, "Wanted", true, true),
                        BooleanParameter(StandardMediaActionParameter.AddMissing, "Also add missing members")
                    ]
                });
            }

            if (group.IsDiscoverySource)
            {
                actions.Add(new ActionDescriptor
                {
                    StandardAction = StandardMediaAction.RefreshGroups,
                    ActionId = StandardActionIds.GroupRefresh(group.AxisId),
                    Name = $"Refresh {group.Plural?.ToLowerInvariant() ?? "groups"}",
                    Scope = ActionScope.Group,
                    TargetGroupAxisId = group.AxisId,
                    Consequence = Consequence.Costly,
                    Confirmation = ConfirmationRequirement.None,
                    LongRunning = true
                });
            }
        }

        return actions;
    }

    private static ActionDescriptor Standard(
        StandardMediaAction action,
        string name,
        ActionScope scope,
        Consequence consequence,
        MediaLevelId levelId) => new()
        {
            StandardAction = action,
            ActionId = StandardActionIds.For(action),
            Name = name,
            Scope = scope,
            TargetLevelId = scope == ActionScope.Group ? null : levelId,
            Consequence = consequence,
            Confirmation = ConfirmationRequirement.None
        };

    private static ActionParameter BooleanParameter(
        StandardMediaActionParameter parameter,
        string name,
        bool defaultValue = false,
        bool required = false) =>
        new(
            WireParameterId(parameter),
            name,
            FieldValueKind.Boolean,
            required,
            [],
            defaultValue ? "true" : "false")
        {
            StandardParameter = parameter
        };

    private static ActionParameter SelectionParameter(SelectionDraft facet) =>
        new(
            facet.FacetId,
            facet.Name,
            facet.Kind == SelectionFacetKind.Enumerated ? FieldValueKind.Enumerated : FieldValueKind.Decimal,
            false,
            facet.Values,
            facet.DefaultAllowed.Count > 0
                ? facet.DefaultAllowed[0]
                : facet.DefaultNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"selection.{facet.FacetId}")
        {
            StandardParameter = StandardMediaActionParameter.Availability
        };

    private static string WireParameterId(StandardMediaActionParameter parameter) => parameter switch
    {
        StandardMediaActionParameter.IncludeUnavailable => "includeUnavailable",
        StandardMediaActionParameter.Wanted => "wanted",
        StandardMediaActionParameter.AddMissing => "addMissing",
        StandardMediaActionParameter.Identifier => "identifier",
        StandardMediaActionParameter.Monitoring => "monitoring",
        StandardMediaActionParameter.Availability => "availability",
        StandardMediaActionParameter.SearchImmediately => "searchImmediately",
        StandardMediaActionParameter.DeleteFiles => "deleteFiles",
        StandardMediaActionParameter.Exclude => "exclude",
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null)
    };

    private static WorkbenchDescriptor DeriveWorkbench(WorkbenchDraft draft, CompiledShapeCatalog compiledShapes)
    {
        var row = ItemTypeReader.ReadRow(compiledShapes.Get(draft.RowType));

        return new WorkbenchDescriptor
        {
            WorkbenchId = draft.WorkbenchId,
            Name = draft.Name,
            Subject = draft.Subject,

            // Columns are the generic presentation projection of the typed decision row.
            Columns =
            [
                .. row.Select(static candidate => new WorkbenchColumn
                {
                    Field = candidate.Descriptor,
                    Editable = candidate.Descriptor.Editable
                })
            ],
            Inputs = draft.Inputs,
            CommitLabel = draft.CommitLabel,
            CommitConsequence = draft.CommitConsequence,
            CommitConfirmation = draft.CommitConsequence >= Consequence.Destructive
                ? ConfirmationRequirement.Acknowledge
                : ConfirmationRequirement.None
        };
    }

    private static SortDirection DefaultDirection(FieldValueKind kind) =>
        kind switch
        {
            FieldValueKind.Text or FieldValueKind.MultilineText or FieldValueKind.Enumerated
                or FieldValueKind.FilePath or FieldValueKind.Language => SortDirection.Ascending,
            _ => SortDirection.Descending
        };
}
