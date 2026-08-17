using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>The typed facts one file's quality consists of. Marker only.</summary>
/// <remarks>
/// Deliberately empty: everything about a quality type is read from its properties. There is exactly one
/// concept here, the axis, and no second kind of quality fact — no flags field, no side-channel score.
/// That invariant is what keeps the policy vocabulary small enough for one readable editor.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityFacts;
