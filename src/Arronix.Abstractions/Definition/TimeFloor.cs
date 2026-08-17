using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// The boundary a request instant is floored to.
/// </summary>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum TimeFloor
{
    /// <summary>No flooring.</summary>
    None = 0,

    /// <summary>Floor to the hour.</summary>
    Hour = 1,

    /// <summary>Floor to the day.</summary>
    Day = 2
}
