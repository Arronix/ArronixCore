// The shape (ARX0013) and definition (ARX0019) contracts are experimental until 1.0.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

using System.Linq;
using System.Text.Json;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Metadata;

/// <summary>
/// Executes the five host derivation kinds a <c>CatalogDeclaration</c> may invoke, over mapped fields.
/// </summary>
/// <remarks>
/// <para>
/// Each kind is a host-implemented rule with declared parameters (<c>DerivationRule</c> contract). The
/// parameter surface here is <b>structured</b> — field identifiers and numbers — not the expression
/// strings the Movies exhibit sketches (<c>"now &gt; inCinemas"</c>): a free-form expression parameter
/// would be a second micro-language, which the definition design's own closed-vocabulary rule refuses
/// (§2.9, and exhibit-divergence resolution 8 applied the same correction to selection rows). The
/// divergence is reported, not absorbed.
/// </para>
/// <para>
/// Semantics are ported verbatim from the pure mapper the four catalogers share their logic with:
/// <c>Arronix.Plugin.Movies/Providers/MoviesCataloger.cs</c> — <c>DeriveStatus</c> (:750, itself
/// Radarr's <c>SkyHookProxy.cs:292-314</c> including the 90-day theatrical-window clause that looks
/// like a bug and is not), <c>DeriveReleaseDate</c> (:795), <c>SelectCertification</c> (:817, the
/// no-cross-region rule), and <c>SelectImage</c> (:843, first-of-role over absolute addresses).
/// </para>
/// </remarks>
internal static class CatalogDerivations
{
    /// <summary>
    /// Applies every declared derivation to a node's mapped fields, in declared order.
    /// </summary>
    /// <param name="rules">The rules.</param>
    /// <param name="fields">The mapped fields, mutated in place.</param>
    /// <param name="document">The response element the node was mapped from, for source paths.</param>
    /// <param name="settings">The definition's settings values.</param>
    /// <param name="now">The present instant, from the host clock.</param>
    public static void Apply(
        IReadOnlyList<DerivationRule> rules,
        IDictionary<string, FieldValue> fields,
        JsonElement document,
        IReadOnlyDictionary<string, string> settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(settings);

        foreach (var rule in rules)
        {
            switch (rule.Kind)
            {
                case DerivationKind.StatusStages when rule.TargetFieldId is { Length: > 0 }:
                    ApplyStatusStages(rule, fields, now);
                    break;

                case DerivationKind.DateReduction when rule.TargetFieldId is { Length: > 0 }:
                    ApplyDateReduction(rule, fields);
                    break;

                case DerivationKind.RegionSelect when rule.TargetFieldId is { Length: > 0 }:
                    ApplyRegionSelect(rule, fields, document, settings);
                    break;

                case DerivationKind.ImageRoleSelect:
                    ApplyImageRoles(rule, fields, document);
                    break;

                case DerivationKind.Conditional when rule.TargetFieldId is { Length: > 0 }:
                    ApplyConditional(rule, fields);
                    break;

                default:
                    break;
            }
        }
    }

