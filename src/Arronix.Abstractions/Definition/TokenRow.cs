
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One row of a token table: a pattern and the tag it writes.
/// </summary>
/// <param name="Pattern">The regular expression that recognizes the token.</param>
/// <param name="Tag">The tag key the recognition lands under.</param>
/// <param name="Value">
/// The tag value written on recognition, when it is fixed. Null means the pattern's own capture is the
/// value.
/// </param>
/// <param name="Constraint">
/// An additional validity constraint on the captured text, in the engine's constraint vocabulary,
/// validated at load.
/// </param>
public readonly record struct TokenRow(
    string Pattern,
    string Tag,
    string? Value = null,
    string? Constraint = null);
