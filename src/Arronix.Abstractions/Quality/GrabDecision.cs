using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What to do with a candidate, and why.</summary>
/// <param name="Verdict">The verdict.</param>
/// <param name="Reason">
/// One sentence, naming the axis that decided. That the reason names an axis rather than a rung is the
/// whole usability argument for the model: "rung eighteen is not above rung nineteen" tells a user
/// nothing about what to change.
/// </param>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct GrabDecision(GrabVerdict Verdict, string Reason);

/// <summary>What to do with a candidate.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum GrabVerdict
{
    /// <summary>Take it.</summary>
    Grab = 0,

    /// <summary>A requirement refuses it outright.</summary>
    Refused = 1,

    /// <summary>It is not above what is already held.</summary>
    NotAnUpgrade = 2,

    /// <summary>What is held already satisfies the cutoff.</summary>
    AlreadyGoodEnough = 3,

    /// <summary>Nothing read says enough to decide.</summary>
    EvidenceInsufficient = 4,
}
