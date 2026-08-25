using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Intent;
using Arronix.Host.Media;


namespace Arronix.Host.Intent;

/// <summary>
/// Checks what an extension declared about working with its media kind against the shape it declared.
/// </summary>
/// <remarks>
/// <para>
/// The intent surface is data an extension writes and the host serializes to whatever front end is asking.
/// The host is therefore the sole publisher of that data, and being the publisher means being responsible
/// for it: an action naming a field that does not exist, or a browse axis naming a level that does not
/// exist, is a fault the host must refuse rather than a rendering problem for every consumer to work
/// around independently.
/// </para>
/// <para>
/// The residual blast radius of a hostile declaration that passes these checks is a misleading label. The
/// declaration <em>becomes</em> pure data before it reaches here — every collection in it is copied into
/// host-owned values at admission, because the members are interfaces an extension supplies the instances
/// of, and a lazy or extension-defined collection would be executable code inside what this text calls
/// data. Given that copy, the one remaining class of attack would need a consumer to render declaration
/// strings unescaped: a single bug in one consumer rather than one per extension.
/// </para>
/// </remarks>
public static partial class IntentSurfaceValidator
{
    /// <summary>
    /// Checks a declared surface.
    /// </summary>
    /// <param name="shape">The validated shape the surface belongs to.</param>
    /// <param name="surface">The declaration.</param>
    /// <returns>Every fault found. Empty exactly when the declaration is sound.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="shape"/> or <paramref name="surface"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<ShapeDefect> Validate(ValidatedShape shape, PluginIntentSurface surface)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(surface);

        var defects = new List<ShapeDefect>();

        if (surface.MediaKind != shape.Kind)
        {
            defects.Add(new ShapeDefect(
                "intent.mediaKind",
                $"The surface declares media kind '{surface.MediaKind}' but belongs to '{shape.Kind}'.",
                CoreErrorCode.PluginShapeInvalid));
        }

        var fieldIds = shape.Levels
            .SelectMany(level => level.Fields)
            .Select(field => field.FieldId)
            .ToHashSet(StringComparer.Ordinal);

        RequireDistinct(defects, "intent.actions", surface.Actions.Select(action => action.ActionId));
        RequireDistinct(defects, "intent.browseAxes", surface.BrowseAxes.Select(axis => axis.AxisId));
        RequireDistinct(defects, "intent.states", surface.States.Select(state => state.StateId));
        RequireDistinct(defects, "intent.workbenches", surface.Workbenches.Select(bench => bench.WorkbenchId));
        RequireDistinct(
            defects,
            "intent.externalSurfaces",
            surface.ExternalSurfaces.Select(external => external.SurfaceId));

        CheckActions(shape, surface, fieldIds, defects);
        CheckBrowseAxes(shape, surface, fieldIds, defects);
        CheckSortsAndFilters(surface, fieldIds, defects);
        CheckStates(surface, fieldIds, defects);
        CheckExternalSurfaces(shape, surface, fieldIds, defects);
        CheckWorkbenches(shape, surface, defects);

