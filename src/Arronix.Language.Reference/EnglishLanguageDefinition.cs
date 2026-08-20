
using System.Collections.Frozen;
using System;
using Arronix.Abstractions.Languages;
using TextLanguage = Arronix.Abstractions.DTOs.Language;

namespace Arronix.Languages.Reference;

/// <summary>English title equivalence, query spelling and article sorting.</summary>
public sealed class EnglishLanguageDefinition : ILanguageDefinition
{
    private static readonly FrozenSet<string> Articles =
        new[] { "a", "an", "the" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> InternalStopWords =
        new[] { "a", "an", "the", "and", "or", "of" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TextLanguage Language => TextLanguage.English;

    /// <inheritdoc />
    public string PrepareComparison(string text)
        => LanguageText.WithoutInternalWords(text, InternalStopWords, protectSingleLetterRuns: true);

    /// <inheritdoc />
    public string PrepareQuery(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = LanguageText.Words(text);
        var withoutLeadingArticle = words.Count > 1
            && string.Equals(words[0], "the", StringComparison.OrdinalIgnoreCase)
                ? text[(text.IndexOf(words[0], StringComparison.OrdinalIgnoreCase) + words[0].Length)..].TrimStart()
                : text;

        return LanguageText.QueryText(withoutLeadingArticle, "and");
    }

    /// <inheritdoc />
    public string PrepareFileName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("&", "and", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public string PrepareSort(string text) => LanguageText.MoveLeadingArticle(text, Articles);
}
