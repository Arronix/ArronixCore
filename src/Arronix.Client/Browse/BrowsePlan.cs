
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Client.Rendering;

namespace Arronix.Client.Browse;

/// <summary>
/// Works out, from a declared traversal, where its items come from and how they are divided up.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the resolver that chooses the component: this decides what to ask the server for and how
/// to arrange the answer, which is a different question from what draws it. Both switch over the same
/// closed vocabulary and neither has a fall-through arm, so a traversal kind added to the contract fails
/// this build in two places, each of which needs a real answer.
/// </para>
/// <para>
/// <b>A stated limit.</b> Dividing a page into sections happens here, over the page the server returned,
/// so a section is only ever complete when the whole level fits in one page. Dividing on the server would
/// need a request shape the published surface does not have. It is recorded rather than hidden, because a
/// library large enough to page is a library where the difference shows.
/// </para>
/// </remarks>
public static class BrowsePlan
{
    /// <summary>
    /// Gets where a traversal's items are read from.
    /// </summary>
    /// <param name="kind">The kind of traversal.</param>
    /// <returns>The source.</returns>
    public static BrowseSource SourceFor(BrowseAxisKind kind) => kind switch
    {
        BrowseAxisKind.Hierarchy => BrowseSource.Level,
        BrowseAxisKind.Sequence => BrowseSource.Level,
        BrowseAxisKind.Grouping => BrowseSource.Groups,
        BrowseAxisKind.Facet => BrowseSource.Level,
        BrowseAxisKind.Flat => BrowseSource.Level,
    };

    /// <summary>
    /// Divides a page of items into the sections a traversal implies.
    /// </summary>
    /// <param name="context">What is being drawn.</param>
    /// <returns>The sections, in the order they should be presented.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<BrowseSection> Divide(PresenterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Axis.Kind switch
        {
            BrowseAxisKind.Hierarchy => [Whole(context)],
            BrowseAxisKind.Sequence => BySequence(context),
            BrowseAxisKind.Grouping => [Whole(context)],
            BrowseAxisKind.Facet => ByField(context),
            BrowseAxisKind.Flat => [Whole(context)],
        };
    }

    private static BrowseSection Whole(PresenterContext context)
        => new(string.Empty, string.Empty, 0, context.Items);

    private static IReadOnlyList<BrowseSection> BySequence(PresenterContext context)
    {
        var axis = FindSequenceAxis(context);

        if (axis is not null)
        {
            return ByOrdinalComponent(context, axis);
        }

        return context.Axis.FieldId is { Length: > 0 }
            ? ByField(context)
            : [Whole(context)];
    }

    private static SequenceAxis? FindSequenceAxis(PresenterContext context)
    {
        if (context.Axis.SequenceAxisId is not { Length: > 0 } axisId)
        {
            return null;
        }

        return context.Kind.Shape.Levels
            .SelectMany(level => level.SequenceAxes)
            .FirstOrDefault(axis => string.Equals(axis.AxisId, axisId, StringComparison.Ordinal));
    }

    private static IReadOnlyList<BrowseSection> ByOrdinalComponent(PresenterContext context, SequenceAxis axis)
    {
        var sections = new Dictionary<long, List<ItemDetail>>();
        var unplaced = new List<ItemDetail>();

        foreach (var item in context.Items)
        {
            if (!item.Item.Coordinates.TryGet(axis.SpaceId, out var reading)
                || reading.Value.Kind is not CoordinateKind.Ordinal
                || reading.Value.Ordinals.Length <= axis.ComponentIndex)
            {
                unplaced.Add(item);
                continue;
            }

            var position = reading.Value.Ordinals[axis.ComponentIndex];

            if (!sections.TryGetValue(position, out var bucket))
            {
                bucket = [];
                sections[position] = bucket;
            }

            bucket.Add(item);
        }

        var ordered = sections
            .OrderBy(section => section.Key)
            .Select(section => new BrowseSection(
                section.Key.ToString(CultureInfo.InvariantCulture),
                LabelFor(axis, section.Key),
                section.Key,
                section.Value))
            .ToList();

        if (unplaced.Count > 0)
        {
            ordered.Add(new BrowseSection("unplaced", "Unplaced", long.MaxValue, unplaced));
        }

        return ordered;
    }

    private static string LabelFor(SequenceAxis axis, long position)
    {
        foreach (var exception in axis.Exceptions)
        {
            if (exception.Value == position)
            {
                return exception.Name;
            }
        }

        return string.Create(CultureInfo.CurrentCulture, $"{axis.Name} {position}");
    }

    private static IReadOnlyList<BrowseSection> ByField(PresenterContext context)
    {
        if (context.Axis.FieldId is not { Length: > 0 } fieldId)
        {
            return [Whole(context)];
        }

        var descriptor = context.Level.Fields
            .FirstOrDefault(field => string.Equals(field.FieldId, fieldId, StringComparison.Ordinal));

        if (descriptor is null)
        {
            return [Whole(context)];
        }

        var sections = new Dictionary<string, List<ItemDetail>>(StringComparer.CurrentCulture);

        foreach (var item in context.Items)
        {
            item.Item.Fields.TryGetValue(fieldId, out var value);
            var label = FieldValueFormatter.Format(descriptor, value);

            if (!sections.TryGetValue(label, out var bucket))
            {
                bucket = [];
                sections[label] = bucket;
            }

            bucket.Add(item);
        }

        return sections
            .OrderBy(section => section.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(section => new BrowseSection(section.Key, section.Key, 0, section.Value))
            .ToList();
    }
}

/// <summary>
/// Where a traversal's items are read from.
/// </summary>
public enum BrowseSource
{
    /// <summary>From a level, or from the contents of an item at one.</summary>
    Level = 0,

    /// <summary>From the collections a grouping axis declares.</summary>
    Groups = 1
}

/// <summary>
/// One division of a page.
/// </summary>
/// <param name="Key">The division's stable identifier.</param>
/// <param name="Label">What the division is called, empty when the page is not divided.</param>
/// <param name="Order">Where the division sorts.</param>
/// <param name="Items">The items in it.</param>
public sealed record BrowseSection(string Key, string Label, long Order, IReadOnlyList<ItemDetail> Items);
