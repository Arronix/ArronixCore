
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Rendering;

/// <summary>
/// Converts a declared value to and from the text a person edits it as.
/// </summary>
/// <remarks>
/// <para>
/// Editing happens in text because that is the one form every declared value shape has and every input
/// carries. Everything the platform takes from a user is already text — an action's parameters and a
/// provider's settings are both string maps — so a working surface that edited typed values would be the
/// only place in the client that did.
/// </para>
/// <para>
/// Reading is forgiving and writing is exact: text that does not parse yields an absent value rather than
/// throwing, because a half-typed number is a normal state of an input and not an error worth a stack
/// trace.
/// </para>
/// </remarks>
public static class FieldValueText
{
    /// <summary>
    /// Renders a value as the text it is edited as.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text, empty when the value is absent.</returns>
    public static string ToEditText(FieldValue? value)
    {
        if (value is null || value.IsAbsent)
        {
            return string.Empty;
        }

        if (value.Items is { Count: > 0 } items)
        {
            return string.Join(", ", items.Select(ToEditText));
        }

        return value.Kind switch
        {
            FieldValueKind.Text => value.Text ?? string.Empty,
            FieldValueKind.MultilineText => value.Text ?? string.Empty,
            FieldValueKind.Integer => Invariant(value.Number),
            FieldValueKind.Decimal => Invariant(value.Real),
            FieldValueKind.Boolean => value.Flag == true ? "true" : "false",
            FieldValueKind.Date => value.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            FieldValueKind.Instant => value.Instant?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            FieldValueKind.Duration => value.Duration?.ToString("c", CultureInfo.InvariantCulture) ?? string.Empty,
            FieldValueKind.ByteSize => Invariant(value.Number),
            FieldValueKind.Ratio => Invariant(value.Real),
            FieldValueKind.Ordinal => value.Ordinals?.ToString() ?? string.Empty,
            FieldValueKind.Enumerated => value.Text ?? string.Empty,
            // Components are joined above; a component-less composite edits as nothing.
            FieldValueKind.Composite => string.Empty,
            FieldValueKind.Reference => value.Reference?.ToString() ?? string.Empty,
            FieldValueKind.ExternalIdentifier => value.External?.ToString() ?? string.Empty,
            FieldValueKind.Link => value.Link?.ToString() ?? string.Empty,
            FieldValueKind.FilePath => value.Text ?? string.Empty,
            FieldValueKind.Language => value.Language?.Code ?? string.Empty,
            FieldValueKind.Quality => value.Quality?.Name ?? string.Empty,
            FieldValueKind.Artwork => value.Link?.ToString() ?? string.Empty,
            FieldValueKind.Count => Invariant(value.Number),
        };
    }

    /// <summary>
    /// Reads edited text back into a value of a declared shape.
    /// </summary>
    /// <param name="kind">The shape the value must carry.</param>
    /// <param name="text">The text as the user left it.</param>
    /// <returns>The value, absent when the text does not name one.</returns>
    public static FieldValue FromEditText(FieldValueKind kind, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return FieldValue.Absent(kind);
        }

        var trimmed = text.Trim();

        return kind switch
        {
            FieldValueKind.Text => FieldValue.OfText(trimmed),
            FieldValueKind.MultilineText => FieldValue.OfMultilineText(text),
            FieldValueKind.Integer => TryLong(trimmed, out var integer)
                ? FieldValue.OfInteger(integer)
                : FieldValue.Absent(kind),
            FieldValueKind.Decimal => TryDouble(trimmed, out var real)
                ? FieldValue.OfDecimal(real)
                : FieldValue.Absent(kind),
            FieldValueKind.Boolean => FieldValue.OfBoolean(
                string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)),
            FieldValueKind.Date => DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, out var date)
                ? FieldValue.OfDate(date)
                : FieldValue.Absent(kind),
            FieldValueKind.Instant => DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var instant)
                ? FieldValue.OfInstant(instant)
                : FieldValue.Absent(kind),
            FieldValueKind.Duration => TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var duration)
                ? FieldValue.OfDuration(duration)
                : FieldValue.Absent(kind),
            FieldValueKind.ByteSize => TryLong(trimmed, out var bytes)
                ? FieldValue.OfByteSize(bytes)
                : FieldValue.Absent(kind),
            FieldValueKind.Ratio => TryDouble(trimmed, out var ratio)
                ? FieldValue.OfRatio(ratio)
                : FieldValue.Absent(kind),
            FieldValueKind.Ordinal => OrdinalPath.TryParse(trimmed, out var ordinals)
                ? FieldValue.OfOrdinal(ordinals)
                : FieldValue.Absent(kind),
            FieldValueKind.Enumerated => FieldValue.OfEnumerated(trimmed),
            // A composite is edited through its components, never as one text box.
            FieldValueKind.Composite => FieldValue.Absent(kind),
            FieldValueKind.Reference => FieldValue.Absent(kind),
            FieldValueKind.ExternalIdentifier => ExternalId.TryParse(trimmed, out var external)
                ? FieldValue.OfExternalIdentifier(external)
                : FieldValue.Absent(kind),
            FieldValueKind.Link => Uri.TryCreate(trimmed, UriKind.Absolute, out var link)
                ? FieldValue.OfLink(link)
                : FieldValue.Absent(kind),
            FieldValueKind.FilePath => FieldValue.OfFilePath(trimmed),
            FieldValueKind.Language => FieldValue.OfLanguage(new Language(trimmed, trimmed)),
            FieldValueKind.Quality => FieldValue.OfQuality(new QualityTier(trimmed, 0)),
            FieldValueKind.Artwork => Uri.TryCreate(trimmed, UriKind.Absolute, out var artwork)
                ? FieldValue.OfArtwork(artwork)
                : FieldValue.Absent(kind),
            FieldValueKind.Count => TryLong(trimmed, out var count)
                ? FieldValue.OfCount(count)
                : FieldValue.Absent(kind),
        };
    }

    /// <summary>
    /// Gets whether a value of this shape can be produced from text a user typed.
    /// </summary>
    /// <param name="kind">The value shape.</param>
    /// <returns>
    /// <see langword="false"/> for a reference, which names another item and can only be chosen from a
    /// set the platform supplies.
    /// </returns>
    public static bool IsEditableAsText(FieldValueKind kind)
        => kind is not (FieldValueKind.Reference or FieldValueKind.Composite);

    private static string Invariant(long? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Invariant(double? value)
        => value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool TryLong(string text, out long value)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
