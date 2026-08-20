
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One query parameter of a request template.
/// </summary>
/// <param name="Name">The parameter name as the catalog expects it.</param>
/// <param name="Template">The value template, with converters after a colon.</param>
public readonly record struct RequestParameter(string Name, string Template);
