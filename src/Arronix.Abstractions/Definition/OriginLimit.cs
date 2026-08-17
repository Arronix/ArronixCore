using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// The greatest number of results wanted for one search origin.
/// </summary>
/// <param name="Origin">The origin the limit applies to.</param>
/// <param name="Limit">The greatest number of results wanted.</param>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct OriginLimit(SearchOrigin Origin, int Limit);
