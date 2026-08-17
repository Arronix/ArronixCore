// The shape (ARX0013) and definition (ARX0019) contracts are experimental until 1.0.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

using System.Globalization;
using System.Linq;
using System.Text.Json;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Metadata;

/// <summary>
/// The closed converter set a <c>ResponseMapRow</c> may name. An unknown converter is a load failure,
/// never a silent pass-through.
/// </summary>
/// <remarks>
/// <para>
/// The set covers what the four reference catalogers actually convert
/// (<c>Arronix.Plugin.Movies/Providers/MoviesCataloger.cs</c>, <c>MoviesCatalogMapper.MapFields</c>):
/// numbers, dates, minutes-to-duration, language codes, absolute addresses, counts, and the two
/// multivalue postures (<c>distinct</c> and <c>keep-empty</c>). The form <c>&lt;scheme&gt;-id</c>
/// converts through the declaration's identifier rules, which is how <c>imdb-id</c> restores the
/// <c>tt</c> prefix and zero-pads without a bespoke converter per scheme.
/// </para>
/// <para>
/// Growing this list is a host release by design (<c>declarative-media-kinds.md</c> §2.8): the
/// at-scale precedent needed embedded conditionals exactly here, so the closed set is a measured
/// budget and the escape is an integration plugin.
/// </para>
/// </remarks>
internal sealed class CatalogValueConverters
{
    private static readonly string[] KnownConverters =
    [
        "int", "decimal", "count", "date", "minutes", "language", "absolute-uri", "distinct", "keep-empty",
    ];

    private readonly CatalogIdRules _idRules;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogValueConverters"/> class.
    /// </summary>
    /// <param name="idRules">The declaration's identifier rules, backing the id converters.</param>
    public CatalogValueConverters(CatalogIdRules idRules)
    {
        ArgumentNullException.ThrowIfNull(idRules);
        _idRules = idRules;
    }

    /// <summary>
    /// Answers whether a converter name is part of the vocabulary.
    /// </summary>
    /// <param name="converter">The name, or null for pass-through.</param>
    /// <returns>Whether the row is executable.</returns>
    public static bool IsKnown(string? converter) =>
        converter is null
        || KnownConverters.Contains(converter, StringComparer.OrdinalIgnoreCase)
        || converter.EndsWith("-id", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts the matched elements of one row into a field value.
    /// </summary>
    /// <param name="matches">The elements the row's path found.</param>
    /// <param name="converter">The row's converter, or null for text pass-through.</param>
    /// <returns>The field value, or null when nothing usable matched.</returns>
    public FieldValue? Convert(IReadOnlyList<JsonElement> matches, string? converter)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1
            || string.Equals(converter, "distinct", StringComparison.OrdinalIgnoreCase)
            || string.Equals(converter, "keep-empty", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertMany(matches, converter);
        }

        return ConvertOne(matches[0], converter);
    }

    private FieldValue? ConvertMany(IReadOnlyList<JsonElement> matches, string? converter)
    {
        var keepEmpty = string.Equals(converter, "keep-empty", StringComparison.OrdinalIgnoreCase);
        var distinct = string.Equals(converter, "distinct", StringComparison.OrdinalIgnoreCase);
        var elementConverter = keepEmpty || distinct ? null : converter;

        var items = new List<FieldValue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in matches)
        {
            var text = JsonPathReader.Text(match) ?? string.Empty;

            if (text.Length == 0 && !keepEmpty)
            {
                continue;
            }

            if (distinct && !seen.Add(text))
            {
                continue;
            }

            var item = ConvertOne(match, elementConverter);

            if (item is not null)
            {
                items.Add(item);
            }
            else if (keepEmpty)
            {
                // The position-correlated multivalue posture: an empty slot holds its place so sibling
                // rows stay aligned (the translations triple in the exhibit).
                items.Add(FieldValue.OfText(string.Empty));
            }
        }

        return items.Count == 0
            ? null
            : FieldValue.OfItems(items[0].Kind, items);
    }

    private FieldValue? ConvertOne(JsonElement element, string? converter)
    {
        var text = JsonPathReader.Text(element);

        if (converter is null)
        {
            return text is { Length: > 0 } ? FieldValue.OfText(text) : null;
        }

        if (converter.EndsWith("-id", StringComparison.OrdinalIgnoreCase))
        {
            if (text is not { Length: > 0 })
            {
                return null;
            }

            var scheme = converter[..^3];
            return FieldValue.OfExternalIdentifier(_idRules.Normalize(ExternalId.Of(scheme, text)));
        }

        switch (converter.ToLowerInvariant())
        {
            case "int":
                return TryLong(element, out var number) ? FieldValue.OfInteger(number) : null;

            case "count":
                return TryLong(element, out var count) ? FieldValue.OfCount(count) : null;

            case "decimal":
                return TryDouble(element, out var real) ? FieldValue.OfDecimal(real) : null;

            case "minutes":
                return TryLong(element, out var minutes) && minutes > 0
                    ? FieldValue.OfDuration(TimeSpan.FromMinutes(minutes))
                    : null;

            case "date":
                return text is { Length: > 0 }
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
                    ? FieldValue.OfInstant(instant)
                    : null;

            case "language":
                return text is { Length: > 0 }
                    ? FieldValue.OfLanguage(new Language(text.ToLowerInvariant(), text))
                    : null;

            case "absolute-uri":
                return Uri.TryCreate(text, UriKind.Absolute, out var address)
                    ? FieldValue.OfLink(address)
                    : null;

            case "distinct":
            case "keep-empty":
                return text is { Length: > 0 } ? FieldValue.OfText(text) : null;

            default:
                // Load validation refuses unknown converters; reaching here is a host defect.
                throw new InvalidOperationException($"Unknown response converter '{converter}'.");
        }
    }

    private static bool TryLong(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
        {
            return true;
        }

        return long.TryParse(JsonPathReader.Text(element), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDouble(JsonElement element, out double value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value))
        {
            return true;
        }

        return double.TryParse(JsonPathReader.Text(element), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
