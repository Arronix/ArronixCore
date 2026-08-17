namespace Arronix.Common.Net;

/// <summary>
/// Validation of URLs supplied as configuration.
/// </summary>
/// <remarks>
/// Thin, and load-bearing anyway. Settings validation is where a mistyped address should be caught, and the
/// framework's own parser is deliberately forgiving in exactly the way that defeats that: it trims
/// surrounding whitespace and accepts the result, so an address pasted from a document with a trailing space
/// is silently repaired at parse time and then fails at connect time with an error naming neither the setting
/// nor the space.
/// </remarks>
public static class UriValidationExtensions
{
    /// <summary>
    /// Determines whether the text is an absolute, well-formed URL fit to be stored as a setting.
    /// </summary>
    /// <param name="value">The text to test. May be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the text is an absolute URL with no surrounding whitespace; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Surrounding whitespace is rejected rather than trimmed. The value the operator typed is the value the
    /// platform stores, so accepting one that needed repair would mean storing something they did not write,
    /// and rejecting it puts the error where it can be corrected.
    /// </remarks>
    public static bool IsValidUrl(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsWellFormedOriginalString();
    }
}
