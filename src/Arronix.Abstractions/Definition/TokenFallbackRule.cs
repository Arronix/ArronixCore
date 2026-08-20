
namespace Arronix.Abstractions.Definition;

/// <summary>
/// Where a naming token's value comes from when its primary source is empty.
/// </summary>
public sealed record TokenFallbackRule
{
    /// <summary>
    /// Gets the token the rule serves, in its template spelling.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Gets the source paths tried in order; the first with a value wins.
    /// </summary>
    public required IReadOnlyList<string> Order { get; init; }
}
