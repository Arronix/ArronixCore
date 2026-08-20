using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One row mapping how a match was made to how much it is trusted.
/// </summary>
/// <param name="Basis">What the match was made on.</param>
/// <param name="CoordinateConfidence">
/// The coordinate confidence the row additionally requires, or null for any.
/// </param>
/// <param name="Result">The confidence reported.</param>
/// <param name="SourceIn">The provenances the row applies to, or null for all.</param>
public readonly record struct ConfidenceRule(
    MatchBasis Basis,
    CoordinateConfidence? CoordinateConfidence,
    MatchConfidence Result,
    IReadOnlyList<MatchSource>? SourceIn = null);
