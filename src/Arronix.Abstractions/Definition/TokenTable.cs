
namespace Arronix.Abstractions.Definition;

/// <summary>
/// A media-owned token table used by its declared title grammar.
/// </summary>
/// <remarks>
/// A table states only vocabulary owned by the declaring media type. Representation packages own their
/// own technical vocabulary; there is no host-global codec or format scan. Occurrence selection lives on
/// the scan: whether the first or the last occurrence in
/// the text wins is a property of scanning the text, not of any rule table that later consumes the one
/// value the scan produced.
/// </remarks>
public sealed record TokenTable
{
    /// <summary>
    /// Gets the table's identifier.
    /// </summary>
    public required string TableId { get; init; }

    /// <summary>
    /// Gets the token rows, in declared order.
    /// </summary>
    public required IReadOnlyList<TokenRow> Rows { get; init; }

    /// <summary>
    /// Gets which occurrence wins when a row's pattern matches the text more than once.
    /// </summary>
    public OccurrenceSelection Occurrence { get; init; } = OccurrenceSelection.FirstOccurrence;
}
