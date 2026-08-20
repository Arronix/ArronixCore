using System.Globalization;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// The shape of the values a coordinate space is addressed by.
/// </summary>
/// <remarks>
/// A closed vocabulary. A consumer switches over it exhaustively, so a value added here is a compile
/// error at every consumer rather than a silent misrender.
/// </remarks>
public enum CoordinateKind
{
    /// <summary>An ordinal tuple, ordered and countable.</summary>
    Ordinal = 0,

    /// <summary>A calendar date, ordered but not countable in the sense a dense sequence is.</summary>
    Date = 1,

    /// <summary>A label that may not parse as a number: <c>"A1"</c>, <c>"2.5"</c>, <c>"1-3"</c>. Equatable, not orderable.</summary>
    Label = 2,

    /// <summary>The level addresses itself; there is exactly one position and it needs no value.</summary>
    Singleton = 3
}

/// <summary>
/// One reading's worth of position within a coordinate space.
/// </summary>
/// <remarks>
/// <para>
/// A tagged struct rather than a closed record hierarchy: this value crosses both the extension boundary
/// and the client boundary, and polymorphic serialization across two independently versioned assemblies
/// is the versioning trap this contract layer refuses to walk into. Exactly one payload slot is populated
/// per <see cref="Kind"/>.
/// </para>
/// <para>
/// A plain string was rejected because the host must <i>order</i> coordinates — browse in sequence, find
/// the gaps in a dense run, decide what comes next — and ordering cannot be recovered from text.
/// </para>
/// </remarks>
public readonly record struct Coordinate
{
    /// <summary>
    /// Gets the kind of value carried, which selects the populated payload slot.
    /// </summary>
    public CoordinateKind Kind { get; init; }

    /// <summary>
    /// Gets the ordinal tuple. Populated when <see cref="Kind"/> is <see cref="CoordinateKind.Ordinal"/>.
    /// </summary>
    public OrdinalPath Ordinals { get; init; }

    /// <summary>
    /// Gets the calendar date. Populated when <see cref="Kind"/> is <see cref="CoordinateKind.Date"/>.
    /// </summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// Gets the label. Populated when <see cref="Kind"/> is <see cref="CoordinateKind.Label"/>.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Gets the position of a level that has exactly one.
    /// </summary>
    public static Coordinate Singleton { get; } = new() { Kind = CoordinateKind.Singleton };

    /// <summary>
    /// Creates an ordinal coordinate.
    /// </summary>
    /// <param name="path">The ordinal tuple.</param>
    /// <returns>The coordinate.</returns>
    public static Coordinate OfOrdinals(OrdinalPath path)
        => new() { Kind = CoordinateKind.Ordinal, Ordinals = path };

    /// <summary>
    /// Creates a date coordinate.
    /// </summary>
    /// <param name="value">The calendar date.</param>
    /// <returns>The coordinate.</returns>
    public static Coordinate OfDate(DateOnly value)
        => new() { Kind = CoordinateKind.Date, Date = value };

    /// <summary>
    /// Creates a label coordinate.
    /// </summary>
    /// <param name="value">The label.</param>
    /// <returns>The coordinate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Coordinate OfLabel(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Coordinate { Kind = CoordinateKind.Label, Label = value };
    }

    /// <summary>
    /// Gets the diagnostic form of the populated payload.
    /// </summary>
    /// <returns>The coordinate text; <c>"*"</c> for a singleton.</returns>
    public override string ToString() => Kind switch
    {
        CoordinateKind.Ordinal => Ordinals.ToString(),
        CoordinateKind.Date => Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        CoordinateKind.Label => Label ?? string.Empty,
        CoordinateKind.Singleton => "*",
        _ => string.Empty
    };
}

/// <summary>
/// How much a coordinate reading can be trusted.
/// </summary>
/// <remarks>
/// Confidence is a property of the reading, not of the space: the same space carries values confirmed
/// against a catalog and values extrapolated by a mapping service, and a consumer that cannot tell them
/// apart will present a guess as a fact.
/// </remarks>
public enum CoordinateConfidence
{
    /// <summary>Extrapolated or guessed, and flagged as such by whatever produced it.</summary>
    Unverified = 0,

    /// <summary>Derived from another coordinate through a declared mapping.</summary>
    Derived = 1,

    /// <summary>Asserted by a release name or by a file.</summary>
    Asserted = 2,

    /// <summary>Confirmed against the catalog record.</summary>
    Verified = 3
}

/// <summary>
/// One item's position in one coordinate space.
/// </summary>
/// <param name="SpaceId">The <see cref="CoordinateSpace.SpaceId"/> the reading is expressed in.</param>
/// <param name="Value">The position.</param>
/// <param name="Confidence">How much the reading can be trusted.</param>
public readonly record struct CoordinateReading(
    string SpaceId,
    Coordinate Value,
    CoordinateConfidence Confidence);
