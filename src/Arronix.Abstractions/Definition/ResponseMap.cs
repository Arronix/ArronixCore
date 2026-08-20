
namespace Arronix.Abstractions.Definition;

/// <summary>
/// How one response shape maps onto one level's or one grouping axis's declared fields.
/// </summary>
public sealed record ResponseMap
{
    /// <summary>
    /// Gets the level the map populates. Exactly one of this and <see cref="AxisId"/> is set.
    /// </summary>
    public string? LevelId { get; init; }

    /// <summary>
    /// Gets the grouping axis the map populates. Exactly one of this and <see cref="LevelId"/> is set.
    /// </summary>
    public string? AxisId { get; init; }

    /// <summary>
    /// Gets the path to the record's external identifier in the response.
    /// </summary>
    public required string ExternalIdPath { get; init; }

    /// <summary>
    /// Gets the scheme the external identifier belongs to.
    /// </summary>
    public required string ExternalIdScheme { get; init; }

    /// <summary>
    /// Gets the path to member records that re-enter another map, for group responses that embed their
    /// members.
    /// </summary>
    public string? MemberPath { get; init; }

    /// <summary>
    /// Gets the field rows.
    /// </summary>
    public required IReadOnlyList<ResponseMapRow> Rows { get; init; }
}
