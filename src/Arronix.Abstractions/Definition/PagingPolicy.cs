
namespace Arronix.Abstractions.Definition;

/// <summary>
/// How many pages a catalog fetch may take, and what running out means.
/// </summary>
/// <remarks>
/// Truncation is reported as incompleteness, never conflated with transport failure: a list longer than
/// its page budget is a different fact from a list that could not be fetched, and completeness reporting
/// downstream depends on the difference.
/// </remarks>
public sealed record PagingPolicy
{
    /// <summary>
    /// Gets the default policy: ten pages, truncation reported as incompleteness.
    /// </summary>
    public static PagingPolicy Default { get; } = new();

    /// <summary>
    /// Gets the greatest number of pages one fetch may take.
    /// </summary>
    public int MaxPages { get; init; } = 10;

    /// <summary>
    /// Gets a value indicating whether hitting the page budget marks the result incomplete.
    /// </summary>
    public bool TruncationIsFailure { get; init; } = true;
}
