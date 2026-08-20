
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One credited-name substitution the community's grammar uses in place of the catalog form.
/// </summary>
/// <param name="Credit">The credited name as the catalog writes it.</param>
/// <param name="Substitute">The form the release community writes instead.</param>
public readonly record struct CreditSubstitution(string Credit, string Substitute);
