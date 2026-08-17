using System.Globalization;
using System.Text.Json;

namespace Arronix.Host.Engines.Metadata;

/// <summary>
/// Evaluates the closed JSON-path subset a <c>ResponseMap</c> row may use.
/// </summary>
/// <remarks>
/// The grammar is deliberately tiny — <c>$</c>, <c>.name</c> steps, <c>[*]</c> fan-out, and a trailing
/// <c>.length</c> pseudo-property for array counts — because the response map is a declaration, not a
/// query language. Anything richer is the integration-plugin escape
/// (<c>docs/design/declarative-media-kinds.md</c> §2.8), never a bigger path grammar.
/// </remarks>
internal static class JsonPathReader
{
    /// <summary>
    /// Evaluates a path against a document element.
    /// </summary>
    /// <param name="root">The element the path is relative to.</param>
    /// <param name="path">The path, e.g. <c>$.translations[*].title</c>.</param>
    /// <returns>The matched elements, in document order. Empty when the path finds nothing.</returns>
    public static IReadOnlyList<JsonElement> Evaluate(JsonElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var current = new List<JsonElement> { root };
        var remaining = path.StartsWith("$", StringComparison.Ordinal) ? path[1..] : path;

        foreach (var rawStep in remaining.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var step = rawStep;
            var fanOut = step.EndsWith("[*]", StringComparison.Ordinal);

            if (fanOut)
            {
                step = step[..^3];
            }

            var next = new List<JsonElement>();

            foreach (var element in current)
            {
                var target = element;

                if (step.Length > 0)
                {
                    if (string.Equals(step, "length", StringComparison.Ordinal)
                        && element.ValueKind == JsonValueKind.Array)
                    {
                        // The .length pseudo-property: the count, as a number element.
                        using var document = JsonDocument.Parse(
                            element.GetArrayLength().ToString(CultureInfo.InvariantCulture));
                        next.Add(document.RootElement.Clone());
                        continue;
                    }

                    if (element.ValueKind != JsonValueKind.Object
                        || !element.TryGetProperty(step, out target))
                    {
                        continue;
                    }
                }

                if (fanOut)
                {
                    if (target.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var member in target.EnumerateArray())
                        {
                            next.Add(member);
                        }
                    }
                }
                else if (target.ValueKind != JsonValueKind.Null && target.ValueKind != JsonValueKind.Undefined)
                {
                    next.Add(target);
                }
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Evaluates a path and renders the first match as text.
    /// </summary>
    /// <param name="root">The element the path is relative to.</param>
    /// <param name="path">The path.</param>
    /// <returns>The text, or null when nothing matched.</returns>
    public static string? FirstText(JsonElement root, string path)
    {
        var matches = Evaluate(root, path);

        return matches.Count == 0 ? null : Text(matches[0]);
    }

    /// <summary>
    /// Renders a scalar element as text.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The text, or null for non-scalars and nulls.</returns>
    public static string? Text(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };
}
