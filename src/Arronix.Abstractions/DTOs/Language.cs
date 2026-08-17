namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents a language code and name for media content.
/// </summary>
public record Language(string Code, string Name)
{
    /// <summary>
    /// English language.
    /// </summary>
    public static readonly Language English = new("en", "English");

    /// <summary>
    /// Unknown or unspecified language.
    /// </summary>
    public static readonly Language Unknown = new("und", "Unknown");

    /// <summary>
    /// Gets the string representation (code) of this language.
    /// </summary>
    public override string ToString() => Code;
}
