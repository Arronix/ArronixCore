using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Storage;

// Shape and wire contracts are experimental; an item is read as the first and published as the second.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0017

namespace Arronix.Host.Media;

/// <summary>
/// Joins the half of an item an extension projects to the half the host owns.
/// </summary>
/// <remarks>
/// <para>
/// An extension supplies a catalog record and nothing else: monitoring, the chosen manifestation, the
/// files present and what may be done to the item are all host state or host derivations, and an extension
/// that could report any of them could disagree with the store that holds them. Composing the two halves is
/// therefore host work, and it happens here rather than in whichever consumer asked, so that a command line,
/// a scheduled task and a browser are all told the same thing about the same item.
/// </para>
/// <para>
/// Two fields of the published shape are deliberately left empty rather than filled with a guess, and both
/// are roll-ups over an item's descendants that no seam in this milestone can perform without a query per
/// item per page. <see cref="ItemDetail.Progress"/> stays null: counting what is present against what is
/// wanted needs the completeness units beneath an item, which only the owning extension can enumerate and
/// only one page at a time. <see cref="ItemDetail.FileCount"/> and <see cref="ItemDetail.SizeOnDisk"/> count
/// the files linked to the item itself, so a level that bears files reports its own and a level above one
/// reports nothing rather than an undercount presented as a total.
/// </para>
/// </remarks>
/// <param name="store">Where the user-owned half of an item is read from.</param>
public sealed class MediaItemProjection(IMediaStore store)
{
    private readonly IMediaStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// Publishes one item.
    /// </summary>
    /// <param name="kind">The media kind the item belongs to.</param>
    /// <param name="item">What the extension knows about it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item as the platform publishes it.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="kind"/> or <paramref name="item"/> is <see langword="null"/>.
    /// </exception>
    public async Task<ItemDetail> ProjectAsync(
        RegisteredMediaKind kind,
        ItemView item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(item);

        var facet = await _store.FindLibraryAsync(item.Ref, cancellationToken).ConfigureAwait(false);
        return await ComposeAsync(kind, item, facet, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Publishes one page of items.
    /// </summary>
    /// <param name="kind">The media kind the items belong to.</param>
    /// <param name="page">The page the extension returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page as the platform publishes it.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="kind"/> or <paramref name="page"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The library halves are read in one call rather than one per item. That is the whole reason the store
    /// offers a batch read: merging a projected catalog page with library state is the platform's hottest
    /// read, and doing it item by item is how a page of fifty becomes fifty round trips.
    /// </remarks>
    public async Task<ItemDetailPage> ProjectAsync(
        RegisteredMediaKind kind,
        ItemPage page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(page);

        var facets = await _store
            .FindLibraryManyAsync([.. page.Items.Select(item => item.Ref)], cancellationToken)
            .ConfigureAwait(false);

        var items = new List<ItemDetail>(page.Items.Count);

        foreach (var item in page.Items)
        {
            items.Add(await ComposeAsync(
                kind,
                item,
                facets.GetValueOrDefault(item.Ref),
                cancellationToken).ConfigureAwait(false));
        }

        return new ItemDetailPage(items, page.Page, page.PageSize, page.TotalCount);
    }

    private async Task<ItemDetail> ComposeAsync(
        RegisteredMediaKind kind,
        ItemView item,
        LibraryFacet? facet,
        CancellationToken cancellationToken)
    {
        var level = kind.Shape.HasLevel(item.Ref.Level) ? kind.Shape.LevelOf(item.Ref.Level) : null;
        var (fileCount, sizeOnDisk) = await FilesAsync(item.Ref, cancellationToken).ConfigureAwait(false);

        return new ItemDetail
        {
            Item = item,
            Monitor = MonitorStateOf(level, facet),
            MonitorChoices = facet?.Monitor ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Progress = null,
            SelectedVariant = facet?.SelectedVariant,
            StateIds = StateIdsOf(kind, item),

            // Already derived once, at admission, from the shape and from what the deployment has
            // configured. Recomputing them per item would be a second derivation that could disagree.
            Affordances = kind.Descriptor.Levels
                .FirstOrDefault(presentation => presentation.Level == item.Ref.Level)
                ?.Affordances ?? [],

            FileCount = fileCount,
            SizeOnDisk = sizeOnDisk,
        };
    }

    /// <summary>
    /// Rolls the user's monitoring answers up into the one question a consumer asks.
    /// </summary>
    /// <remarks>
    /// Only two-state axes are rolled up, because only a two-state axis has a reading the host can be sure
    /// of. An enumerated axis declares its choices and which one applies by default, and declares nothing
    /// about which of them means the user does not want the thing — so a level monitored solely along one
    /// answers that the question does not apply, and its answers travel verbatim in
    /// <see cref="ItemDetail.MonitorChoices"/> for a consumer that can present them. Guessing that a choice
    /// called "none" means unwanted would be reading an extension's display text as a contract.
    /// </remarks>
    private static MonitorState MonitorStateOf(MediaLevel? level, LibraryFacet? facet)
    {
        var toggles = level?.MonitorDimensions
            .Where(dimension => dimension.Kind == MonitorDimensionKind.Toggle)
            .ToList();

        if (toggles is null || toggles.Count == 0)
        {
            return MonitorState.NotApplicable;
        }

        var wanted = 0;

        foreach (var dimension in toggles)
        {
            var answer = facet?.Monitor.GetValueOrDefault(dimension.DimensionId) ?? dimension.DefaultChoice;

            if (bool.TryParse(answer, out var flag) && flag)
            {
                wanted++;
            }
        }

        return wanted == 0
            ? MonitorState.Unmonitored
            : wanted == toggles.Count ? MonitorState.Monitored : MonitorState.Partial;
    }

    /// <summary>
    /// Finds the declared conditions the item is currently in.
    /// </summary>
    /// <remarks>
    /// The join is the one the declaration describes: a state names the field it is read from, and the item
    /// is in that state when the field's value is the state's own identifier. Nothing here knows what any of
    /// those identifiers mean, which is what lets a consumer draw a condition it has never heard of.
    /// </remarks>
    private static IReadOnlyList<string> StateIdsOf(RegisteredMediaKind kind, ItemView item)
        =>
        [
            .. kind.Intent.States
                .Where(state => item.Fields.TryGetValue(state.SourceFieldId, out var value)
                    && !value.IsAbsent
                    && string.Equals(value.Text, state.StateId, StringComparison.Ordinal))
                .Select(state => state.StateId),
        ];

    private async Task<(int Count, long Size)> FilesAsync(MediaItemRef reference, CancellationToken cancellationToken)
    {
        var links = await _store.LinksForUnitAsync(reference, cancellationToken).ConfigureAwait(false);

        if (links.Count == 0)
        {
            return (0, 0);
        }

        var size = 0L;
        var counted = new HashSet<MediaFileId>();

        foreach (var link in links)
        {
            // A file satisfying several units is one file on disk. Counting its bytes once per link would
            // report an item as several times the size it takes up.
            if (!counted.Add(link.File))
            {
                continue;
            }

            var file = await _store.FindFileAsync(link.File, cancellationToken).ConfigureAwait(false);

            if (file is not null)
            {
                size += file.Size;
            }
        }

        return (counted.Count, size);
    }
}
