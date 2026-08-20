
using System.Collections.Frozen;
using System;
using Arronix.Abstractions.Languages;
using TextLanguage = Arronix.Abstractions.DTOs.Language;

namespace Arronix.Languages.Reference;

/// <summary>French title equivalence and query spelling.</summary>
public sealed class FrenchLanguageDefinition : ILanguageDefinition
{
    private static readonly FrozenSet<string> InternalStopWords =
        new[] { "à" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TextLanguage Language { get; } = new("fr", "French");

    /// <inheritdoc />
    public string PrepareComparison(string text) => LanguageText.WithoutInternalWords(text, InternalStopWords);

    /// <inheritdoc />
    public string PrepareQuery(string text) => LanguageText.QueryText(text, "et");

    /// <inheritdoc />
    public string PrepareFileName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("&", "et", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public string PrepareSort(string text) => text;
}
