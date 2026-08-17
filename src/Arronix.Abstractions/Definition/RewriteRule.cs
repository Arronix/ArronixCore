using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One expression-to-replacement substitution, applied in declared order.
/// </summary>
/// <param name="Regex">The regular expression matched.</param>
/// <param name="Replacement">The replacement text, which may reference captures.</param>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct RewriteRule(string Regex, string Replacement);
