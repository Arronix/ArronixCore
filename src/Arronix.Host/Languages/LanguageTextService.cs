using System.Globalization;
using System.Text;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Languages;

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
            using var held = _definitions.Lease(language);

            if (held is not null)
            {
                Add(keys, held.Value.Language.Code, held.Value.PrepareComparison(text));
            }

            return keys;
        }

        // One using around the whole loop: a definition that throws must not leave the leases of the
        // definitions after it held forever.
        using var definitions = _definitions.LeaseAll();

        foreach (var definition in definitions)
        {
            Add(keys, definition.Language.Code, definition.PrepareComparison(text));
        }

        return keys;
    }

    /// <summary>Builds provider query text using a stated language when one is known.</summary>
    internal string Query(string text, Language? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return CollapseQueryWhitespace(Prepared(text, language, static definition => definition.PrepareQuery));
    }

    /// <summary>Builds language-aware text for a file or folder name.</summary>
    internal string FileName(string text, Language? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Prepared(text, language, static definition => definition.PrepareFileName);
    }

    /// <summary>Builds a sort spelling using a stated language when one is known.</summary>
    internal string Sort(string text, Language? language = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Prepared(text, language, static definition => definition.PrepareSort);
    }

    /// <summary>
    /// Runs one language-owned operation under that language's lease, or returns the text unchanged.
    /// </summary>
    /// <remarks>
    /// The lease is held for the whole call, because the operation is the extension's own code and teardown
    /// must wait for it rather than disposing the definition while it runs.
    /// </remarks>
    private string Prepared(string text, Language? language, Func<ILanguageDefinition, Func<string, string>> select)
    {
        if (language is null)
        {
            return text;
        }

        using var held = _definitions.Lease(language);
        return held is null ? text : select(held.Value)(text);
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
