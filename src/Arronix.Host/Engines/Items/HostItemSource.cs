using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Storage;
using Arronix.Host.Storage.Durable;


namespace Arronix.Host.Engines.Items;

/// <summary>
/// The item-store engine (design table row E9): the host's answer for a declared kind's items, read from
/// the durable catalog records that kind's own catalog materialized.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the host owns this at all.</b> A declared media kind ships data and no code, so there is no
/// extension object left to project a catalog. Every consumer downstream of the registry reaches an
/// admitted kind's items through this seam and has no other route.
/// </para>
/// <para>
/// <b>What it answers with.</b> A record holds the item exactly as its own contract serialized it, so this
/// reads it back into that typed value and hands it to the kind's own projector — the same projector that
/// ran when the item was added. A record read after a restart therefore projects identically to the one
/// projected when it was written, rather than through a second host-side derivation that could disagree.
/// </para>
/// <para>
/// <b>What it does not answer for.</b> An item nobody added has no record and is not here; searching a
/// catalog resolves identity and materializes nothing. A reference superseded by a merge is canonicalized
/// before it is looked up, so a caller holding an old one still finds the item rather than a hole.
/// </para>
/// <para>
/// The two working-surface methods refuse. An empty proposal and an unsuccessful commit both read as
/// ordinary answers to a caller, and a surface that does not exist has not answered.
/// </para>
/// </remarks>
internal sealed class HostItemSource : IMediaItemSource
{
    private const string NoWorkbenches =
        "This build materializes catalog records on an explicit add. It has no working surface, so it can "
        + "neither propose rows nor commit them.";

    private readonly ICatalogRecordStore _records;
    private readonly CatalogIdentity _identity;
    private readonly IMediaTypeRuntime _runtime;
    private readonly Lazy<CatalogPayloadContract> _contract;
    private readonly MediaLevelId _itemLevel;
    private readonly string? _titleFieldId;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostItemSource"/> class.
    /// </summary>
    /// <param name="definition">The kind's validated definition.</param>
    /// <param name="records">Where the kind's catalog records are held.</param>
    /// <param name="identity">Host identity state, which outlives any extension reload.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The definition did not come from a typed declaration, so there is no runtime to project through.
    /// </exception>
    internal HostItemSource(
        ValidatedDefinition definition,
        ICatalogRecordStore records,
        CatalogIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(identity);

        _records = records;
        _identity = identity;
        _runtime = definition.Runtime
            ?? throw new InvalidOperationException(
                $"Media kind '{definition.Kind}' was assembled from its pieces rather than derived from a "
                + "typed declaration, so there is no runtime to project its items through.");

        // Resolved on first use rather than at admission. An item type that implements the entity contract
        // directly, rather than deriving from the common item, carries no generated bridge — which is a
        // supported way to declare a kind, so it must not be a reason to refuse the whole kind. What it
        // cannot be is a silent empty page: a kind whose items cannot be stored says so when it is asked.
        _contract = new Lazy<CatalogPayloadContract>(() => CatalogPayloadContract.For(_runtime));
        MediaKind = definition.Kind;

        // The level a kind's items sit at, read the same way the dispatcher reads it when it assigns them
        // their identity, so the two cannot name different key spaces.
        _itemLevel = _runtime.Shape.Levels[0].Id;

        _titleFieldId = _runtime.Shape.Levels[0].Fields
            .FirstOrDefault(field => (field.Semantics & FieldSemantics.Title) != FieldSemantics.None)
            ?.FieldId;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind { get; }

    /// <inheritdoc />
    public async Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        RequireServiceable(query);
        RequireStorable();

        var page = await _records
            .PageAsync(
                MediaKind,
                _itemLevel,
                new CatalogRecordQuery(
                    query.TextSearch,
                    ByTitle: IsTitleOrder(query.SortFieldId),
                    query.SortDescending,
                    query.Page,
                    query.PageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return new ItemPage(
            [.. page.Records.Select(Project)],
            query.Page,
            query.PageSize,
            page.TotalCount);
    }

    /// <inheritdoc />
    public async Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (reference.Kind != MediaKind)
        {
            throw new ArronixException(
                CoreErrorCode.MediaItemNotFound,
                $"'{reference}' belongs to media kind '{reference.Kind}', and this source answers for "
                + $"'{MediaKind}'. A local identity is unique within its kind, so the same number here is "
                + "another entity, not this one.");
        }

        RequireStorable();

        // A merge leaves callers holding a reference the platform no longer keys by. Resolving it here is
        // what keeps an identity a consumer was once given from silently becoming a missing item.
        var canonical = _identity.Canonical(reference);
        var record = await _records.FindAsync(canonical, cancellationToken).ConfigureAwait(false);

        return record is null ? null : Project(record);
    }

    /// <inheritdoc />
    public async Task<MediaItemRef?> ResolveExternalAsync(
        ExternalId externalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A malformed identifier is not an item nobody has: it is a question the platform cannot ask, and
        // answering "not found" would let a caller conclude the catalog does not hold it.
        CatalogIdentity.RequireWellFormed(externalId, nameof(externalId));

        if (!_identity.TryIdentify(MediaKind, _itemLevel, externalId, out var reference))
        {
            return null;
        }

        var canonical = _identity.Canonical(reference);

        // Identity alone is not presence: a search resolves an identifier without materializing anything,
        // so an item is resolvable here only once a record for it exists.
        return await _records.FindAsync(canonical, cancellationToken).ConfigureAwait(false) is null
            ? null
            : canonical;
    }

    /// <inheritdoc />
    /// <exception cref="ArronixException">This build has no working surface to propose over.</exception>
    /// <remarks>
    /// An empty proposal is a proposal. A caller handed one has been told the surface ran and found
    /// nothing, which is a different statement from the true one.
    /// </remarks>
    public Task<WorkbenchProposal> ProposeAsync(
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbenchId);
        cancellationToken.ThrowIfCancellationRequested();

        throw new ArronixException(
            CoreErrorCode.InvalidConfiguration,
            $"Media kind '{MediaKind}' declares no working surface '{workbenchId}' this build can propose "
            + $"over. {NoWorkbenches}");
    }

