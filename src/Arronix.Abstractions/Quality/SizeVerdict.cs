using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>Whether a size is plausible for what the release claims.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum SizeVerdict
{
    /// <summary>
    /// Nothing can be said. Always a pass, never a rejection: a release nobody can assess is not thereby
    /// implausible, and a size gate that refused what it could not measure would be a requirement in
    /// disguise.
    /// </summary>
    NotAssessable = 0,

    /// <summary>Inside the expected band.</summary>
    Plausible = 1,

    /// <summary>Below the floor: too small for what it claims to be.</summary>
    ImplausiblySmall = 2,

    /// <summary>Above the ceiling: too large for what it claims to be.</summary>
    ImplausiblyLarge = 3,
}
