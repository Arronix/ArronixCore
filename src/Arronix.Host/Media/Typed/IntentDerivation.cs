using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed.Builders;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Turns an item type and its configuration into the intent surface.
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
    /// <param name="declaration">Everything the configuration call recorded.</param>
    /// <returns>The intent surface.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static PluginIntentSurface Derive(
        MediaKindId kind,
        ItemTypeReader item,
        MediaShape shape,
        TypedDeclaration declaration)
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
            Actions = [.. declaration.Actions.Select(draft => DeriveAction(draft, levelId))],

            // None, and deliberately. Every surveyed external surface was a catalog's own address grammar
            // spelled inside a media kind; a surface at a catalog belongs to whoever owns the identifier.
            ExternalSurfaces = [],

            Workbenches = [.. declaration.Workbenches.Select(DeriveWorkbench)]
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

    private static ActionDescriptor DeriveAction(ActionDraft draft, MediaLevelId levelId) =>
        new()
        {
            ActionId = draft.ActionId,
            Name = draft.Name,
            Scope = draft.Scope,
            TargetLevelId = draft.Scope == ActionScope.Group ? null : levelId,
            TargetGroupAxisId = draft.GroupAxisId,
            Consequence = draft.Consequence,
            Confirmation = draft.Confirmation,
            ConsequenceStatement = draft.ConsequenceStatement,
            LongRunning = draft.LongRunning,
            Parameters = draft.Parameters,
            EnabledWhenFieldId = draft.EnabledWhenFieldId
        };

    private static WorkbenchDescriptor DeriveWorkbench(WorkbenchDraft draft)
    {
        var row = ItemTypeReader.ReadRow(draft.RowType);

        return new WorkbenchDescriptor
        {
            WorkbenchId = draft.WorkbenchId,
            Name = draft.Name,
            Subject = draft.Subject,

            // The row type is the column set, so a column and the proposal that fills it cannot disagree.
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
