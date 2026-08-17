using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arronix.Common.Serialization.Converters;

/// <summary>
/// Reads and writes <see cref="DateTime"/> as an instant in UTC, to the second.
/// </summary>
/// <remarks>
/// <para>
/// The built-in handling is faithful rather than normalizing: a local value is written with its offset, a
/// value with no kind is written with no marker at all, and sub-second precision is preserved. Faithful is
/// the wrong answer for a platform whose payloads cross machines — two hosts in different zones then write
/// two different strings for the same instant, and a value with no marker means whatever the machine that
/// reads it happens to think. This converter collapses all of that: everything on the wire is UTC, and a
/// string with no zone is read as UTC rather than as the reader's local time.
/// </para>
/// <para>
/// Precision stops at the second, deliberately. Sub-second digits do not survive most of the stores and
/// remote APIs these payloads pass through, and a value that silently loses precision somewhere in the
/// middle compares unequal to itself. Losing it here, once, is the honest version. Values that genuinely
/// need higher resolution belong in a type that says so.
/// </para>
/// <para>
/// New payload types should prefer <see cref="DateTimeOffset"/>, which carries its own zone and needs no
/// converter at all. This one exists for the values that are already <see cref="DateTime"/>.
/// </para>
/// </remarks>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    /// <summary>
    /// ISO 8601, whole seconds, explicit zulu marker. Every literal is quoted so that no part of it is
    /// reinterpreted as a format specifier.
    /// </summary>
    private const string WireFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'";

    /// <summary>
    /// A string with no zone is an instant in UTC, not an instant in whatever zone the reading machine is
    /// configured for. Combined with <see cref="DateTimeStyles.AdjustToUniversal"/> this yields a value that
    /// is the same on every machine, which is the only property that makes a stored timestamp comparable.
    /// </summary>
    private const DateTimeStyles ReadStyles =
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces;

    /// <summary>
    /// Reads a timestamp and converts it to UTC.
    /// </summary>
    /// <param name="reader">The reader positioned on the value.</param>
    /// <param name="typeToConvert">The requested type.</param>
    /// <param name="options">The options in force.</param>
    /// <returns>The instant, with <see cref="DateTime.Kind"/> set to <see cref="DateTimeKind.Utc"/>.</returns>
    /// <exception cref="JsonException">
    /// The value is not a string, or is a string no date can be read from.
    /// </exception>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a timestamp written as a string, found {reader.TokenType}.");
        }

        // The reader's own ISO 8601 path is strict, allocation-free and independent of any culture, so it
        // gets first refusal on the shape virtually every payload actually uses.
        if (reader.TryGetDateTime(out var parsed))
        {
            return ToUtc(parsed);
        }

        var text = reader.GetString();

        // Anything else is parsed against the invariant culture. Parsing against the ambient culture — the
        // legacy behavior — makes the result depend on the machine's regional settings, and under a culture
        // with a non-Gregorian calendar it does not merely reformat the value, it reinterprets it.
        if (text is not null &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture, ReadStyles, out var fallback))
        {
            return ToUtc(fallback);
        }

        throw new JsonException($"'{text}' is not a timestamp this platform can read.");
    }

    /// <summary>
    /// Writes a timestamp as an ISO 8601 instant in UTC, to the second.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The options in force.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Formatting against the ambient culture is the same defect as parsing against it, and it is worse
        // on this side because it is silent: under a culture whose calendar is not Gregorian the year comes
        // out as a different number entirely, and the result is still a well-formed-looking timestamp.
        writer.WriteStringValue(ToUtc(value).ToString(WireFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Interprets a value as an instant in UTC.
    /// </summary>
    /// <param name="value">The value to interpret.</param>
    /// <returns>The same instant, expressed in UTC.</returns>
    /// <remarks>
    /// A value with no kind is taken to already be UTC, matching how such a string is read. Treating it as
    /// local — which is what the framework's own conversion does — would shift the value by the machine's
    /// offset on the way out and not shift it back on the way in, so a value would drift by one offset per
    /// round trip.
    /// </remarks>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime(),
    };
}
