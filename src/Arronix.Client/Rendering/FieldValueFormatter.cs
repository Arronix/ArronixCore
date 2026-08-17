#pragma warning disable ARX0013 // Shape contracts are experimental; field values are what this file reads.

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Rendering;

/// <summary>
/// Renders a declared value as text a person can read.
/// </summary>
/// <remarks>
/// <para>
/// The reason the contract layer carries a tagged value rather than a bag of strings is here: a size, a
/// duration, a date and a proportion each want formatting for the reader, and none of that is possible
/// once the type has been thrown away at the server. Every branch below is a formatting decision this
/// client owns; the extension supplied only the number and what kind of number it is.
/// </para>
/// <para>
/// Absent is not empty. A field the item has no value for reads as a dash, because an empty string in a
/// column reads as a value that happens to be blank.
/// </para>
/// </remarks>
public static class FieldValueFormatter
{
    private const string AbsentMarker = "—";

    /// <summary>
    /// Renders a value for display.
    /// </summary>
    /// <param name="descriptor">What the field is, which supplies its choices and its unit.</param>
    /// <param name="value">The value, which may be absent.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    public static string Format(FieldDescriptor descriptor, FieldValue? value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (value is null || value.IsAbsent)
        {
            return AbsentMarker;
        }

        if (value.Items is { Count: > 0 } items)
        {
            return string.Join(", ", items.Select(item => Format(descriptor, item)));
        }

        if (value.Items is { Count: 0 })
        {
            return AbsentMarker;
        }

        var text = FormatScalar(descriptor, value);

        return string.IsNullOrEmpty(descriptor.Unit) || text == AbsentMarker
            ? text
            : $"{text} {descriptor.Unit}";
    }

    /// <summary>
    /// Renders a value with no field description to hand, for a working-surface cell or a summary line.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string Format(FieldValue? value)
        => Format(Anonymous(value?.Kind ?? FieldValueKind.Text), value);

    private static FieldDescriptor Anonymous(FieldValueKind kind)
        => new() { FieldId = "value", Name = "Value", ValueKind = kind };

    private static string FormatScalar(FieldDescriptor descriptor, FieldValue value) => value.Kind switch
    {
        FieldValueKind.Text => value.Text ?? AbsentMarker,
        FieldValueKind.MultilineText => value.Text ?? AbsentMarker,
        FieldValueKind.Integer => value.Number?.ToString("N0", CultureInfo.CurrentCulture) ?? AbsentMarker,
        FieldValueKind.Decimal => value.Real?.ToString("0.##", CultureInfo.CurrentCulture) ?? AbsentMarker,
        FieldValueKind.Boolean => value.Flag is { } flag ? (flag ? "Yes" : "No") : AbsentMarker,
        FieldValueKind.Date => value.Date?.ToString("d", CultureInfo.CurrentCulture) ?? AbsentMarker,
        FieldValueKind.Instant => value.Instant?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            ?? AbsentMarker,
        FieldValueKind.Duration => value.Duration is { } duration ? FormatDuration(duration) : AbsentMarker,
        FieldValueKind.ByteSize => value.Number is { } bytes ? FormatBytes(bytes) : AbsentMarker,
        FieldValueKind.Ratio => value.Real is { } ratio
            ? ratio.ToString("P0", CultureInfo.CurrentCulture)
            : AbsentMarker,
        FieldValueKind.Ordinal => value.Ordinals is { } ordinals && ordinals.Length > 0
            ? ordinals.ToString()
            : AbsentMarker,
        FieldValueKind.Enumerated => ChoiceName(descriptor, value.Text),
        // A composite carries its parts in Items, which Format handles before reaching here; a composite
        // with no parts has nothing to show.
        FieldValueKind.Composite => AbsentMarker,
        FieldValueKind.Reference => value.Reference?.ToString() ?? AbsentMarker,
        FieldValueKind.ExternalIdentifier => value.External?.ToString() ?? AbsentMarker,
        FieldValueKind.Link => value.Link?.ToString() ?? AbsentMarker,
        FieldValueKind.FilePath => value.Text ?? AbsentMarker,
        FieldValueKind.Language => value.Language?.Name ?? AbsentMarker,
        FieldValueKind.Quality => value.Quality?.Name ?? AbsentMarker,
        FieldValueKind.Artwork => value.Link?.ToString() ?? AbsentMarker,
        FieldValueKind.Count => value.Number?.ToString("N0", CultureInfo.CurrentCulture) ?? AbsentMarker,
    };

    private static string ChoiceName(FieldDescriptor descriptor, string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return AbsentMarker;
        }

        foreach (var choice in descriptor.Choices)
        {
            if (string.Equals(choice.Value, stored, StringComparison.Ordinal))
            {
                return choice.Name;
            }
        }

        return stored;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration >= TimeSpan.FromDays(1))
        {
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{(int)duration.TotalDays}d {duration.Hours}h");
        }

        if (duration >= TimeSpan.FromHours(1))
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours}h {duration.Minutes}m");
        }

        return duration >= TimeSpan.FromMinutes(1)
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds}s")
            : string.Create(CultureInfo.CurrentCulture, $"{duration.TotalSeconds:0.#}s");
    }

    /// <summary>
    /// Renders a size in bytes at a scale a person reads.
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The text.</returns>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var magnitude = 0;
        double size = bytes;

        while (Math.Abs(size) >= 1024 && magnitude < units.Length - 1)
        {
            size /= 1024;
            magnitude++;
        }

        return magnitude == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes:N0} B")
            : string.Create(CultureInfo.CurrentCulture, $"{size:0.#} {units[magnitude]}");
    }
}
