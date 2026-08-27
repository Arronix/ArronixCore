using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Providers;
using Arronix.Host.Storage;
using Arronix.Host.Storage.Durable;

namespace Arronix.Host.Media.Catalog;

/// <summary>What adding an item did.</summary>
/// <param name="View">The item as the catalog describes it, which is what a caller is shown.</param>
/// <param name="Created">
/// Whether this call materialized the record, or found the one an earlier add wrote. It is not part of what
/// is published: a caller learns it from the response status, so the body says the same thing either way.
/// </param>
public readonly record struct CatalogAddition(CatalogItemView View, bool Created);

/// <summary>
/// Searching a catalog, adding what it found, and refreshing what was added.
/// </summary>
/// <remarks>
/// <para>
/// The three operations differ in exactly one way that matters, and it is the reason they are one class.
/// A search mints or resolves identity and stops; an add materializes the durable record and the user's
/// presence beside it; a refresh replaces the catalog-owned half and cannot reach the user-owned one.
/// Putting them in separate places is how the middle one quietly starts happening during the first.
/// </para>
/// <para>
/// Every catalog call goes through <see cref="CatalogDispatcher"/>, so routing is by scheme and the
/// cataloger remains the authority for identity. No curator is involved: a curated list proposes references
/// to a catalog's items and mints nothing of its own.
/// </para>
/// </remarks>
public sealed class CatalogLibrary
{
    private readonly MediaKindRegistry _kinds;
    private readonly CatalogDispatcher _catalog;
    private readonly ICatalogRecordStore _records;
    private readonly IMediaStore _library;
    private readonly CatalogIdentity _identity;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Serializes identity convergence with the writes that depend on it.
    /// </summary>
    /// <remarks>
    /// Assigning identity and materializing the record under it are one decision taken in two steps. A
    /// merge that commits between them moves the rows that exist at that instant, and a record written
    /// afterwards lands under an identity nothing resolves to any more — present in the database and
    /// invisible to every reader. Every operation that can converge identity is taken here, so no merge can
    /// land in that gap. It costs concurrency between catalog operations, which this vertical can afford
    /// and a silently unreachable record is not.
    /// </remarks>
    private readonly SemaphoreSlim _admission = new(1, 1);

    /// <summary>How many times one refresh may follow a merge onto another catalog's record.</summary>
    private const int OwnerHandovers = 1;

    internal CatalogLibrary(
        MediaKindRegistry kinds,
        CatalogDispatcher catalog,
        ICatalogRecordStore records,
        IMediaStore library,
        CatalogIdentity identity,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(clock);

        _kinds = kinds;
        _catalog = catalog;
        _records = records;
        _library = library;
        _identity = identity;
        _clock = clock;
    }

    /// <summary>
    /// Searches one catalog, giving every hit the identity the platform would hold it under.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="scheme">The catalog to search.</param>
    /// <param name="query">The query.</param>
    /// <param name="page">The one-based page of the catalog's answer to return.</param>
    /// <param name="pageSize">How many results that page holds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page or its size is not positive.</exception>
    /// <exception cref="ArronixException">The kind is not installed, or no cataloger owns the scheme.</exception>
    /// <remarks>
    /// <para>
    /// Nothing is materialized. Identity is durable because two searches for one item must agree about what
    /// it will be called; the record that describes it exists only once someone has asked for it.
    /// </para>
    /// <para>
    /// The catalog contract carries no paging, so the page is taken over the answer the catalog gave and
    /// the total is the size of that answer — not a claim about how many records the catalog holds.
    /// </para>
    /// </remarks>
    public async Task<CatalogItemPage> SearchAsync(
        MediaKindId kind,
        string scheme,
        CatalogQuery query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var runtime = RuntimeOf(kind);

        IReadOnlyList<MaterializedItem<IMediaItem>> found;

        await _admission.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            found = await _catalog.SearchAsync(runtime, scheme, query, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _admission.Release();
        }

        // Wide, because a page number a caller is free to choose must not multiply into a negative offset.
        var offset = ((long)page - 1) * pageSize;

        if (offset >= found.Count)
        {
            return new CatalogItemPage([], page, pageSize, found.Count);
        }

        var wanted = found.Skip((int)offset).Take(pageSize).ToArray();

        // One read for the page rather than one per result: whether the user holds an item is the same
        // question browse asks, and asking it fifty times is how a page becomes fifty round trips.
        var held = await _library
            .FindLibraryManyAsync([.. wanted.Select(static item => item.Reference)], cancellationToken)
            .ConfigureAwait(false);

        return new CatalogItemPage(
            [
                .. wanted.Select(item => new CatalogItemView
                {
                    // The exact value the catalog returned, projected by the kind's own projector - the
                    // same one browse uses - so a result carries every fact the item states.
                    Item = runtime.Project(item.Reference, item.Item, _identity),
                    CatalogId = item.CatalogId,
                    InLibrary = held.ContainsKey(item.Reference),
                }),
            ],
            page,
            pageSize,
            found.Count);
    }

