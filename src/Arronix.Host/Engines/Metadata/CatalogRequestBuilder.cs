// The http (ARX0008) and definition (ARX0019) contracts are experimental until 1.0.
#pragma warning disable ARX0008
#pragma warning disable ARX0019

using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Http;

namespace Arronix.Host.Engines.Metadata;

/// <summary>
/// Builds outbound requests from declared <see cref="RequestTemplate"/>s and named arguments.
/// </summary>
/// <remarks>
/// <para>
/// The templating half of the Cardigann-style mapping (<c>declarative-media-kinds.md</c> §2.8): route
/// placeholders (<c>movie/{tmdbId}</c>), query parameter templates with converters after a colon
/// (<c>{since:iso8601}</c>, <c>{text:query:plus-separated}</c>), the optional marker (<c>{year?}</c> —
/// the parameter is omitted when the argument is absent, matching the surveyed
/// send-empty-year-unconditionally only when declared without the marker), and body templates with the
/// <c>join</c> converter (<c>[{tmdbIds:join(,)}]</c>).
/// </para>
/// <para>
/// The route resolves relative to the endpoint the definition's settings supply — the definition never
/// sees a socket; the host's gateway executes what this builder produces, attributed and rate-limited
/// under the plugin's identity.
/// </para>
/// </remarks>
internal static class CatalogRequestBuilder
{
    /// <summary>
    /// Builds a request.
    /// </summary>
    /// <param name="template">The declared template.</param>
    /// <param name="endpoint">The catalog endpoint from settings.</param>
    /// <param name="arguments">The argument values by placeholder name.</param>
    /// <returns>The request specification.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required placeholder has no argument. Optional markers make absence declarable; silence would
    /// send a broken route.
    /// </exception>
    public static OutboundHttpRequest Build(
        RequestTemplate template,
        Uri endpoint,
        IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(arguments);

        var route = Expand(template.Route, arguments, escapeValues: true)
            ?? throw new InvalidOperationException(
                $"Request '{template.RequestId}' is missing an argument for its route '{template.Route}'.");

        var builder = new StringBuilder();
        builder.Append(endpoint.ToString().TrimEnd('/'));
        builder.Append('/');
        builder.Append(route.TrimStart('/'));

        var first = true;

        foreach (var parameter in template.Query)
        {
            var value = Expand(parameter.Template, arguments, escapeValues: true);

            if (value is null)
            {
                // An optional parameter whose argument is absent is omitted whole.
                continue;
            }

            builder.Append(first ? '?' : '&');
            builder.Append(Uri.EscapeDataString(parameter.Name));
            builder.Append('=');
            builder.Append(value);
            first = false;
        }

        var request = new OutboundHttpRequest(new Uri(builder.ToString(), UriKind.Absolute))
        {
            Method = HttpMethod.Parse(template.Verb),
        };

        if (template.BodyTemplate is { Length: > 0 } bodyTemplate)
        {
            var body = Expand(bodyTemplate, arguments, escapeValues: false)
                ?? throw new InvalidOperationException(
                    $"Request '{template.RequestId}' is missing an argument for its body template.");

            request.SetContent(body);
            request.Headers.Set("Content-Type", "application/json");
        }

        return request;
    }

    /// <summary>
    /// Converts an instant to the wire spelling the <c>iso8601</c> converter produces.
    /// </summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The text.</returns>
    public static string Iso8601(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? Expand(string template, IReadOnlyDictionary<string, string> arguments, bool escapeValues)
    {
        var result = new StringBuilder();
        var position = 0;

        while (position < template.Length)
        {
            var open = template.IndexOf('{', position);

            if (open < 0)
            {
                result.Append(template, position, template.Length - position);
                break;
            }

            var close = template.IndexOf('}', open + 1);

            if (close < 0)
            {
                result.Append(template, position, template.Length - position);
                break;
            }

            result.Append(template, position, open - position);
            position = close + 1;

            var placeholder = template[(open + 1)..close];
            var pieces = placeholder.Split(':');
            var name = pieces[0];
            var optional = name.EndsWith('?');

            if (optional)
            {
                name = name[..^1];
            }

            if (!arguments.TryGetValue(name, out var value) || value.Length == 0)
            {
                if (optional)
                {
                    return null;
                }

                throw new InvalidOperationException($"No argument supplies '{{{placeholder}}}'.");
            }

            var escaped = false;

            foreach (var converter in pieces.Skip(1))
            {
                (value, escaped) = ApplyConverter(value, converter, escaped, escapeValues);
            }

            if (escapeValues && !escaped)
            {
                value = Uri.EscapeDataString(value);
            }

            result.Append(value);
        }

        return result.ToString();
    }

    private static (string Value, bool Escaped) ApplyConverter(string value, string converter, bool escaped, bool escaping)
    {
        if (string.Equals(converter, "query", StringComparison.OrdinalIgnoreCase))
        {
            // Explicit URL escaping; later converters (plus-separated) run over the escaped form.
            return escaping && !escaped ? (Uri.EscapeDataString(value), true) : (value, escaped);
        }

        if (string.Equals(converter, "plus-separated", StringComparison.OrdinalIgnoreCase))
        {
            // Spaces spell '+' on this catalog's query grammar. Applied after escaping so the plus
            // itself survives.
            if (escaping && !escaped)
            {
                value = Uri.EscapeDataString(value);
                escaped = true;
            }

            return (value.Replace("%20", "+", StringComparison.Ordinal).Replace(" ", "+", StringComparison.Ordinal), escaped);
        }

        if (string.Equals(converter, "iso8601", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
                ? (Iso8601(instant), escaped)
                : (value, escaped);
        }

        if (converter.StartsWith("join(", StringComparison.OrdinalIgnoreCase) && converter.EndsWith(')'))
        {
            var separator = converter[5..^1];

            // Multi-value arguments arrive joined with the unit separator; join re-spells them.
            return (string.Join(
                separator,
                value.Split('\u001F', StringSplitOptions.RemoveEmptyEntries)), escaped);
        }

        throw new InvalidOperationException($"Unknown request converter '{converter}'.");
    }
}
