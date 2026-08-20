using System;
using System.Collections.Generic;
using System.Text;

namespace Arronix.Languages.Reference;

/// <summary>Small invariant mechanics shared by the language-owned implementations in this assembly.</summary>
internal static class LanguageText
{
    internal static IReadOnlyList<string> Words(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || (character is '\'' or '’' && word.Length > 0))
            {
                word.Append(character);
                continue;
            }

            Complete(word, words);
        }

        Complete(word, words);
        return words;
    }

    internal static string WithoutInternalWords(
        string text,
        IReadOnlySet<string> omitted,
        bool protectSingleLetterRuns = false)
    {
        var words = Words(text);
        if (words.Count < 3)
        {
            return text;
        }

        var kept = new List<string>(words.Count) { words[0] };

        for (var index = 1; index < words.Count - 1; index++)
        {
            var candidate = words[index];
            var protectedRun = protectSingleLetterRuns
                && candidate.Length == 1
                && (words[index - 1].Length == 1 || words[index + 1].Length == 1);

            if (protectedRun || !omitted.Contains(candidate))
            {
                kept.Add(candidate);
            }
        }

        kept.Add(words[^1]);
        return string.Join(' ', kept);
    }

    internal static string QueryText(string text, string conjunction)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (character == '&')
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                builder.Append(conjunction);
                pendingSpace = true;
            }
            else if (character is '\'' or '.' or '`' or '´' or '‘' or '’')
            {
                continue;
            }
            else if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(character);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = builder.Length > 0;
            }
        }

        return builder.ToString().Trim();
    }

    internal static string MoveLeadingArticle(string text, IReadOnlySet<string> articles)
    {
        var words = Words(text);
        if (words.Count < 2 || !articles.Contains(words[0]))
        {
            return text;
        }

        var first = words[0];
        var start = text.IndexOf(first, StringComparison.OrdinalIgnoreCase);
        var remainder = text[(start + first.Length)..].TrimStart();
        return $"{remainder}, {text.AsSpan(start, first.Length)}";
    }

    internal static string ReplaceCharacters(string text, IReadOnlyDictionary<char, string> replacements)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(replacements.TryGetValue(character, out var replacement) ? replacement : character);
        }

        return builder.ToString();
    }

    private static void Complete(StringBuilder word, ICollection<string> words)
    {
        if (word.Length == 0)
        {
            return;
        }

        words.Add(word.ToString());
        word.Clear();
    }

}
