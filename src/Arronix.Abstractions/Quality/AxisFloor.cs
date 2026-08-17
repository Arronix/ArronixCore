using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>One axis's contribution to "good enough".</summary>
/// <param name="Axis">The axis.</param>
/// <param name="AtLeast">The least acceptable richness.</param>
/// <param name="WhenUnknown">
/// What an absent reading means. <see cref="UnknownEvidence.Ignore"/> makes the floor vacuous, which is
/// how one floor covers an axis that has no reading for a legitimate reason.
/// </param>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct AxisFloor(
    QualityAxisId Axis,
    AxisValue AtLeast,
    UnknownEvidence WhenUnknown);
