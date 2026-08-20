using System.Globalization;
using System.Text;
using Arronix.Abstractions.DTOs;

namespace Arronix.Host.Languages;

/// <summary>Composes invariant text mechanics with the installed language-owned operations.</summary>
internal sealed class LanguageTextService(LanguageDefinitionRegistry definitions)
{
    private readonly LanguageDefinitionRegistry _definitions =
        definitions ?? throw new ArgumentNullException(nameof(definitions));

    /// <summary>Builds comparison keys, keeping each language's equivalence space distinct.</summary>
    internal IReadOnlySet<string> ComparisonKeys(string text, Language? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        Add(keys, "und", text);

        if (language is not null)
        {
            if (_definitions.Find(language) is { } definition)
            {
                Add(keys, definition.Language.Code, definition.PrepareComparison(text));
            }

            return keys;
        }

        foreach (var definition in _definitions.All)
        {
            Add(keys, definition.Language.Code, definition.PrepareComparison(text));
        }

        return keys;
    }

    /// <summary>Builds provider query text using a stated language when one is known.</summary>
    internal string Query(string text, Language? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var prepared = language is not null && _definitions.Find(language) is { } definition
            ? definition.PrepareQuery(text)
            : text;

        return CollapseQueryWhitespace(prepared);
    }

    /// <summary>Builds language-aware text for a file or folder name.</summary>
    internal string FileName(string text, Language? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return language is not null && _definitions.Find(language) is { } definition
            ? definition.PrepareFileName(text)
            : text;
    }

    /// <summary>Builds a sort spelling using a stated language when one is known.</summary>
    internal string Sort(string text, Language? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return language is not null && _definitions.Find(language) is { } definition
            ? definition.PrepareSort(text)
            : text;
    }

    private static void Add(ISet<string> keys, string language, string text)
    {
        var key = InvariantComparisonKey(text);
        if (key.Length > 0)
        {
            keys.Add($"{language.ToLowerInvariant()}:{key}");
        }
    }

    private static string InvariantComparisonKey(string text)
    {
        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            decomposed = text;
        }

        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string CollapseQueryWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString().TrimEnd();
    }
}
