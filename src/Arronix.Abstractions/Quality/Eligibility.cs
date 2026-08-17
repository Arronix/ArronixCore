using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>Why a candidate is or is not eligible.</summary>
/// <param name="IsAdmitted">Whether the candidate may be taken at all.</param>
/// <param name="RefusedBy">The requirement that refused it, when one did.</param>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct Eligibility(bool IsAdmitted, AxisRequirement? RefusedBy);
