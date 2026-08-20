
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One identifier normalization or user-typed lookup form.
/// </summary>
public sealed record IdNormalization
{
    /// <summary>
    /// Gets which normalization the rule applies.
    /// </summary>
    public required IdRuleKind Kind { get; init; }

    /// <summary>
    /// Gets the external-identifier scheme the rule serves, when it serves one.
    /// </summary>
    public string? Scheme { get; init; }

    /// <summary>
    /// Gets the prefix a canonical identifier carries, for the prefix-and-pad kind.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Gets the digit count the numeric part is zero-padded to, for the prefix-and-pad kind.
    /// </summary>
    public int? PadDigitsTo { get; init; }

    /// <summary>
    /// Gets the address pattern an identifier is extracted from, for the address-segment kind, with the
    /// identifier slot written as a placeholder.
    /// </summary>
    public string? AddressPattern { get; init; }

    /// <summary>
    /// Gets a value indicating whether a slug after the leading digits of the extracted segment is
    /// discarded.
    /// </summary>
    public bool StripSlugAfterDigits { get; init; }

    /// <summary>
    /// Gets the typed prefixes a user may write before an identifier, for the typed-prefix kind.
    /// </summary>
    public IReadOnlyList<string> Prefixes { get; init; } = [];

    /// <summary>
    /// Gets the earliest year accepted by a trailing-year split.
    /// </summary>
    public int? YearLowerBound { get; init; }

    /// <summary>
    /// Gets how far past the current year a trailing-year split still accepts, admitting announced
    /// work.
    /// </summary>
    public int? YearUpperBoundYearsFromNow { get; init; }
}
