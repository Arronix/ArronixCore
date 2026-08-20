using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;


namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The match engine's read window, backed by the kind's item source.
/// </summary>
/// <remarks>
/// <para>
/// The two seams exist separately on purpose — <see cref="IMatchEntryReader"/> is deliberately narrower than
/// <see cref="IMediaItemSource"/>, so match behavior cannot grow paging or workbench dependencies the
/// declaration has no way to describe — and this is the one place they are joined. Everything here is a
/// translation: an entry list is a query at the library-entry level, a unit list is a query for one item's
/// children, and neither adds a rule of its own.
/// </para>
/// <para>
/// A kind whose acquisition unit is its library entry — one level, one file, no children — answers its own
/// unit query with itself. That is not a special case bolted on for one media kind: the shape says the level
/// has nothing below it, so the item is the only unit a match within it can resolve to.
/// </para>
/// </remarks>
internal sealed class ItemSourceEntryReader : IMatchEntryReader
{
    private const int PageSize = 200;

    private readonly IMediaItemSource _items;
    private readonly ValidatedShape _shape;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemSourceEntryReader"/> class.
    /// </summary>
    /// <param name="items">The kind's item source.</param>
    /// <param name="shape">The kind's resolved shape, which the level traversal is read from.</param>
    internal ItemSourceEntryReader(IMediaItemSource items, ValidatedShape shape)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(shape);

        _items = items;
        _shape = shape;
    }

    /// <inheritdoc />
    public Task<MediaItemRef?> ResolveExternalAsync(
        MediaKindId kind,
        ExternalId externalId,
        CancellationToken cancellationToken = default)
        => kind == _shape.Kind
            ? _items.ResolveExternalAsync(externalId, cancellationToken)
            : Task.FromResult<MediaItemRef?>(null);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemView>> GetEntriesAsync(
        MediaKindId kind,
        CancellationToken cancellationToken = default)
        => kind == _shape.Kind
            ? await ReadAllAsync(_shape.LibraryEntry.Id, parent: null, cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
        => reference.Kind == _shape.Kind
            ? _items.GetAsync(reference, cancellationToken)
            : Task.FromResult<ItemView?>(null);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemView>> GetUnitsAsync(
        MediaItemRef entry,
        CancellationToken cancellationToken = default)
    {
        if (entry.Kind != _shape.Kind || !_shape.HasLevel(entry.Level))
        {
            return [];
        }

        var below = _shape.Levels.FirstOrDefault(level => level.Parent == entry.Level);

        if (below is null)
        {
            var self = await _items.GetAsync(entry, cancellationToken).ConfigureAwait(false);
            return self is null ? [] : [self];
        }

        return await ReadAllAsync(below.Id, entry, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ItemView>> ReadAllAsync(
        MediaLevelId level,
        MediaItemRef? parent,
        CancellationToken cancellationToken)
    {
        var found = new List<ItemView>();

        for (var page = 1; ; page++)
        {
            var read = await _items.QueryAsync(
                new ItemQuery
                {
                    Kind = _shape.Kind,
                    Level = level,
                    Parent = parent,
                    Page = page,
                    PageSize = PageSize,
                },
                cancellationToken).ConfigureAwait(false);

            found.AddRange(read.Items);

            if (read.Items.Count < PageSize || found.Count >= read.TotalCount)
            {
                return found;
            }
        }
    }
}
