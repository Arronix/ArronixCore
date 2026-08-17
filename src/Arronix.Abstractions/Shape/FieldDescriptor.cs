using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Declares one field an item carries: what it means, how important it is, and what shape its values
/// take.
/// </summary>
/// <remarks>
/// This one declaration serves every consumer. A summary reads the primary fields, a table takes its
/// default columns from the same rank, a text client's verbose mode reads the rest, sorting and filtering
/// read <see cref="Semantics"/>, and a support bundle reads the diagnostic tail. Parallel identifier
/// lists per consumer were rejected because they drift the moment a field is added.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record FieldDescriptor
{
    /// <summary>
    /// Gets the identifier values, sorts and filters reference this field by. Unique within its level.
    /// </summary>
    public required string FieldId { get; init; }

    /// <summary>
    /// Gets the field's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a sentence explaining what the field holds.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the shape of the field's values.
    /// </summary>
    public required FieldValueKind ValueKind { get; init; }

    /// <summary>
    /// Gets what the field means to the platform, beyond its value shape.
    /// </summary>
    public FieldSemantics Semantics { get; init; } = FieldSemantics.None;

    /// <summary>
    /// Gets how important the field is.
    /// </summary>
    public Prominence Prominence { get; init; } = Prominence.Detail;

    /// <summary>
    /// Gets a value indicating whether the field holds a list of values rather than one.
    /// </summary>
    public bool Multivalued { get; init; }

    /// <summary>
    /// Gets a value indicating whether a user may change the value.
    /// </summary>
    public bool Editable { get; init; }

    /// <summary>
    /// Gets the unit the value is expressed in, for presentation only.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Gets the permitted values. Populated when <see cref="ValueKind"/> is
    /// <see cref="FieldValueKind.Enumerated"/>.
    /// </summary>
    public IReadOnlyList<FacetValue> Choices { get; init; } = [];

    /// <summary>
    /// Gets the component fields of a composite. Populated when and only when <see cref="ValueKind"/> is
    /// <see cref="FieldValueKind.Composite"/>. A repeated tuple — a translation with its language, a
    /// credit with its role — is one multivalued composite field, never several parallel lists
    /// correlated by index: the correlation would be undeclarable and every consumer that filtered one
    /// list would silently desynchronize the rest.
    /// </summary>
    public IReadOnlyList<FieldDescriptor> Components { get; init; } = [];
}

/// <summary>
/// The shape of a field's values.
/// </summary>
/// <remarks>
/// Closed, and exhaustively handled by every consumer. A value added here is a compile error at each of
/// them, which is the point: a consumer that silently ignored an unknown shape would render an item's
/// most important field as nothing at all.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum FieldValueKind
{
    /// <summary>A single line of plain text.</summary>
    Text = 0,

    /// <summary>Several lines of plain text. Never markup.</summary>
    MultilineText = 1,

    /// <summary>A whole number.</summary>
    Integer = 2,

    /// <summary>A fractional number.</summary>
    Decimal = 3,

    /// <summary>A two-state value.</summary>
    Boolean = 4,

    /// <summary>A calendar date with no time part.</summary>
    Date = 5,

    /// <summary>A point in time, with an offset.</summary>
    Instant = 6,

    /// <summary>An elapsed length of time.</summary>
    Duration = 7,

    /// <summary>A size in bytes.</summary>
    ByteSize = 8,

    /// <summary>A proportion, where one means whole.</summary>
    Ratio = 9,

    /// <summary>A position expressed as an ordinal tuple.</summary>
    Ordinal = 10,

    /// <summary>One of the field's declared choices.</summary>
    Enumerated = 11,

    /// <summary>A reference to another item.</summary>
    Reference = 12,

    /// <summary>An identifier assigned by an external catalog.</summary>
    ExternalIdentifier = 13,

    /// <summary>An address outside the platform.</summary>
    Link = 14,

    /// <summary>A path on a storage mount.</summary>
    FilePath = 15,

    /// <summary>A language.</summary>
    Language = 16,

    /// <summary>A quality tier.</summary>
    Quality = 17,

    /// <summary>An image that represents the item.</summary>
    Artwork = 18,

    /// <summary>A count of something, which may be compared against a total.</summary>
    Count = 19,

    /// <summary>A tuple of the field's declared component fields, kept together as one value.</summary>
    Composite = 20
}

/// <summary>
/// What a field means to the platform, beyond the shape of its values.
/// </summary>
/// <remarks>
/// Flags rather than one value because the meanings compose: the field that holds an item's title is
/// routinely also the field it is searched by and sorted on.
/// </remarks>
[Flags]
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum FieldSemantics
{
    /// <summary>No special meaning.</summary>
    None = 0,

    /// <summary>The field identifies the item to a person.</summary>
    Identity = 1,

    /// <summary>The field is the item's title. Exactly one field per level carries this.</summary>
    Title = 2,

    /// <summary>The field is the normalized form the item sorts by.</summary>
    SortKey = 4,

    /// <summary>The field may be sorted on.</summary>
    Sortable = 8,

    /// <summary>The field may be filtered on.</summary>
    Filterable = 16,

    /// <summary>The field may be partitioned on.</summary>
    Groupable = 32,

    /// <summary>The field participates in free-text search.</summary>
    Searchable = 64,

    /// <summary>The field reports how far along something is.</summary>
    Progress = 128,

    /// <summary>The field reports a condition rather than a property.</summary>
    Status = 256,

    /// <summary>The field records when something happened.</summary>
    Timestamp = 512,

    /// <summary>The field reports a size.</summary>
    Size = 1024,

    /// <summary>The field addresses an image representing the item.</summary>
    Artwork = 2048,

    /// <summary>The field distinguishes items that would otherwise share a title.</summary>
    Disambiguation = 4096
}

/// <summary>
/// How important a field is.
/// </summary>
/// <remarks>
/// An importance rank, not a layout instruction. It is meaningful to a summary, to a default column set
/// and to a verbose flag alike, and it orders naturally. Explicitly rejected in its place: a width, an
/// absolute position and an inline flag, none of which mean anything outside one particular presentation.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum Prominence
{
    /// <summary>Identifies the item. Always worth showing.</summary>
    Primary = 0,

    /// <summary>Qualifies the item. Worth showing when there is room.</summary>
    Secondary = 1,

    /// <summary>Shown when the item itself is the subject.</summary>
    Detail = 2,

    /// <summary>Shown when something has gone wrong.</summary>
    Diagnostic = 3
}
