
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One alias spelling of a search subject, as a template.
/// </summary>
public sealed record AliasTemplate
{
    /// <summary>
    /// Gets the alias row's identifier.
    /// </summary>
    public required string AliasId { get; init; }

    /// <summary>
    /// Gets the spelling template over fields, grouping-axis fields and the coordinate grammar.
    /// </summary>
    public required string Template { get; init; }

    /// <summary>
    /// Gets the row's position among the spellings; lower is more canonical.
    /// </summary>
    public int Order { get; init; } = 1;

    /// <summary>
    /// Gets a value indicating whether spellings from this row are emitted only for languages the
    /// acquisition's accepted-language list allows — what makes translated-spelling fan-out affordable.
    /// </summary>
    public bool FilterByAcceptedLanguages { get; init; }

    /// <summary>
    /// Gets a value indicating whether spellings from this row ride along as aliases only and never
    /// become a query of their own.
    /// </summary>
    public bool NeverOwnQuery { get; init; }
}
