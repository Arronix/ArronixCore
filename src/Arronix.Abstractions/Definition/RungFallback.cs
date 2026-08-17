using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// Where quality evidence that lands between rungs goes.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum RungFallback
{
    /// <summary>Round up, never down: an unrecognized source is never treated as the worst one.</summary>
    RoundUp = 0,

    /// <summary>Take the nearest rung in either direction.</summary>
    Nearest = 1
}
