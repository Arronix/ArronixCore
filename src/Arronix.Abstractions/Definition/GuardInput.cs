
namespace Arronix.Abstractions.Definition;

/// <summary>
/// Which form of the text a guard expression runs against.
/// </summary>
/// <remarks>
/// The surveyed parsers probe the raw text where normalization destroys the evidence — a bracketed
/// literal, a second-chance resolution check — while everything else scans the normalized form. The
/// choice is per guard, exactly as the sources make it.
/// </remarks>
public enum GuardInput
{
    /// <summary>The normalized text the pattern list runs against.</summary>
    Normalized = 0,

    /// <summary>The raw text as it arrived.</summary>
    Raw = 1
}
