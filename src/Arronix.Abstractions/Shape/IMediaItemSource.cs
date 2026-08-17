using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Wire;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Projects an extension's own catalog, and answers for the working surfaces it declared.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A design in which extensions only declare a structure, with no catalog of
/// their own, ships an interface that can render nothing but empty lists until a persistence layer and a
/// metadata pipeline exist. This seam is the catalog-and-library split made into the runtime topology:
/// <b>the extension projects its own catalog; the host stores only library state</b> — what is wanted,
/// which files satisfy what, which variant is chosen, what belongs to which group — and merges the two on
/// read.
/// </para>
/// <para>
/// It qualifies for promotion on the strictest reading of the promotion rule: four demonstrated
/// implementers ship alongside it.
/// </para>
/// <para>
/// The working-surface methods are here rather than on a separate seam because the proposal they produce
/// is domain knowledge about the extension's own items — the same knowledge the catalog projection
/// needs — and because a separate seam would need its own capability, its own registration and its own
/// failure mode for no gain.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaItemSource
{
    /// <summary>
    /// Gets the media kind this source serves.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Returns one page of items.
    /// </summary>
    /// <param name="query">What to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page.</returns>
    Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one item.
    /// </summary>
    /// <param name="reference">The item wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when the catalog has no such item.</returns>
    Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an external catalog identifier to an item.
    /// </summary>
    /// <param name="externalId">The identifier to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when the identifier is unknown here.</returns>
    Task<MediaItemRef?> ResolveExternalAsync(
        ExternalId externalId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proposes the decisions for one of the extension's declared working surfaces.
    /// </summary>
    /// <param name="workbenchId">The surface being opened.</param>
    /// <param name="inputs">The values the caller collected, keyed by parameter identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The proposed rows, with the extension's confidence in each.</returns>
    Task<WorkbenchProposal> ProposeAsync(
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the user's amended decisions, all together.
    /// </summary>
    /// <param name="commit">The rows as the user left them, and the ones they excluded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the platform took the commit on, and what to tell the user.</returns>
    Task<ActionResult> CommitAsync(WorkbenchCommit commit, CancellationToken cancellationToken = default);
}
