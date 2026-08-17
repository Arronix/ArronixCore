using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// The host derivations a catalog declaration may invoke. Closed and append-only.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum DerivationKind
{
    /// <summary>An ordered status ladder with dated stage conditions and a windowed final stage.</summary>
    StatusStages = 0,

    /// <summary>A single date reduced from several candidate dates.</summary>
    DateReduction = 1,

    /// <summary>A value selected by region key, with cross-region fallback declared on or off.</summary>
    RegionSelect = 2,

    /// <summary>Images selected into declared roles.</summary>
    ImageRoleSelect = 3,

    /// <summary>A value written only when a guard condition holds.</summary>
    Conditional = 4
}
