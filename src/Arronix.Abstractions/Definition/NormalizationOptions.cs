
namespace Arronix.Abstractions.Definition;

/// <summary>
/// A kind's parameters for the host normalization chain.
/// </summary>
/// <remarks>
/// The chain itself is host code, identical for every kind; what differs per kind is only this data.
/// </remarks>
public sealed record NormalizationOptions
{
    /// <summary>
    /// Gets the options every kind starts from.
    /// </summary>
    public static NormalizationOptions Default { get; } = new();

    /// <summary>
    /// Gets the leading articles stripped when deriving sort and comparison forms.
    /// </summary>
    public IReadOnlyList<string> LeadingArticles { get; init; } = [];

    /// <summary>
    /// Gets the stop words removed when deriving the canonical comparison form.
    /// </summary>
    public IReadOnlyList<string> StopWords { get; init; } = [];

    /// <summary>
    /// Gets the expression that removes stop words, when the word list alone cannot carry the
    /// lookarounds — keeping a stop word that is the whole title, keeping single-letter acronym parts
    /// intact. Null means the plain word list is used.
    /// </summary>
    public string? StopWordExpression { get; init; }

    /// <summary>
    /// Gets reversible transliteration rows applied before diacritic folding, in declared order.
    /// </summary>
    public IReadOnlyList<TransliterationRule> Transliterations { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether remaining diacritics are folded to their base letters.
    /// </summary>
    public bool FoldDiacritics { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether an all-numeric title passes through unchanged rather than being
    /// read as a year or an ordinal.
    /// </summary>
    public bool NumericTitlesPassThrough { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a normalization that consumes the entire text falls back to the
    /// raw form instead of yielding an empty title.
    /// </summary>
    public bool EmptyResultFallsBackToRaw { get; init; } = true;

    /// <summary>
    /// Gets the rewrites that derive the query-text form sent to release sources, in declared order.
    /// </summary>
    public IReadOnlyList<RewriteRule> QueryRewrites { get; init; } = [];
}
