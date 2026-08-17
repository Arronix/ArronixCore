using Arronix.Abstractions.Shape;

// The shape contracts the candidate row reuses are experimental.
#pragma warning disable ARX0013

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// One row of an assignment problem: a reading or a unit, reduced to the evidence the distance features
/// compute over.
/// </summary>
/// <remarks>
/// Media-agnostic on purpose: the surveyed inputs — a tagged audio file against a catalog recording — are
/// projected into this row by the caller, and the features never see a media noun.
/// </remarks>
internal sealed record AssignmentCandidate
{
    /// <summary>
    /// Gets the candidate's title text.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the candidate's absolute position along its sequence, when it states one.
    /// </summary>
    public long? Position { get; init; }

    /// <summary>
    /// Gets the candidate's running length, when it states one.
    /// </summary>
    public TimeSpan? Length { get; init; }

    /// <summary>
    /// Gets the candidate's stated year, when it states one.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets the external identifiers the candidate is known by, current first.
    /// </summary>
    public IReadOnlyList<ExternalId> ExternalIds { get; init; } = [];
}
