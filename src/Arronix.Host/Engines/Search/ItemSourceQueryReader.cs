using Arronix.Abstractions.Shape;


namespace Arronix.Host.Engines.Search;

/// <summary>
/// The query templater's read window, backed by the kind's item source.
/// </summary>
/// <remarks>
/// One method wide because that is the whole of what templating needs: the item a query is being planned
/// for. Kept as its own seam rather than handing the planner an <see cref="IMediaItemSource"/> so that a
/// planner cannot start paging, filtering or searching — a plan is built from one item's declared fields,
/// and a planner able to query would be a planner able to make decisions the declaration does not express.
/// </remarks>
internal sealed class ItemSourceQueryReader : IQueryItemReader
{
    private readonly IMediaItemSource _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemSourceQueryReader"/> class.
    /// </summary>
    /// <param name="items">The kind's item source.</param>
    internal ItemSourceQueryReader(IMediaItemSource items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    /// <inheritdoc />
    public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
        => reference.Kind == _items.MediaKind
            ? _items.GetAsync(reference, cancellationToken)
            : Task.FromResult<ItemView?>(null);
}
