using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// How quality tiers of different format families relate.
/// </summary>
/// <remarks>
/// Append-only, currently one member: no surveyed kind compares across families, and the engine rule the
/// member implies — a cross-family cutoff reads mismatch as satisfied, and nothing is ever an upgrade
/// over a file of another family — deletes an entire per-kind model. A second member requires evidence.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum CrossFamilyRule
{
    /// <summary>Tiers of different families are never compared.</summary>
    NeverCompare = 0
}
