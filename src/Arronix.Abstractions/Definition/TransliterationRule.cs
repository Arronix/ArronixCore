using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One reversible character-sequence substitution applied during normalization.
/// </summary>
/// <param name="From">The text as written.</param>
/// <param name="To">The text as compared.</param>
/// <remarks>
/// Distinct from diacritic folding because the mapping is not one letter to its base letter — a folded
/// form would collide with a different word, so the row states the conventional spelling instead.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct TransliterationRule(string From, string To);
