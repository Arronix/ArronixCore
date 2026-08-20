using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Api.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;


namespace Arronix.Api.Endpoints;

/// <summary>
/// How a request's text becomes the platform's identifiers, and how a refusal becomes a problem document.
/// </summary>
/// <remarks>
/// Kept in one place because every endpoint needs the same four things — an item reference, a page, a
/// filter set and a way to say no — and because the textual form of an identifier is part of the API's
/// contract. Writing it once means the form cannot drift between routes.
/// </remarks>
internal static class ApiRequests
{
    /// <summary>
    /// The separator between the parts of an item reference in a path segment or a query value.
    /// </summary>
    private const char RefSeparator = ':';

    /// <summary>
    /// Parses the item identifier used in a path segment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The identifier is <c>level:id</c>, and the fully qualified <c>kind:level:id</c> is also accepted so
    /// that a reference copied out of a payload works when pasted into a URL.
    /// </para>
    /// <para>
    /// It carries the level because a numeric identifier alone does not say what it identifies: the same
    /// number is a different thing at each level of a kind's hierarchy, and the extension that owns the
    /// catalog is asked for one specific thing, not asked to search for it.
    /// </para>
    /// </remarks>
    /// <param name="kind">The media kind the route already established.</param>
    /// <param name="text">The path segment.</param>
    /// <param name="reference">The parsed reference.</param>
    /// <returns><see langword="true"/> when the segment was a well-formed reference.</returns>
    internal static bool TryParseItemRef(MediaKindId kind, string? text, out MediaItemRef reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(RefSeparator);
        var (levelText, idText) = parts.Length switch
        {
            2 => (parts[0], parts[1]),
            3 when string.Equals(parts[0], kind.Value, StringComparison.Ordinal) => (parts[1], parts[2]),
            _ => (null, null),
        };

        if (levelText is null
            || idText is null
            || !MediaLevelId.TryParse(levelText, out var level)
            || !long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        reference = new MediaItemRef(kind, level, MediaItemId.FromInt64(id));
        return true;
    }

    /// <summary>
    /// Renders an item reference in the form a path segment uses.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The <c>level:id</c> text.</returns>
    internal static string ToPathSegment(MediaItemRef reference)
        => string.Create(CultureInfo.InvariantCulture, $"{reference.Level}{RefSeparator}{reference.Id}");

    /// <summary>
    /// Clamps a requested page and size to what the host is willing to serve.
    /// </summary>
    /// <param name="page">The requested page, one-based.</param>
    /// <param name="size">The requested page size.</param>
    /// <param name="options">The API settings.</param>
    /// <returns>The page and size to query with.</returns>
    internal static (int Page, int Size) Paging(int? page, int? size, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resolvedPage = page is > 0 ? page.Value : 1;
        var resolvedSize = size is > 0 ? Math.Min(size.Value, options.MaxPageSize) : options.DefaultPageSize;
        return (resolvedPage, resolvedSize);
    }

    /// <summary>
    /// Reads the repeated <c>filter</c> query values into the filter bag a query carries.
    /// </summary>
    /// <remarks>
    /// The form is <c>filter=fieldId=value</c>, repeated. Values for the same field accumulate into one
    /// entry, which is the set membership a front end means when it ticks two boxes in one facet.
    /// </remarks>
    /// <param name="request">The request.</param>
    /// <returns>The filter bag.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Filters(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var accumulated = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var entry in request.Query["filter"])
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var split = entry.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0 || split == entry.Length - 1)
            {
                continue;
            }

            var field = entry[..split];
            var value = entry[(split + 1)..];

            if (!accumulated.TryGetValue(field, out var values))
            {
                values = [];
                accumulated[field] = values;
            }

            values.Add(value);
        }

        return accumulated.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads every query value that is not one of the reserved paging or filtering names into an input bag,
    /// which is how a front end supplies the parameters a declaration asked it to collect.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="reserved">Names to leave out.</param>
    /// <returns>The collected inputs.</returns>
    internal static IReadOnlyDictionary<string, string> Inputs(HttpRequest request, params string[] reserved)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reserved);

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in request.Query)
        {
            if (reserved.Contains(entry.Key, StringComparer.Ordinal))
            {
                continue;
            }

            var value = entry.Value.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                inputs[entry.Key] = value;
            }
        }

        return inputs;
    }

    /// <summary>
    /// Builds a problem document carrying the platform's own error code.
    /// </summary>
    /// <remarks>
    /// The code travels in an extension member rather than being folded into the status: an HTTP status
    /// says what the caller should do about it, while the code says what actually went wrong, and a client
    /// that wants to be specific should not have to parse a sentence to find out.
    /// </remarks>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">The platform error code.</param>
    /// <param name="detail">A human-readable explanation.</param>
    /// <returns>The problem result.</returns>
    internal static ProblemHttpResult Problem(int status, CoreErrorCode code, string detail)
        => TypedResults.Problem(
            detail: detail,
            statusCode: status,
            title: code.ToString(),
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["errorCode"] = (int)code,
            });

    /// <summary>
    /// The refusal used when a route names a media kind no extension declared.
    /// </summary>
    /// <param name="kind">The kind that was asked for.</param>
    /// <returns>The problem result.</returns>
    internal static ProblemHttpResult UnknownKind(string kind)
        => Problem(
            StatusCodes.Status404NotFound,
            CoreErrorCode.MediaKindNotFound,
            $"No loaded extension declares the media kind '{kind}'.");
}
