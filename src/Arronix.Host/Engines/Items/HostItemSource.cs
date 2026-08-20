using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Media;


namespace Arronix.Host.Engines.Items;

/// <summary>
/// The item-store engine (design table row E9) in its pre-storage form: the host's own answer for a
/// declared kind's items, behind the existing <see cref="IMediaItemSource"/> seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the host owns this at all.</b> A declared media kind ships data and no code, so there is no
/// extension object left to project a catalog. The binder is right to require the slot rather than admit a
/// kind whose items nothing can answer for: every consumer downstream of the registry —
/// <see cref="MediaItemProjection"/>, the completeness and affordance calculators, and the store's own
/// span checks — reaches an admitted kind's items through this seam and has no other route.
/// </para>
/// <para>
/// <b>Why it answers with nothing.</b> The host's store (<c>IMediaStore</c>) deliberately holds one half of
/// an item: the half the user owns — what is wanted, which files satisfy what, which variant is chosen,
/// what belongs to which group. It holds no item rows, and a <c>LibraryFacet</c> is not one:
/// <see cref="ItemView"/> is the <i>other</i> half by contract, and it names monitoring state, the selected
/// variant and file counts as things it must never carry. So there is nothing here to read a catalog out
/// of, and inventing titles or coordinates the platform does not have would be worse than an empty answer —
/// an <see cref="ItemView"/> with a fabricated title is a match candidate, and it would silently poison
/// entry resolution. Until item rows and a metadata pipeline exist, the honest projection of an empty
/// library is an empty page.
/// </para>
/// <para>
/// <b>What replaces it.</b> The storage milestone (<c>docs/design/storage-layer.md</c>) gives the host item
/// rows and this class starts reading them; phase 6 of the declarative rollout then dissolves the four
/// extension-side item sources into it. Nothing above this seam changes when that happens, which is the
/// whole point of putting it here now: the shape of the answer is fixed, only its source moves.
/// </para>
/// <para>
/// Read-only by construction. The two working-surface methods answer for surfaces this milestone cannot
/// propose over — a proposal is a statement about items, and there are none — so they refuse quietly rather
/// than pretending to have committed something.
/// </para>
/// </remarks>
internal sealed class HostItemSource : IMediaItemSource
{
    private const string NoItemRowsYet =
        "This build has no item rows: the host store holds library state only, and no catalog is projected "
        + "for a declared media kind until the storage milestone lands.";

    /// <summary>
    /// Initializes a new instance of the <see cref="HostItemSource"/> class.
    /// </summary>
    /// <param name="definition">The kind's validated definition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    internal HostItemSource(ValidatedDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        MediaKind = definition.Kind;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind { get; }

    /// <inheritdoc />
    public Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ItemPage([], query.Page, query.PageSize, 0));
    }

    /// <inheritdoc />
    public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ItemView?>(null);
    }

    /// <inheritdoc />
    public Task<MediaItemRef?> ResolveExternalAsync(
        ExternalId externalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<MediaItemRef?>(null);
    }

    /// <inheritdoc />
    public Task<WorkbenchProposal> ProposeAsync(
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbenchId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new WorkbenchProposal { WorkbenchId = workbenchId, Rows = [] });
    }

    /// <inheritdoc />
    public Task<ActionResult> CommitAsync(WorkbenchCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ActionResult(false, NoItemRowsYet, null, null));
    }
}
