using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;

// The shape contracts this adapter reads through are experimental.
#pragma warning disable ARX0013

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// The naming engine's item resolver, backed by the kind's item source.
/// </summary>
/// <remarks>
/// The rename seam hands the engine a bare <see cref="MediaItemId"/> with no level on it, so the level has
/// to be recovered before the item can be read. It is recovered from the shape rather than guessed: the
/// declared levels are tried outermost first, which is the order a rename actually asks in — a folder name
/// is rendered for a library entry, a file name for the level below it.
/// </remarks>
internal sealed class ItemSourceNamingResolver : INamingItemResolver
{
    private readonly IMediaItemSource _items;
    private readonly ValidatedShape _shape;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemSourceNamingResolver"/> class.
    /// </summary>
    /// <param name="items">The kind's item source.</param>
    /// <param name="shape">The kind's resolved shape, whose levels the identifier is tried against.</param>
    internal ItemSourceNamingResolver(IMediaItemSource items, ValidatedShape shape)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(shape);

        _items = items;
        _shape = shape;
    }

    /// <inheritdoc />
    public async Task<ItemView?> GetItemAsync(MediaItemId itemId, CancellationToken cancellationToken = default)
    {
        foreach (var level in _shape.Levels)
        {
            var found = await _items
                .GetAsync(new MediaItemRef(_shape.Kind, level.Id, itemId), cancellationToken)
                .ConfigureAwait(false);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
