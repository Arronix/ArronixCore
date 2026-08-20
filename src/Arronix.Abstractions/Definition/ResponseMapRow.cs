
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One field row of a response map.
/// </summary>
public sealed record ResponseMapRow
{
    /// <summary>
    /// Gets the path into the response document.
    /// </summary>
    public required string JsonPath { get; init; }

    /// <summary>
    /// Gets the declared field the value lands in.
    /// </summary>
    public required string FieldId { get; init; }

    /// <summary>
    /// Gets the value converter applied, from the engine's closed converter set. Null passes the value
    /// through. An unknown converter is a load failure.
    /// </summary>
    public string? Converter { get; init; }
}
