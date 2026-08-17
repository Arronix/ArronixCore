using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>A media kind's chance to contribute its own evidence to its family's axes.</summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
/// <remarks>
/// <para>
/// The family reads what every kind's releases say; a kind refines that with what only <i>its</i>
/// releases say. Bracketed naming conventions, a scene's disc-image spelling, a kind's own guard set —
/// all of it stays inside the extension that owns the strings, and the contract assembly never learns an
/// identifier it did not derive.
/// </para>
/// <para>
/// <b>Refinements may only strengthen absence into presence, or raise a reading's source.</b> A
/// refinement that returns a reading at a weaker <see cref="EvidenceSource"/> than the one already
/// present is discarded by the host, which is what stops a per-kind heuristic from overwriting a probe. A
/// refinement therefore cannot make the family's reading <i>worse</i>, only more complete — and because a
/// refinement contributes at <see cref="EvidenceSource.Assumed"/>, it can inform ranking and rendering
/// while <see cref="AxisRequirement.MinimumSource"/> keeps it from refusing anything.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityRefinement<TFacts>
    where TFacts : IQualityFacts
{
    /// <summary>Gets the family the refinement contributes to.</summary>
    static abstract FormatFamilyId Family { get; }

    /// <summary>Contributes this kind's own evidence.</summary>
    /// <param name="read">What the family read.</param>
    /// <param name="evidence">The same evidence the family saw, including this kind's guards and tags.</param>
    /// <returns>The refined facts.</returns>
    static abstract TFacts Refine(TFacts read, ReleaseEvidence evidence);
}
