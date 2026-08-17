using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Diagnostics;

/// <summary>
/// Contributes redaction rules for the secrets one component knows the shape of.
/// </summary>
/// <remarks>
/// Every registered provider is consulted once at composition time and the resulting rule set is
/// applied to all redacted output, whatever produced it. Rules are additive: a provider cannot remove
/// or weaken a rule contributed by another.
/// </remarks>
[Experimental(ExperimentalContracts.Diagnostics, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IRedactionRuleProvider
{
    /// <summary>
    /// Gets the rules this provider contributes. Evaluated once during composition, so the collection
    /// must be stable for the lifetime of the provider.
    /// </summary>
    IReadOnlyList<RedactionRule> Rules { get; }
}
