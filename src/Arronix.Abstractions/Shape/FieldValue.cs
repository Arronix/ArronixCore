using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// One field's value, tagged with the shape it carries.
/// </summary>
/// <remarks>
/// <para>
/// One tagged record rather than twenty derived types. Exactly one payload slot is populated per
/// <see cref="Kind"/>; <see cref="Items"/> carries a multivalued field; <see cref="IsAbsent"/> means the
/// item has no value for the field, which is different from an empty one.
/// </para>
/// <para>
/// A bag of strings was rejected because a consumer must format dates, sizes and durations for the
/// reader's locale and cannot do that once the type is gone. A polymorphic hierarchy was rejected because
/// this value crosses two independently versioned assembly boundaries, where a serializer that has to
/// resolve a subtype by name is a versioning hazard rather than a convenience.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record FieldValue
{
    /// <summary>
    /// Gets the shape of the value, which selects the populated payload slot.
    /// </summary>
    public required FieldValueKind Kind { get; init; }

    /// <summary>
    /// Gets a value indicating whether the item has no value for this field.
    /// </summary>
    public bool IsAbsent { get; init; }

    /// <summary>
    /// Gets the text payload, used by the text, path and enumerated shapes.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the whole-number payload, used by the integer, byte-size and count shapes.
    /// </summary>
    public long? Number { get; init; }

    /// <summary>
    /// Gets the fractional payload, used by the decimal and ratio shapes.
    /// </summary>
    public double? Real { get; init; }

    /// <summary>
    /// Gets the two-state payload.
    /// </summary>
    public bool? Flag { get; init; }

    /// <summary>
    /// Gets the point-in-time payload.
    /// </summary>
    public DateTimeOffset? Instant { get; init; }

    /// <summary>
    /// Gets the calendar-date payload.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// Gets the elapsed-time payload.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Gets the ordinal-tuple payload.
    /// </summary>
    public OrdinalPath? Ordinals { get; init; }

    /// <summary>
    /// Gets the item-reference payload.
    /// </summary>
    public MediaItemRef? Reference { get; init; }

    /// <summary>
    /// Gets the external-identifier payload.
    /// </summary>
    public ExternalId? External { get; init; }

    /// <summary>
    /// Gets the address payload, used by the link and artwork shapes.
    /// </summary>
    public Uri? Link { get; init; }

    /// <summary>
    /// Gets the quality payload. Reuses the stable quality tier.
    /// </summary>
    public QualityTier? Quality { get; init; }

    /// <summary>
    /// Gets the language payload. Reuses the stable language.
    /// </summary>
    public Language? Language { get; init; }

    /// <summary>
    /// Gets the elements of a multivalued field, each carrying the same <see cref="Kind"/> — or, for the
    /// composite shape, the component values in the order the field's components are declared.
    /// </summary>
    public IReadOnlyList<FieldValue>? Items { get; init; }

    /// <summary>Creates a value meaning "this item has no value for this field".</summary>
    /// <param name="kind">The shape the field would have carried.</param>
    /// <returns>The value.</returns>
    public static FieldValue Absent(FieldValueKind kind) => new() { Kind = kind, IsAbsent = true };

    /// <summary>Creates a single-line text value.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfText(string value) => new() { Kind = FieldValueKind.Text, Text = value };

    /// <summary>Creates a multi-line plain-text value.</summary>
    /// <param name="value">The text, with no markup.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfMultilineText(string value)
        => new() { Kind = FieldValueKind.MultilineText, Text = value };

    /// <summary>Creates a whole-number value.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfInteger(long value) => new() { Kind = FieldValueKind.Integer, Number = value };

    /// <summary>Creates a fractional-number value.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfDecimal(double value) => new() { Kind = FieldValueKind.Decimal, Real = value };

    /// <summary>Creates a two-state value.</summary>
    /// <param name="value">The state.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfBoolean(bool value) => new() { Kind = FieldValueKind.Boolean, Flag = value };

    /// <summary>Creates a calendar-date value.</summary>
    /// <param name="value">The date.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfDate(DateOnly value) => new() { Kind = FieldValueKind.Date, Date = value };

    /// <summary>Creates a point-in-time value.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfInstant(DateTimeOffset value)
        => new() { Kind = FieldValueKind.Instant, Instant = value };

    /// <summary>Creates an elapsed-time value.</summary>
    /// <param name="value">The duration.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfDuration(TimeSpan value)
        => new() { Kind = FieldValueKind.Duration, Duration = value };

    /// <summary>Creates a size-in-bytes value.</summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfByteSize(long bytes) => new() { Kind = FieldValueKind.ByteSize, Number = bytes };

    /// <summary>Creates a proportion value.</summary>
    /// <param name="value">The proportion, where one means whole.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfRatio(double value) => new() { Kind = FieldValueKind.Ratio, Real = value };

    /// <summary>Creates an ordinal-tuple value.</summary>
    /// <param name="path">The tuple.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfOrdinal(OrdinalPath path)
        => new() { Kind = FieldValueKind.Ordinal, Ordinals = path };

    /// <summary>Creates a value drawn from a field's declared choices.</summary>
    /// <param name="value">The stored value of the chosen entry.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfEnumerated(string value)
        => new() { Kind = FieldValueKind.Enumerated, Text = value };

    /// <summary>Creates a reference to another item.</summary>
    /// <param name="reference">The item referred to.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfReference(MediaItemRef reference)
        => new() { Kind = FieldValueKind.Reference, Reference = reference };

    /// <summary>Creates an external-catalog identifier value.</summary>
    /// <param name="externalId">The identifier.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfExternalIdentifier(ExternalId externalId)
        => new() { Kind = FieldValueKind.ExternalIdentifier, External = externalId };

    /// <summary>Creates an address value.</summary>
    /// <param name="link">The address.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfLink(Uri link) => new() { Kind = FieldValueKind.Link, Link = link };

    /// <summary>Creates a storage-path value.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfFilePath(string path) => new() { Kind = FieldValueKind.FilePath, Text = path };

    /// <summary>Creates a language value.</summary>
    /// <param name="language">The language.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfLanguage(Language language)
        => new() { Kind = FieldValueKind.Language, Language = language };

    /// <summary>Creates a quality value.</summary>
    /// <param name="quality">The quality tier.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfQuality(QualityTier quality)
        => new() { Kind = FieldValueKind.Quality, Quality = quality };

    /// <summary>Creates an artwork value.</summary>
    /// <param name="link">The address of the image.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfArtwork(Uri link) => new() { Kind = FieldValueKind.Artwork, Link = link };

    /// <summary>Creates a count value.</summary>
    /// <param name="count">The count.</param>
    /// <returns>The value.</returns>
    public static FieldValue OfCount(long count) => new() { Kind = FieldValueKind.Count, Number = count };

    /// <summary>
    /// Creates a multivalued value.
    /// </summary>
    /// <param name="kind">The shape each element carries.</param>
    /// <param name="items">The elements.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public static FieldValue OfItems(FieldValueKind kind, IReadOnlyList<FieldValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new FieldValue { Kind = kind, Items = items };
    }

    /// <summary>
    /// Creates a composite value: one tuple of a composite field's components, in declared order.
    /// </summary>
    /// <param name="components">The component values, in the order the field declares its components.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is <see langword="null"/>.</exception>
    public static FieldValue OfComposite(IReadOnlyList<FieldValue> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return new FieldValue { Kind = FieldValueKind.Composite, Items = components };
    }
}
