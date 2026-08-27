using System.Collections;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Catalog;

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
    /// Projects one entity under the reference the host holds it by.
    /// </summary>
    /// <param name="reference">The host-owned reference.</param>
    /// <param name="item">The entity.</param>
    /// <param name="identity">The read half of host identity state, used to address group references.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="reference"/> belongs to another media kind or level, or <paramref name="item"/> is
    /// of the wrong type.
    /// </exception>
    /// <remarks>
    /// The caller supplies the root reference, so projecting an entity does not derive its identity and an
    /// entity no catalog has named remains projectable. Reference-valued fields are resolved through the
    /// read half of identity state, which has no member that could assign one.
    /// </remarks>
    internal ItemView Project(MediaItemRef reference, object item, ICatalogIdentityReader identity)
    {
        Require(item);
        ArgumentNullException.ThrowIfNull(identity);

        if (reference.Kind != Kind || reference.Level != LevelId)
        {
            throw new ArgumentException(
                $"The '{Kind}' runtime projects level '{LevelId}', not '{reference.Kind}:{reference.Level}'.",
                nameof(reference));
        }

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);

        foreach (var candidate in Reading.Fields)
        {
            fields[candidate.FieldId] = ValueOf(candidate, item, identity);
        }

        var entity = (IMediaEntity)item;

        return new ItemView
        {
            Ref = reference,
            Title = entity.Title,
            TitleLanguage = entity.TitleLanguage,
            Fields = fields,
            ExternalIds = entity.ExternalIds.Values,
            SortIndex = reference.Id.Value
        };
    }

    /// <summary>
    /// Reads one field off one entity.
    /// </summary>
    /// <param name="item">The entity.</param>
    /// <param name="fieldId">The field identifier.</param>
    /// <param name="identity">The read half of host identity state, used to address group references.</param>
    /// <returns>The value, or an absent value when the entity carries none.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The entity is of the wrong type, or the field is unknown.</exception>
    internal FieldValue Read(object item, string fieldId, ICatalogIdentityReader identity)
    {
        Require(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldId);
        ArgumentNullException.ThrowIfNull(identity);

        return _byFieldId.TryGetValue(fieldId, out var candidate)
            ? ValueOf(candidate, item, identity)
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

    private FieldValue ValueOf(DerivedField field, object item, ICatalogIdentityReader identity)
    {
        var raw = field.Read(item);
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
                elements.Add(element is null
                    ? FieldValue.Absent(kind)
                    : Scalar(field, kind, element, identity));
            }

            return FieldValue.OfItems(kind, elements);
        }

        return Scalar(field, kind, raw, identity);
    }

    private FieldValue Scalar(DerivedField field, FieldValueKind kind, object raw, ICatalogIdentityReader identity) =>
        kind == FieldValueKind.Reference ? Reference(raw, identity) : ScalarOfKind(field, kind, raw);

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

    private FieldValue Reference(object raw, ICatalogIdentityReader identity)
    {
        // A reference carries both the handle and the referent's own title: the handle so a consumer can
        // follow it, the title so a consumer that will not follow it still has something to show a person.
        //
        // A group is addressed per axis rather than per level — it is deliberately not a level — so the
        // axis identifier fills the level slot of the handle. The alternative was inventing a level for
        // every grouping axis, which is exactly the fused shape the descriptor keeps apart.
        if (raw is not IMediaEntity entity)
        {
            throw new ArgumentException(
                $"'{raw.GetType().FullName}' was compiled as a reference but does not implement '{nameof(IMediaEntity)}'.",
                nameof(raw));
        }

        var address = _groupAxisIds.TryGetValue(raw.GetType(), out var axisId)
            ? MediaLevelId.FromString(axisId)
            : LevelId;

        if (entity.ExternalIds.Values.Count == 0)
        {
            throw new ArgumentException(
                $"'{raw.GetType().FullName}' is compiled as a reference but states no catalog identifier, "
                + "so there is nothing to address it by.",
                nameof(raw));
        }

        // The referent is addressed in its own level, which is its own key space: a group's identifiers and
        // an item's are never compared. Resolution only: a referent the host holds no identity for is
        // projected under its catalog identity rather than given one, because minting here would make every
        // page render a write.
        MediaItemRef? handle = null;
        var catalogId = entity.ExternalIds.Values[0];

        foreach (var candidate in entity.ExternalIds.Values)
        {
            if (identity.TryFind(Kind, address, candidate, out var found))
            {
                handle = identity.Canonical(found);
                catalogId = candidate;
                break;
            }
        }

        return new FieldValue
        {
            Kind = FieldValueKind.Reference,
            Reference = handle,

            // Exactly one handle: the local one when the host holds the referent, the catalog's own when it
            // does not, so an unresolved reference is still something a consumer can follow up.
            External = handle is null ? catalogId : null,
            Text = entity.Title
        };
    }

    private static FieldValue Composite(DerivedField field, object raw)
        => Composite(field.Components, raw);

    private static FieldValue Composite(IReadOnlyList<CompiledField> components, object raw)
    {
        var values = new List<FieldValue>();

        foreach (var component in components)
        {
            values.Add(ComponentValue(component, component.Read(raw)));
        }

        return FieldValue.OfComposite(values);
    }

    private static FieldValue ComponentValue(CompiledField field, object? raw)
    {
        var descriptor = field.Descriptor;
        if (raw is null)
        {
            return FieldValue.Absent(descriptor.ValueKind);
        }

        if (descriptor.Multivalued && raw is IEnumerable sequence and not string)
        {
            return FieldValue.OfItems(
                descriptor.ValueKind,
                [.. sequence.Cast<object?>().Select(value => ComponentScalar(field, value))]);
        }

        return ComponentScalar(field, raw);
    }

    private static FieldValue ComponentScalar(CompiledField field, object? raw)
    {
        var descriptor = field.Descriptor;
        if (raw is null)
        {
            return FieldValue.Absent(descriptor.ValueKind);
        }

        return descriptor.ValueKind switch
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
            FieldValueKind.Composite => Composite(field.Components, raw),
            _ => FieldValue.Absent(descriptor.ValueKind)
        };
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
