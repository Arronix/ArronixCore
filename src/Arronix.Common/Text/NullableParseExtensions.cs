using System.Globalization;

namespace Arronix.Common.Text;

/// <summary>
/// Parses text into a nullable value instead of a value plus a success flag.
/// </summary>
/// <remarks>
/// <para>
/// The framework's try-parse pattern needs a statement and an out parameter, which cannot appear inside an
/// expression. A parse that yields <see langword="null"/> on failure composes with query operators, null
/// coalescing and pattern matching, so text arriving from a remote server can be turned into a value and
/// defaulted on one line rather than four.
/// </para>
/// <para>
/// Every member parses with the invariant culture. Text reaching these methods comes from feeds, headers and
/// configuration files written elsewhere, so interpreting it under whatever locale the host happens to run in
/// would make the platform's behavior depend on the machine it was installed on.
/// </para>
/// </remarks>
public static class NullableParseExtensions
{
    /// <summary>
    /// The number shape used for the integral parsers: an optional sign and digits, with surrounding
    /// whitespace tolerated.
    /// </summary>
    private const NumberStyles IntegerStyles = NumberStyles.Integer;

    /// <summary>
    /// The number shape used for the fractional parser: sign, digits, group separators, a decimal point and
    /// an exponent.
    /// </summary>
    private const NumberStyles DecimalStyles = NumberStyles.Float | NumberStyles.AllowThousands;

    /// <summary>
    /// Number formatting in which a comma is the decimal separator and a dot groups thousands, used to read
    /// values produced by the parts of the world that write them that way.
    /// </summary>
    private static readonly NumberFormatInfo CommaDecimalFormat = NumberFormatInfo.ReadOnly(
        new NumberFormatInfo
        {
            NumberDecimalSeparator = ",",
            NumberGroupSeparator = ".",
        });

    /// <summary>
    /// Parses a 32-bit integer.
    /// </summary>
    /// <param name="value">The text to parse. May be <see langword="null"/>.</param>
    /// <returns>The parsed value, or <see langword="null"/> when the text is not a 32-bit integer.</returns>
    public static int? ParseInt32(this string? value) =>
        int.TryParse(value, IntegerStyles, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>
    /// Parses a 64-bit integer.
    /// </summary>
    /// <param name="value">The text to parse. May be <see langword="null"/>.</param>
    /// <returns>The parsed value, or <see langword="null"/> when the text is not a 64-bit integer.</returns>
    public static long? ParseInt64(this string? value) =>
        long.TryParse(value, IntegerStyles, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>
    /// Parses a double-precision number, accepting either a dot or a comma as the decimal separator.
    /// </summary>
    /// <param name="value">The text to parse. May be <see langword="null"/>.</param>
    /// <returns>The parsed value, or <see langword="null"/> when the text is not a number.</returns>
    /// <remarks>
    /// <para>
    /// Which character is the decimal separator is decided from the text itself, not by rewriting it. When
    /// both a dot and a comma are present the dot is the decimal separator and the comma groups thousands;
    /// when only a comma is present it is read as the decimal separator, which is how most of the world
    /// writes it.
    /// </para>
    /// <para>
    /// The implementation this replaces substituted every comma with a dot before parsing, so a perfectly
    /// ordinary <c>"1,234.5"</c> became <c>"1.234.5"</c> and failed to parse at all — the platform read
    /// nothing rather than a wrong number, which is how the defect survived. Feed sizes and rates arrive
    /// grouped like that routinely.
    /// </para>
    /// </remarks>
    public static double? ParseDouble(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var commaIsDecimalSeparator = value.Contains(',') && !value.Contains('.');

        var format = commaIsDecimalSeparator
            ? CommaDecimalFormat
            : NumberFormatInfo.InvariantInfo;

        return double.TryParse(value, DecimalStyles, format, out var result) ? result : null;
    }
}
