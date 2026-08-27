
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Client.Services;

/// <summary>
/// What the client is asking to see: one page of one level, narrowed and ordered.
/// </summary>
/// <remarks>
/// Every member here corresponds to something an extension declared — a browse axis, a sort option, a
/// filter option — so nothing in this request can name a value the current media kind has not published.
/// It is the request shape of the descriptor-driven browser, not a schema of its own.
/// </remarks>
public sealed record ItemBrowseRequest
{
    /// <summary>
    /// Gets the item whose contents are wanted, when the traversal has descended into one.
    /// </summary>
    public MediaItemRef? Parent { get; init; }

    /// <summary>
    /// Gets the <see cref="Abstractions.Intent.BrowseAxis.AxisId"/> being traversed.
    /// </summary>
    public string? AxisId { get; init; }

    /// <summary>
    /// Gets the free-text narrowing.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the field narrowings, keyed by <see cref="FieldDescriptor.FieldId"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Gets the <see cref="FieldDescriptor.FieldId"/> the page is ordered by.
    /// </summary>
    public string? SortFieldId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the ordering runs from greatest to least.
    /// </summary>
    public bool SortDescending { get; init; }

    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Gets the number of items wanted.
    /// </summary>
    public int PageSize { get; init; } = 60;

    /// <summary>
    /// Renders the request as query values.
    /// </summary>
    /// <returns>The query values, in a stable order so that two equal requests address the same page.</returns>
    public IReadOnlyList<KeyValuePair<string, string?>> ToQuery()
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("parent", Parent is { } parent ? ApiPaths.ItemReference(parent) : null),
            new("axis", AxisId),
            new("q", Text),
            new("sort", SortFieldId),
            new("desc", SortDescending ? "true" : null),
            new("page", Page.ToString(CultureInfo.InvariantCulture)),
            new("size", PageSize.ToString(CultureInfo.InvariantCulture)),
        };

        query.AddRange(
            Filters
                .OrderBy(filter => filter.Key, StringComparer.Ordinal)
                .Select(filter => new KeyValuePair<string, string?>(
                    "filter",
                    $"{filter.Key}={filter.Value}")));

        return query;
    }
}
