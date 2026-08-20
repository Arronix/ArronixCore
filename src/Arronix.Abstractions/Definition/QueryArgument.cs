using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One structured argument of a query tier, as a template.
/// </summary>
/// <param name="Term">The kind of argument.</param>
/// <param name="Template">The value template over fields and identifiers.</param>
/// <param name="Scheme">The external-identifier scheme, when the term is an identifier.</param>
/// <param name="OmitWhenAbsent">
/// Whether the argument is dropped when its template resolves to nothing, rather than failing the tier.
/// </param>
public readonly record struct QueryArgument(
    SearchTerm Term,
    string Template,
    string? Scheme = null,
    bool OmitWhenAbsent = false);
