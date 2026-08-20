
using System.Collections.Frozen;
using System;
using System.Collections.Generic;
using Arronix.Abstractions.Languages;
using TextLanguage = Arronix.Abstractions.DTOs.Language;

namespace Arronix.Languages.Reference;

/// <summary>German title transliteration and article sorting.</summary>
public sealed class GermanLanguageDefinition : ILanguageDefinition
{
    private static readonly FrozenDictionary<char, string> Transliteration =
        new Dictionary<char, string>
        {
            ['ä'] = "ae",
            ['ö'] = "oe",
            ['ü'] = "ue",
            ['Ä'] = "Ae",
            ['Ö'] = "Oe",
            ['Ü'] = "Ue",
            ['ß'] = "ss",
        }.ToFrozenDictionary();

    private static readonly FrozenSet<string> Articles =
        new[] { "der", "die", "das", "ein", "eine" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TextLanguage Language { get; } = new("de", "German");

    /// <inheritdoc />
    public string PrepareComparison(string text) => LanguageText.ReplaceCharacters(text, Transliteration);

    /// <inheritdoc />
    public string PrepareQuery(string text) => LanguageText.QueryText(text, "und");

    /// <inheritdoc />
    public string PrepareFileName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("&", "und", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public string PrepareSort(string text) => LanguageText.MoveLeadingArticle(text, Articles);
}
