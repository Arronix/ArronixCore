// The shape contracts are experimental until 1.0.
#pragma warning disable ARX0013

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// Renders a <see cref="FieldValue"/> as naming-token text, invariantly.
/// </summary>
internal static class FieldValueText
{
    /// <summary>
    /// Renders a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text, empty for absent values.</returns>
    public static string Render(FieldValue? value)
    {
        if (value is null || value.IsAbsent)
        {
            return string.Empty;
        }

        if (value.Items is { Count: > 0 } items)
        {
            return string.Join(", ", items.Select(Render).Where(text => text.Length > 0));
        }

        return value switch
        {
            { Text: { Length: > 0 } text } => text,
            { Number: { } number } => number.ToString(CultureInfo.InvariantCulture),
            { Real: { } real } => real.ToString(CultureInfo.InvariantCulture),
            { Flag: { } flag } => flag ? "true" : "false",
            { Date: { } date } => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            { Instant: { } instant } => instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            { Duration: { } duration } => Math.Round(duration.TotalMinutes).ToString(CultureInfo.InvariantCulture),
            { Ordinals: { } ordinals } => ordinals.ToString(),
            { External: { } external } => external.Value,
            { Link: { } link } => link.ToString(),
            { Quality: { } quality } => quality.Name,
            { Language: { } language } => language.Name,
            _ => string.Empty,
        };
    }
}
