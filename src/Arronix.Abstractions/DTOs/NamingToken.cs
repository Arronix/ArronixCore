namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents a naming token that can be used in file and folder templates.
/// Tokens are replaced with actual values during the naming process.
/// </summary>
public record NamingToken(
    string Name,
    string Description,
    string ExampleValue,
    bool IsRequired = false);