    /// <inheritdoc />
    /// <exception cref="ArronixException">This build has no working surface to commit.</exception>
    /// <remarks>
    /// Refused rather than reported as an unsuccessful action, which is what a surface that ran and failed
    /// would answer with.
    /// </remarks>
    public Task<ActionResult> CommitAsync(WorkbenchCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        throw new ArronixException(
            CoreErrorCode.InvalidConfiguration,
            $"Media kind '{MediaKind}' has nothing to commit '{commit.WorkbenchId}' against. {NoWorkbenches}");
    }

    /// <summary>
    /// Refuses to answer for a kind whose items cannot be stored, instead of answering that it has none.
    /// </summary>
    /// <exception cref="ArronixException">The item type declares no contract entry point.</exception>
    /// <remarks>
    /// "No items" and "items cannot be held here" are different answers, and only one of them is true. A
    /// kind with no way to write a record would otherwise report an empty library forever, which reads as a
    /// working installation containing nothing.
    /// </remarks>
    private void RequireStorable() => _ = _contract.Value;

    /// <summary>
    /// Refuses a query this seam cannot answer, instead of answering a different question.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <exception cref="ArronixException">
    /// The query asks for a level, a parent, a filter or an ordering this build does not serve.
    /// </exception>
    /// <remarks>
    /// Every refusal here is something a caller explicitly asked for. Quietly dropping a filter and
    /// returning the unfiltered page, or substituting a different ordering for the one requested, produces
    /// a plausible answer to a question nobody asked — which is worse than saying no, because nothing
    /// downstream can tell it happened.
    /// </remarks>
    private void RequireServiceable(ItemQuery query)
    {
        if (query.Level != _itemLevel)
        {
            throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"Media kind '{MediaKind}' holds catalog records at level '{_itemLevel}'. Level "
                + $"'{query.Level}' has no records and is not a narrower view of the ones that exist.");
        }

        if (query.Parent is { } parent)
        {
            throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"Media kind '{MediaKind}' holds one level of catalog records, so no item contains another "
                + $"and '{parent}' cannot narrow a page.");
        }

        if (query.Filters.Count > 0)
        {
            throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"This build filters catalog records by title text only. It cannot answer a filter on "
                + $"{string.Join(", ", query.Filters.Keys.Order(StringComparer.Ordinal))}, and will not "
                + "return an unfiltered page as though it had.");
        }

        if (query.SortFieldId is { } sort && !string.Equals(sort, _titleFieldId, StringComparison.Ordinal))
        {
            throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"This build orders catalog records by '{_titleFieldId}' or by the level's natural order. "
                + $"It cannot order by '{sort}', and will not substitute a different order for the one asked "
                + "for.");
        }
    }

    /// <summary>Reads one record back into its typed item and projects it through the kind's own runtime.</summary>
    private ItemView Project(CatalogRecord record)
        => _runtime.Project(
            record.Reference,
            _contract.Value.Read(record.Payload, record.ContractMetadataHash),
            _identity);

    /// <summary>
    /// Whether a requested sort is the title order the store can take, rather than the level's natural one.
    /// </summary>
    /// <remarks>
    /// The title is the one fact held beside a record, so it is the one axis a page can be ordered by
    /// without reading every payload. An unstated sort means the level's natural order; any other stated
    /// field was already refused.
    /// </remarks>
    private bool IsTitleOrder(string? sortFieldId)
        => _titleFieldId is not null && string.Equals(sortFieldId, _titleFieldId, StringComparison.Ordinal);
}