        return defects;
    }

    [GeneratedRegex(@"\{([^{}]*)\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex PlaceholderPattern();

    private static void CheckActions(
        ValidatedShape shape,
        PluginIntentSurface surface,
        HashSet<string> fieldIds,
        List<ShapeDefect> defects)
    {
        foreach (var action in surface.Actions)
        {
            if (action.TargetLevelId is { } level && !shape.HasLevel(level))
            {
                defects.Add(new ShapeDefect(
                    $"intent.actions[{action.ActionId}].targetLevelId",
                    $"Level '{level}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            if (action.EnabledWhenFieldId is { } gate && !fieldIds.Contains(gate))
            {
                defects.Add(new ShapeDefect(
                    $"intent.actions[{action.ActionId}].enabledWhenFieldId",
                    $"Field '{gate}' is not declared by any level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
            }
        }
    }

    private static void CheckBrowseAxes(
        ValidatedShape shape,
        PluginIntentSurface surface,
        HashSet<string> fieldIds,
        List<ShapeDefect> defects)
    {
        var groupingIds = shape.Declaration.GroupingAxes
            .Select(axis => axis.AxisId)
            .ToHashSet(StringComparer.Ordinal);

        var sequenceIds = shape.AllSequenceAxes()
            .Select(axis => axis.AxisId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var axis in surface.BrowseAxes)
        {
            var path = $"intent.browseAxes[{axis.AxisId}]";

            if (axis.LevelId is { } level && !shape.HasLevel(level))
            {
                defects.Add(new ShapeDefect(
                    $"{path}.levelId",
                    $"Level '{level}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            switch (axis.Kind)
            {
                case BrowseAxisKind.Grouping:
                    RequireReference(defects, $"{path}.groupingAxisId", axis.GroupingAxisId, groupingIds, "grouping axis");
                    break;

                case BrowseAxisKind.Sequence:
                    // A sequence traversal runs over an ordinal axis or over a date field — the contract
                    // says so on both properties, and a kind whose shape declares no sequence axes at all
                    // can still offer "what is coming next". Exactly one of the two must be named, and it
                    // must resolve; naming neither leaves the traversal undefined, and naming both leaves
                    // two answers to what the order is.
                    CheckSequenceAxis(defects, path, axis, sequenceIds, fieldIds);
                    break;

                case BrowseAxisKind.Facet:
                    RequireReference(defects, $"{path}.fieldId", axis.FieldId, fieldIds, "field");
                    break;

                case BrowseAxisKind.Hierarchy:
                case BrowseAxisKind.Flat:
                default:
                    break;
            }
        }

        if (surface.BrowseAxes.Count(axis => axis.IsDefault) > 1)
        {
            defects.Add(new ShapeDefect(
                "intent.browseAxes",
                "At most one browse axis is the default one.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    private static void CheckSortsAndFilters(
        PluginIntentSurface surface,
        HashSet<string> fieldIds,
        List<ShapeDefect> defects)
    {
        foreach (var sort in surface.Sorts.Where(sort => !fieldIds.Contains(sort.FieldId)))
        {
            defects.Add(new ShapeDefect(
                $"intent.sorts[{sort.FieldId}]",
                $"Field '{sort.FieldId}' is not declared by any level of this shape.",
                CoreErrorCode.PluginShapeInvalid));
        }

        foreach (var filter in surface.Filters.Where(filter => !fieldIds.Contains(filter.FieldId)))
        {
            defects.Add(new ShapeDefect(
                $"intent.filters[{filter.FieldId}]",
                $"Field '{filter.FieldId}' is not declared by any level of this shape.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    private static void CheckStates(
        PluginIntentSurface surface,
        HashSet<string> fieldIds,
        List<ShapeDefect> defects)
    {
        foreach (var state in surface.States.Where(state => !fieldIds.Contains(state.SourceFieldId)))
        {
            defects.Add(new ShapeDefect(
                $"intent.states[{state.StateId}].sourceFieldId",
                $"Field '{state.SourceFieldId}' is not declared by any level of this shape.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A template that cannot be parsed at all is exactly the defect being reported, whatever the parser threw.")]
    private static void CheckExternalSurfaces(
        ValidatedShape shape,
        PluginIntentSurface surface,
        HashSet<string> fieldIds,
        List<ShapeDefect> defects)
    {
        foreach (var external in surface.ExternalSurfaces)
        {
            var path = $"intent.externalSurfaces[{external.SurfaceId}]";

            if (!shape.HasLevel(external.LevelId))
            {
                defects.Add(new ShapeDefect(
                    $"{path}.levelId",
                    $"Level '{external.LevelId}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            // Every placeholder is substituted before the template is used, so the scheme is checked on the
            // template's literal prefix rather than on a parse of the whole thing.
            if (!external.UriTemplate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !external.UriTemplate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                defects.Add(new ShapeDefect(
                    $"{path}.uriTemplate",
                    "An external surface is reached over http or https; no other scheme is published to a consumer.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            try
            {
                foreach (var placeholder in PlaceholderPattern().Matches(external.UriTemplate)
                    .Select(match => match.Groups[1].Value))
                {
                    if (!fieldIds.Contains(placeholder))
                    {
                        defects.Add(new ShapeDefect(
                            $"{path}.uriTemplate",
                            $"Placeholder '{{{placeholder}}}' names no field of this shape.",
                            CoreErrorCode.PluginShapeInvalid));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                defects.Add(new ShapeDefect(
                    $"{path}.uriTemplate",
                    "The template could not be scanned for placeholders within the allowed time.",
                    CoreErrorCode.PluginShapeInvalid));
            }
        }
    }

    private static void CheckWorkbenches(
        ValidatedShape shape,
        PluginIntentSurface surface,
        List<ShapeDefect> defects)
    {
        foreach (var bench in surface.Workbenches)
        {
            var path = $"intent.workbenches[{bench.WorkbenchId}]";

            if (bench.TargetLevelId is { } level && !shape.HasLevel(level))
            {
                defects.Add(new ShapeDefect(
                    $"{path}.targetLevelId",
                    $"Level '{level}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            if (bench.Columns.Count == 0)
            {
                defects.Add(new ShapeDefect(
                    $"{path}.columns",
                    "A working surface declares at least one column; a grid with no columns is not a surface.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            RequireDistinct(defects, $"{path}.columns", bench.Columns.Select(column => column.Field.FieldId));

            // Artwork is semantic row data. A visual grid may choose a thumbnail, a text client may emit an
            // address and another consumer may omit it; none of those presentation decisions can make the
            // value invalid at the media boundary.
        }
    }

    private static void RequireDistinct(List<ShapeDefect> defects, string path, IEnumerable<string> ids)
    {
        foreach (var group in ids.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            defects.Add(new ShapeDefect(
                path,
                $"Identifier '{group.Key}' is declared more than once.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    private static void CheckSequenceAxis(
        List<ShapeDefect> defects,
        string path,
        BrowseAxis axis,
        HashSet<string> sequenceIds,
        HashSet<string> fieldIds)
    {
        var byAxis = !string.IsNullOrWhiteSpace(axis.SequenceAxisId);
        var byField = !string.IsNullOrWhiteSpace(axis.FieldId);

        if (byAxis && byField)
        {
            defects.Add(new ShapeDefect(
                $"{path}.sequenceAxisId",
                "A sequence traversal runs over an ordinal axis or over a date field, not both.",
                CoreErrorCode.PluginShapeInvalid));
            return;
        }

        if (byField)
        {
            RequireReference(defects, $"{path}.fieldId", axis.FieldId, fieldIds, "field");
            return;
        }

        if (!byAxis)
        {
            defects.Add(new ShapeDefect(
                $"{path}.sequenceAxisId",
                "This axis names the sequence axis or the date field it browses by.",
                CoreErrorCode.PluginShapeInvalid));
            return;
        }

        RequireReference(defects, $"{path}.sequenceAxisId", axis.SequenceAxisId, sequenceIds, "sequence axis");
    }

    private static void RequireReference(
        List<ShapeDefect> defects,
        string path,
        string? value,
        HashSet<string> known,
        string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            defects.Add(new ShapeDefect(path, $"This axis names the {what} it browses by.", CoreErrorCode.PluginShapeInvalid));
            return;
        }

        if (!known.Contains(value))
        {
            defects.Add(new ShapeDefect(
                path,
                $"The {what} '{value}' is not declared by this shape.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }
}
