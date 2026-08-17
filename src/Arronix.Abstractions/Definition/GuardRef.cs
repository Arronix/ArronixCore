using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// A reference to a declared guard, optionally negated.
/// </summary>
/// <param name="GuardId">The guard referenced. Unknown identifiers are a load failure.</param>
/// <param name="Negated">Whether the reference holds when the guard does not match.</param>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct GuardRef(string GuardId, bool Negated = false);