    /// <summary>
    /// Adds one catalog item: materializes its durable record and records the user's presence beside it.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="catalogId">The identifier to add. It may be an alias the catalog redirects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was added, or <see langword="null"/> when no owning catalog holds the item.</returns>
    /// <exception cref="ArronixException">The kind is not installed, or no cataloger owns the scheme.</exception>
    /// <remarks>
    /// Safe to retry. The record is keyed by the catalog identifier as well as by the reference, so a second
    /// add of the same item answers with the first one's record and leaves the date the user added it alone.
    /// </remarks>
    public async Task<CatalogAddition?> AddAsync(
        MediaKindId kind,
        ExternalId catalogId,
        CancellationToken cancellationToken = default)
    {
        var runtime = RuntimeOf(kind);

        await _admission.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var materialized = await _catalog
                .FetchAsync(runtime, catalogId, cancellationToken)
                .ConfigureAwait(false);

            if (materialized is null)
            {
                return null;
            }

            var contract = CatalogPayloadContract.For(runtime);

            // The record and the user's presence are written together, so there is no window in which the
            // library shows an item nobody added.
            var written = await _records
                .MaterializeAsync(Record(contract, materialized), _clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);

            // What is published is what is stored. On a retry the record was written by the earlier add,
            // and the catalog may have said something different since; publishing the freshly fetched value
            // would describe an item the ordinary item routes do not have, because nothing wrote it.
            var stored = written.Created
                ? materialized.Item
                : contract.Read(written.Record.Payload, written.Record.ContractMetadataHash);

            return new CatalogAddition(
                new CatalogItemView
                {
                    Item = runtime.Project(written.Record.Reference, stored, _identity),
                    CatalogId = written.Record.CatalogId,
                    InLibrary = await HeldAsync(written.Record.Reference, cancellationToken).ConfigureAwait(false),
                },
                written.Created);
        }
        finally
        {
            _admission.Release();
        }
    }

    /// <summary>
    /// Refreshes one added item's catalog-owned facts from the catalog that is the authority for it.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="reference">The item. A reference a merge superseded still resolves.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refreshed item, or <see langword="null"/> when the catalog no longer answers.</returns>
    /// <exception cref="ArronixException">
    /// The kind is not installed, no record is held under the reference, or no cataloger owns the record's
    /// scheme.
    /// </exception>
    /// <remarks>
    /// The catalog half only. Which item the user wants and how they monitor it are theirs, live in a
    /// different table, and are not reachable from here — that is the whole reason the two halves are
    /// separate. A catalog that answers with a withdrawn record writes exactly that, and the record stays
    /// addressable.
    /// </remarks>
    public async Task<CatalogItemView?> RefreshAsync(
        MediaKindId kind,
        MediaItemRef reference,
        CancellationToken cancellationToken = default)
    {
        var runtime = RuntimeOf(kind);

        await _admission.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var target = _identity.Canonical(reference);

            // Bounded, because a fetch can hand the record over: an identifier it returns may merge this
            // record onto one another catalog already owns, and that catalog's record is the one the merge
            // keeps. One handover is a real answer; a second means the installation is disagreeing with
            // itself and this refuses rather than chasing it.
            for (var attempt = 0; attempt <= OwnerHandovers; attempt++)
            {
                var held = await _records.FindAsync(target, cancellationToken).ConfigureAwait(false)
                    ?? throw new ArronixException(
                        CoreErrorCode.MediaItemNotFound,
                        $"No catalog record is held under '{target}', so there is nothing to refresh. An "
                        + "item is refreshed after it has been added, not instead of adding it.");

                // Asked for by the identifier the owning catalog answered with last time, so a refresh asks
                // the authority for the record rather than whichever catalog a caller happened to name.
                var materialized = await _catalog
                    .FetchAsync(runtime, held.CatalogId, cancellationToken)
                    .ConfigureAwait(false);

                if (materialized is null)
                {
                    return null;
                }

                // The fetch itself can converge identifiers: what came back may carry a cross-reference
                // that merged this record onto a survivor, and the journal moved rows with it. That is this
                // record under the name it now has, so it is followed; what is refused is an answer that
                // resolved to some other entity entirely.
                var surviving = _identity.Canonical(target);

                if (materialized.Reference != surviving)
                {
                    throw new ArronixException(
                        CoreErrorCode.CatalogIdentityInvalid,
                        $"Refreshing '{target}' resolved to '{materialized.Reference}', and that reference "
                        + $"now survives as '{surviving}'. A refresh restates one record; it does not move "
                        + "an item onto another identity.");
                }

                var owner = await _records.FindAsync(surviving, cancellationToken).ConfigureAwait(false)
                    ?? throw new ArronixException(
                        CoreErrorCode.MediaItemNotFound,
                        $"'{target}' merged onto '{surviving}', which holds no record.");

                if (owner.CatalogId != held.CatalogId)
                {
                    // The merge kept the survivor's own catalog as the authority. Writing what this catalog
                    // answered into that record would make ownership a property of who refreshed last, so
                    // the refresh continues through the surviving record's own identifier instead.
                    target = surviving;
                    continue;
                }

                var refreshed = await _records
                    .RefreshAsync(
                        Record(CatalogPayloadContract.For(runtime), materialized) with { Reference = surviving },
                        cancellationToken)
                    .ConfigureAwait(false);

                return new CatalogItemView
                {
                    Item = runtime.Project(refreshed.Reference, materialized.Item, _identity),
                    CatalogId = refreshed.CatalogId,
                    InLibrary = await HeldAsync(refreshed.Reference, cancellationToken).ConfigureAwait(false),
                };
            }

            throw new ArronixException(
                CoreErrorCode.CatalogIdentityInvalid,
                $"Refreshing '{reference}' changed owning catalog more than once, so which catalog is the "
                + "authority for it is not settled and this refresh is refused.");
        }
        finally
        {
            _admission.Release();
        }
    }

    /// <summary>Whether the user has explicitly added this item, which only their own facet answers.</summary>
    /// <remarks>
    /// A catalog record can exist without one — a crash between two writes, a record kept after a facet was
    /// removed — and reporting a record as a library entry would tell a user they added something they did
    /// not.
    /// </remarks>
    private async Task<bool> HeldAsync(MediaItemRef reference, CancellationToken cancellationToken)
        => await _library.FindLibraryAsync(reference, cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Builds the durable record for one materialized item, through the item's own contract.</summary>
    private static CatalogRecord Record(CatalogPayloadContract contract, MaterializedItem<IMediaItem> materialized)
        => new()
        {
            Reference = materialized.Reference,
            CatalogId = materialized.CatalogId,

            // Both read off the common item contract, and both indexes over the payload beside them rather
            // than facts this record states independently of it.
            Title = materialized.Item.Title,
            State = materialized.Item.CatalogState,
            ContractMetadataHash = contract.MetadataHash,
            Payload = contract.Write(materialized.Item),
        };

    /// <summary>The kind's closed typed runtime, or a refusal naming what is missing.</summary>
    private IMediaTypeRuntime RuntimeOf(MediaKindId kind)
    {
        if (!_kinds.TryGet(kind, out var registered) || registered is null)
        {
            throw new ArronixException(
                CoreErrorCode.MediaKindNotFound,
                $"No loaded extension declares the media kind '{kind}'.");
        }

        return registered.MediaType
            ?? throw new ArronixException(
                CoreErrorCode.MediaKindNotFound,
                $"Media kind '{kind}' was not declared as a typed media type, so it has no catalog to add "
                + "from.");
    }
}
