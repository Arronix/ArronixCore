using Arronix.Abstractions.Media;

namespace Arronix.Abstractions.Providers;

/// <summary>An external list that proposes items in the media type's own shape.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
public interface ICurator<TItem> : IProvider
    where TItem : class, IMediaItem
{
    TimeSpan MinimumRefreshInterval { get; }

    Task<CuratedListFetch<TItem>> FetchAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default);
}

/// <summary>The typed result of fetching one curated list.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
/// <param name="Items">The shaped items the source proposed.</param>
/// <param name="AnyFailure">Whether part of the fetch failed.</param>
/// <param name="Warnings">Operator-visible warnings.</param>
public sealed record CuratedListFetch<TItem>(
    IReadOnlyList<TItem> Items,
    bool AnyFailure,
    IReadOnlyList<string> Warnings)
    where TItem : class, IMediaItem;
