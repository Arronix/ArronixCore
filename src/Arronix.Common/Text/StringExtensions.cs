using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Arronix.Common.Text;

/// <summary>
/// The handful of string operations the base class library still does not supply.
/// </summary>
/// <remarks>
/// <para>
/// Membership here is deliberately hard to earn. Every member the platform inherited that merely restated a
/// framework method — null-or-whitespace predicates, case-insensitive comparisons, hexadecimal conversion,
/// reversal, formatting sugar — has been dropped in favor of the framework, because an extension that
/// shadows a framework method costs every reader a lookup to discover it does nothing extra, and in the case
/// of the comparison helpers it silently chose culture-sensitive comparison for paths, headers and protocol
/// tokens that all mean ordinal.
/// </para>
/// <para>
/// The type has no static constructor. The version this replaces registered a global text-folding table as a
/// side effect of the first touch of any string extension anywhere in the process, which made behavior
/// depend on initialization order and made the folding table impossible to substitute. Folding that is
/// language-specific belongs to the naming seam instead; what is done here is only the canonical Unicode
/// decomposition described on <see cref="ToUrlSlug"/>.
/// </para>
/// </remarks>
public static partial class StringExtensions
{
    /// <summary>
    /// Returns the text with its first character lowercased, using invariant casing rules.
    /// </summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>
    /// The text with a lowercase first character; the empty string when <paramref name="value"/> is empty.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string FirstCharToLower(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0 || char.IsLower(value[0]))
        {
            return value;
        }

        return string.Create(
            value.Length,
            value,
            static (destination, source) =>
            {
                source.CopyTo(destination);
                destination[0] = char.ToLowerInvariant(destination[0]);
            });
    }

    /// <summary>
    /// Returns the text with its first character uppercased, using invariant casing rules.
    /// </summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>
    /// The text with an uppercase first character; the empty string when <paramref name="value"/> is empty.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string FirstCharToUpper(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0 || char.IsUpper(value[0]))
        {
            return value;
        }

        return string.Create(
            value.Length,
            value,
            static (destination, source) =>
            {
                source.CopyTo(destination);
                destination[0] = char.ToUpperInvariant(destination[0]);
            });
    }

    /// <summary>
    /// Inserts a space before every capital letter that is not the first character, turning an identifier
    /// into something readable.
    /// </summary>
    /// <param name="value">The identifier to split.</param>
    /// <returns>The identifier with interior capitals preceded by a space.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <c>"HttpRequestTimeout".SplitCamelCase()</c> yields <c>"Http Request Timeout"</c>.
    /// </example>
    public static string SplitCamelCase(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return InteriorCapitalRegex().Replace(value, " $0");
    }

    /// <summary>
    /// Removes a trailing suffix if it is present, leaving the text untouched otherwise.
    /// </summary>
    /// <param name="value">The text to trim.</param>
    /// <param name="suffix">The suffix to remove.</param>
    /// <param name="comparison">
    /// How the suffix is matched. The default is ordinal, because the callers are paths, file names and
    /// protocol tokens, none of which are compared under a culture's collation rules.
    /// </param>
    /// <returns>The text without the suffix, or the original text when it does not end with the suffix.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="value"/> or <paramref name="suffix"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Named for what it does. The member this replaces was called <c>TrimEnd</c>, which put a
    /// culture-sensitive, single-suffix method one dot away from <see cref="string.TrimEnd(char[])"/> — a
    /// framework method with entirely different semantics and the same name.
    /// </remarks>
    public static string TrimSuffix(
        this string value,
        string suffix,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(suffix);

        if (suffix.Length == 0 || !value.EndsWith(suffix, comparison))
        {
            return value;
        }

        return value[..^suffix.Length];
    }

    /// <summary>
    /// Wraps the text in double quotes when it contains a space, so that it survives being placed on a
    /// command line as a single argument.
    /// </summary>
    /// <param name="value">The text to quote.</param>
    /// <returns>The quoted text, or the original text when no space is present.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string WrapInQuotes(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Contains(' ') ? string.Concat("\"", value, "\"") : value;
    }

    /// <summary>
    /// Reduces the text to a lowercase, hyphen-separated token safe to place in a URL path.
    /// </summary>
    /// <param name="value">The text to convert.</param>
    /// <returns>
    /// A token containing only <c>a</c>–<c>z</c>, <c>0</c>–<c>9</c>, hyphen and underscore, with no leading,
    /// trailing or repeated separators. The empty string when nothing survives.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Accents are removed by canonical Unicode decomposition, so an accented letter becomes its unaccented
    /// base letter rather than disappearing. This is deliberately the plain, language-neutral transform and
    /// deliberately not extensible: a slug is an identifier, and an identifier whose spelling depended on
    /// which optional components happened to be installed would not be stable across two installations of
    /// the same platform. Extensible, language-aware folding exists separately, for display text.
    /// </para>
    /// <para>
    /// Text that is not valid Unicode — an unpaired surrogate, for instance — cannot be decomposed; it is
    /// then reduced without the accent-removal step rather than being rejected, because a slug is never
    /// important enough to fail an operation over.
    /// </para>
    /// </remarks>
    public static string ToUrlSlug(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
        {
            return string.Empty;
        }

        var folded = RemoveCanonicalAccents(value.ToLowerInvariant());
        var separated = WhitespaceRegex().Replace(folded, "-");
        var stripped = NonSlugCharacterRegex().Replace(separated, string.Empty);
        var collapsed = RepeatedSeparatorRegex().Replace(stripped, "-");

        return collapsed.Trim('-', '_');
    }

    /// <summary>
    /// Strips the combining marks a canonical decomposition exposes, mapping accented letters onto their
    /// base letters.
    /// </summary>
    /// <param name="value">The text to fold.</param>
    /// <returns>The folded text, or the input unchanged when it cannot be normalized.</returns>
    private static string RemoveCanonicalAccents(string value)
    {
        string decomposed;

        try
        {
            decomposed = value.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            // The text is not well-formed Unicode. Nothing can be decomposed, and the character filter
            // below removes everything outside the ASCII slug alphabet anyway.
            return value;
        }

        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex("(?<!^)[A-Z]")]
    private static partial Regex InteriorCapitalRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("[^a-z0-9_-]")]
    private static partial Regex NonSlugCharacterRegex();

    [GeneratedRegex("[-_]{2,}")]
    private static partial Regex RepeatedSeparatorRegex();
}
