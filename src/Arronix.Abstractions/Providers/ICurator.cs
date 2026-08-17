using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// An external list that answers which things you <i>want</i>.
/// </summary>
/// <remarks>
/// <para>
/// A curator selects: a watchlist, a chart, a follow list maintained outside the platform, whose members
/// are the items that belong in the library. It says nothing about what any of them <i>are</i> — the
/// canonical facts come from a <see cref="ICataloger"/> afterwards, keyed by the identifiers the curator
/// hands over.
/// </para>
/// <para>
/// A fifth provider family, present in every surveyed application and absent from the platform's original
/// vocabulary entirely. It is gated by its own capability rather than folded into metadata, because
/// fetching a list of things a user follows elsewhere is a different privilege from fetching the catalog
/// record of something they already own.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface ICurator : IProvider
{
    /// <summary>
    /// Gets the media kind the list yields items for.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Gets the shortest interval the source is willing to be fetched at.
    /// </summary>
    TimeSpan MinimumRefreshInterval { get; }

    /// <summary>
    /// Fetches the list.
    /// </summary>
    /// <param name="invocation">The definition being fetched, and its session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items, and whether the fetch was complete.</returns>
    Task<CuratedListFetch> FetchAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of fetching a curated list.
/// </summary>
/// <param name="Items">The items the list yielded.</param>
/// <param name="AnyFailure">Whether part of the fetch failed, so that the list is incomplete.</param>
/// <param name="Warnings">Anything an operator should know about how the fetch went.</param>
/// <remarks>
/// An envelope rather than a bare list. Two of the surveyed applications evolved to exactly this to carry
/// partial-failure information; the other two still return a bare list and cannot distinguish "the list is
/// empty" from "the list could not be read" — which, for a source that also removes items, is the
/// difference between doing nothing and deleting everything.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CuratedListFetch(
    IReadOnlyList<CuratedItem> Items,
    bool AnyFailure,
    IReadOnlyList<string> Warnings);

/// <summary>
/// One item a curated list yielded.
/// </summary>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CuratedItem
{
    /// <summary>
    /// Gets the level the item sits at.
    /// </summary>
    public required MediaLevelId Level { get; init; }

    /// <summary>
    /// Gets the external identifiers the item is known by. At least one is needed to resolve it.
    /// </summary>
    public required IReadOnlyList<ExternalId> ExternalIds { get; init; }

    /// <summary>
    /// Gets the item's title, for presentation and for fuzzy resolution.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the year associated with the item, when the list carries one.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets when the item was or will be released, when the list carries it.
    /// </summary>
    public DateTimeOffset? ReleaseDate { get; init; }

    /// <summary>
    /// Gets the positions within the item the list singled out. This generalizes the one place a
    /// surveyed list contract lets an intermediate grouping escape into a cross-cutting provider
    /// interface — and here it escapes as a coordinate rather than as a reference to an entity.
    /// </summary>
    public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;
}