    private static void ApplyStatusStages(
        DerivationRule rule,
        IDictionary<string, FieldValue> fields,
        DateTimeOffset now)
    {
        // Structured parameters: "stages" ("announced<inCinemas<released" — first is the floor),
        // "cinemaField", "homeFields" (comma list), "theatricalWindowDays".
        var stages = Text(rule, "stages")?.Split('<', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (stages is not { Length: >= 2 })
        {
            return;
        }

        var cinemaField = Text(rule, "cinemaField") ?? "inCinemas";
        var homeFields = (Text(rule, "homeFields") ?? "physicalRelease,digitalRelease")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var windowDays = Integer(rule, "theatricalWindowDays") ?? 90;

        var cinema = DateOf(fields, cinemaField);
        var homes = homeFields.Select(field => DateOf(fields, field)).ToList();

        // Ported: MoviesCatalogMapper.DeriveStatus (MoviesCataloger.cs:750-781; Radarr
        // SkyHookProxy.cs:292-314). A movie ninety days past its theatrical date with no home date at
        // all counts as released, or it stays permanently unavailable and is never searched for.
        var floorStage = stages[0];
        var cinemaStage = stages.Length >= 3 ? stages[^2] : floorStage;
        var releasedStage = stages[^1];

        var status = floorStage;

        if (cinema is { } cinemaDate && now > cinemaDate)
        {
            status = cinemaStage;

            if (homes.All(home => home is null) && now > cinemaDate.AddDays(windowDays))
            {
                status = releasedStage;
            }
        }

        if (homes.Any(home => home is { } homeDate && now >= homeDate))
        {
            status = releasedStage;
        }

        fields[rule.TargetFieldId!] = FieldValue.OfEnumerated(status);
    }

    private static void ApplyDateReduction(DerivationRule rule, IDictionary<string, FieldValue> fields)
    {
        // The closed reduction grammar: "min(a, b) ?? c" — earliest of the named fields, then the
        // fallback chain. Ported: MoviesCatalogMapper.DeriveReleaseDate (MoviesCataloger.cs:795-811).
        var expression = Text(rule, "reduce");

        if (expression is not { Length: > 0 })
        {
            return;
        }

        DateTimeOffset? result = null;

        foreach (var alternative in expression.Split("??", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (alternative.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
                && alternative.EndsWith(')'))
            {
                var candidates = alternative[4..^1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(field => DateOf(fields, field))
                    .Where(date => date is not null)
                    .Select(date => date!.Value)
                    .ToList();

                if (candidates.Count > 0)
                {
                    result = candidates.Min();
                }
            }
            else
            {
                result = DateOf(fields, alternative);
            }

            if (result is not null)
            {
                break;
            }
        }

        if (result is { } reduced)
        {
            fields[rule.TargetFieldId!] = FieldValue.OfInstant(reduced);
        }
    }

    private static void ApplyRegionSelect(
        DerivationRule rule,
        IDictionary<string, FieldValue> fields,
        JsonElement document,
        IReadOnlyDictionary<string, string> settings)
    {
        var source = Text(rule, "source");
        var regionKey = Text(rule, "regionKey");
        var valueKey = Text(rule, "valueKey");

        if (source is null || regionKey is null || valueKey is null)
        {
            return;
        }

        var region = Text(rule, "regionSetting") is { Length: > 0 } settingId
            && settings.TryGetValue(settingId, out var configured)
            && configured.Length > 0
            ? configured
            : Text(rule, "defaultRegion") ?? string.Empty;

        var fallbackAnywhere = Flag(rule, "fallbackToAnyRegion") ?? false;

        string? selected = null;
        string? first = null;

        foreach (var candidate in JsonPathReader.Evaluate(document, source))
        {
            var candidateRegion = JsonPathReader.FirstText(candidate, regionKey);
            var candidateValue = JsonPathReader.FirstText(candidate, valueKey);

            if (candidateValue is not { Length: > 0 })
            {
                continue;
            }

            first ??= candidateValue;

            if (string.Equals(candidateRegion, region, StringComparison.OrdinalIgnoreCase))
            {
                selected = candidateValue;
                break;
            }
        }

        // No cross-region fallback unless declared: "PG-13" on a foreign regulator's scale means
        // something else (MoviesCataloger.cs:817-835).
        selected ??= fallbackAnywhere ? first : null;

        if (selected is { Length: > 0 })
        {
            fields[rule.TargetFieldId!] = FieldValue.OfText(selected);
        }
    }

    private static void ApplyImageRoles(DerivationRule rule, IDictionary<string, FieldValue> fields, JsonElement document)
    {
        var source = Text(rule, "source");
        var roleKey = Text(rule, "roleKey");
        var urlKey = Text(rule, "urlKey");
        var roles = Text(rule, "roles");

        if (source is null || roleKey is null || urlKey is null || roles is null)
        {
            return;
        }

        var requireAbsolute = Flag(rule, "requireAbsoluteUri") ?? true;

        // "poster->poster, fanart->fanart": response role → target field, first of each role wins
        // (MoviesCataloger.cs:843-866).
        foreach (var pair in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var arrow = pair.Split("->", StringSplitOptions.TrimEntries);

            if (arrow.Length != 2)
            {
                continue;
            }

            foreach (var candidate in JsonPathReader.Evaluate(document, source))
            {
                if (!string.Equals(JsonPathReader.FirstText(candidate, roleKey), arrow[0], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var address = JsonPathReader.FirstText(candidate, urlKey);

                if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                {
                    fields[arrow[1]] = FieldValue.OfArtwork(uri);
                    break;
                }

                if (!requireAbsolute && address is { Length: > 0 })
                {
                    fields[arrow[1]] = FieldValue.OfText(address);
                    break;
                }
            }
        }
    }

    private static void ApplyConditional(DerivationRule rule, IDictionary<string, FieldValue> fields)
    {
        // Structured form of the exhibit's secondary-year rule: write extract(sourceField) when the
        // source is present and its extract differs from extract(notEqualToField).
        var sourceField = Text(rule, "sourceField");

        if (sourceField is null || !fields.TryGetValue(sourceField, out var source) || source.IsAbsent)
        {
            return;
        }

        var extract = Text(rule, "extract");
        var extracted = Extract(source, extract);

        if (extracted is null)
        {
            return;
        }

        if (Text(rule, "notEqualToField") is { Length: > 0 } otherField)
        {
            var other = fields.TryGetValue(otherField, out var otherValue) ? Extract(otherValue, extract) : null;

            if (other is { } same && same == extracted.Value)
            {
                return;
            }
        }

        fields[rule.TargetFieldId!] = FieldValue.OfInteger(extracted.Value);
    }

    private static long? Extract(FieldValue value, string? extract)
    {
        if (string.Equals(extract, "year", StringComparison.OrdinalIgnoreCase))
        {
            return value switch
            {
                { Instant: { } instant } => instant.Year,
                { Date: { } date } => date.Year,
                { Number: { } number } => number,
                _ => null,
            };
        }

        return value.Number;
    }

    private static DateTimeOffset? DateOf(IDictionary<string, FieldValue> fields, string fieldId)
    {
        if (!fields.TryGetValue(fieldId, out var value) || value.IsAbsent)
        {
            return null;
        }

        return value switch
        {
            { Instant: { } instant } => instant,
            { Date: { } date } => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            _ => null,
        };
    }

    private static string? Text(DerivationRule rule, string parameter) =>
        rule.Parameters.TryGetValue(parameter, out var value) ? value.Text : null;

    private static long? Integer(DerivationRule rule, string parameter) =>
        rule.Parameters.TryGetValue(parameter, out var value) ? value.Number : null;

    private static bool? Flag(DerivationRule rule, string parameter) =>
        rule.Parameters.TryGetValue(parameter, out var value) ? value.Flag : null;
}
