using System.Collections;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0005
#pragma warning disable ARX0013
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Reads values off a typed entity into the descriptor-shaped view a consumer that cannot load the
/// plugin assembly gets.
/// </summary>
/// <remarks>
/// This is what keeps the typed model honest for everybody who is not a .NET client. A command-line client
/// or a text interface cannot name the entity type; it reads the derived structure and asks for values by
/// field identifier, and gets exactly what the descriptor said it would.
/// </remarks>
internal sealed class ItemProjector
{
    private readonly Dictionary<string, DerivedField> _byFieldId;
    private readonly IReadOnlyDictionary<Type, string> _groupAxisIds;

    internal ItemProjector(
        MediaKindId kind,
        MediaLevelId levelId,
        ItemTypeReader reading,
        IReadOnlyDictionary<Type, string> groupAxisIds)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(groupAxisIds);

        Kind = kind;
        LevelId = levelId;
        Reading = reading;
        _groupAxisIds = groupAxisIds;
        _byFieldId = reading.Fields.ToDictionary(candidate => candidate.FieldId, StringComparer.Ordinal);
    }

    private MediaKindId Kind { get; }

    private MediaLevelId LevelId { get; }

    private ItemTypeReader Reading { get; }

    /// <summary>
    /// Projects one entity.
    /// </summary>
    /// <param name="item">The entity.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="item"/> is of the wrong type.</exception>
    internal ItemView Project(object item)
    {
        Require(item);

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);

        foreach (var candidate in Reading.Fields)
        {
            fields[candidate.FieldId] = ValueOf(candidate, item);
        }

        var externalIds = Reading.ExternalIds?.Property.GetValue(item) is ExternalIdSet set
            ? set.Values
            : [];

        var id = (MediaItemId)(Reading.Key.Property.GetValue(item) ?? default(MediaItemId));

        return new ItemView
        {
            Ref = new MediaItemRef(Kind, LevelId, id),
            Title = Reading.Title.Property.GetValue(item)?.ToString() ?? string.Empty,
            Fields = fields,
            ExternalIds = externalIds,
            SortIndex = id.Value
        };
    }

    /// <summary>
    /// Reads one field off one entity.
    /// </summary>
    /// <param name="item">The entity.</param>
    /// <param name="fieldId">The field identifier.</param>
    /// <returns>The value, or an absent value when the entity carries none.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entity is of the wrong type, or the field is unknown.</exception>
    internal FieldValue Read(object item, string fieldId)
    {
        Require(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldId);

        return _byFieldId.TryGetValue(fieldId, out var candidate)
            ? ValueOf(candidate, item)
            : throw new ArgumentException(
                $"'{fieldId}' names no field of '{Reading.EntityType.Name}'.",
                nameof(fieldId));
    }

    private void Require(object item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!Reading.EntityType.IsInstanceOfType(item))
        {
            throw new ArgumentException(
                $"The item is a '{item.GetType().Name}' where a '{Reading.EntityType.Name}' was expected.",
                nameof(item));
        }
    }

    private FieldValue ValueOf(DerivedField field, object item)
    {
        var raw = field.Property.GetValue(item);
        var kind = field.Descriptor.ValueKind;

        if (raw is null)
        {
            return FieldValue.Absent(kind);
        }

        if (raw is ExternalIdSet identifiers)
        {
            return FieldValue.OfItems(
                kind,
                [.. identifiers.Values.Select(FieldValue.OfExternalIdentifier)]);
        }

        if (raw is ArtworkSet artwork)
        {
            return FieldValue.OfItems(
                kind,
                [.. artwork.Images.Select(static image => FieldValue.OfArtwork(image.Address))]);
        }

        if (field.Descriptor.Multivalued && raw is IEnumerable sequence and not string)
        {
            var elements = new List<FieldValue>();

            foreach (var element in sequence)
            {
                elements.Add(element is null ? FieldValue.Absent(kind) : Scalar(field, kind, element));
            }

            return FieldValue.OfItems(kind, elements);
        }

        return Scalar(field, kind, raw);
    }

    private FieldValue Scalar(DerivedField field, FieldValueKind kind, object raw) =>
        kind == FieldValueKind.Reference ? Reference(raw) : ScalarOfKind(field, kind, raw);

    private static FieldValue ScalarOfKind(DerivedField field, FieldValueKind kind, object raw) =>
        kind switch
        {
            FieldValueKind.Text => FieldValue.OfText(raw.ToString() ?? string.Empty),
            FieldValueKind.MultilineText => FieldValue.OfMultilineText(raw.ToString() ?? string.Empty),
            FieldValueKind.Integer => FieldValue.OfInteger(AsInt64(raw)),
            FieldValueKind.ByteSize => FieldValue.OfByteSize(AsInt64(raw)),
            FieldValueKind.Count => FieldValue.OfCount(AsInt64(raw)),
            FieldValueKind.Decimal => FieldValue.OfDecimal(Convert.ToDouble(raw, CultureInfo.InvariantCulture)),
            FieldValueKind.Ratio => FieldValue.OfRatio(Convert.ToDouble(raw, CultureInfo.InvariantCulture)),
            FieldValueKind.Boolean => FieldValue.OfBoolean((bool)raw),
            FieldValueKind.Date => FieldValue.OfDate((DateOnly)raw),
            FieldValueKind.Instant => FieldValue.OfInstant(AsInstant(raw)),
            FieldValueKind.Duration => FieldValue.OfDuration((TimeSpan)raw),
            FieldValueKind.Link => FieldValue.OfLink((Uri)raw),
            FieldValueKind.Artwork => FieldValue.OfArtwork(((ArtworkImage)raw).Address),
            FieldValueKind.FilePath => FieldValue.OfFilePath(raw.ToString() ?? string.Empty),
            FieldValueKind.Language => FieldValue.OfLanguage((Language)raw),
            FieldValueKind.Quality => FieldValue.OfQuality((QualityTier)raw),
            FieldValueKind.Ordinal => FieldValue.OfOrdinal((OrdinalPath)raw),
            FieldValueKind.Enumerated => FieldValue.OfEnumerated(
                DerivedNames.Identifier(raw.ToString() ?? string.Empty)),
            FieldValueKind.ExternalIdentifier => FieldValue.OfExternalIdentifier((ExternalId)raw),
            FieldValueKind.Composite => Composite(field, raw),
            _ => FieldValue.Absent(kind)
        };

    private FieldValue Reference(object raw)
    {
        // A reference carries both the handle and the referent's own title: the handle so a consumer can
        // follow it, the title so a consumer that will not follow it still has something to show a person.
        //
        // A group is addressed per axis rather than per level — it is deliberately not a level — so the
        // axis identifier fills the level slot of the handle. The alternative was inventing a level for
        // every grouping axis, which is exactly the fused shape the descriptor keeps apart.
        var reading = ItemTypeReader.ReadRow(raw.GetType());
        var title = reading.FirstOrDefault(static candidate => candidate.Carries(FieldSemantics.Title));
        var key = reading.FirstOrDefault(static candidate =>
            candidate.Property.PropertyType == typeof(MediaItemId));

        var address = _groupAxisIds.TryGetValue(raw.GetType(), out var axisId)
            ? MediaLevelId.FromString(axisId)
            : LevelId;

        var id = key?.Property.GetValue(raw) is MediaItemId itemId ? itemId : default;

        return new FieldValue
        {
            Kind = FieldValueKind.Reference,
            Reference = new MediaItemRef(Kind, address, id),
            Text = title?.Property.GetValue(raw)?.ToString()
        };
    }

    private static FieldValue Composite(DerivedField field, object raw)
    {
        var components = new List<FieldValue>();

        foreach (var component in field.Descriptor.Components)
        {
            var property = raw.GetType().GetProperty(
                char.ToUpperInvariant(component.FieldId[0]) + component.FieldId[1..]);

            var value = property?.GetValue(raw);

            components.Add(value is null
                ? FieldValue.Absent(component.ValueKind)
                : FieldValue.OfText(value.ToString() ?? string.Empty));
        }

        return FieldValue.OfComposite(components);
    }

    private static long AsInt64(object raw) =>
        raw switch
        {
            MediaItemId id => id.Value,
            _ => Convert.ToInt64(raw, CultureInfo.InvariantCulture)
        };

    private static DateTimeOffset AsInstant(object raw) =>
        raw switch
        {
            DateTimeOffset instant => instant,
            DateTime moment => new DateTimeOffset(moment),
            _ => default
        };
}
