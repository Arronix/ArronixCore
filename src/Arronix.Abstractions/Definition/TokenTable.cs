using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// A per-kind token table layered over the host scanners.
/// </summary>
/// <remarks>
/// The shared token vocabulary lives once, host-side, because a codec token means the same thing to
/// every kind; a kind extends recognition here only for tokens the shared vocabulary does not carry.
/// Occurrence selection lives on the scan, where it belongs: whether the first or the last occurrence in
/// the text wins is a property of scanning the text, not of any rule table that later consumes the one
/// value the scan produced.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
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
