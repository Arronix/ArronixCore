using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>An identifier a curator assigns within its own list.</summary>
/// <remarks>
/// A separate type from <see cref="ExternalId"/> so a list position cannot be passed where a catalog
/// identity is meant.
/// </remarks>
public readonly record struct CuratedEntryId
{
    private CuratedEntryId(string value) => Value = value;

    /// <summary>Gets the identifier as the curator writes it.</summary>
    public string Value { get; }

    /// <summary>Creates a curator-owned entry identifier.</summary>
    /// <param name="value">The identifier as the curator writes it.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty or white space.</exception>
    public static CuratedEntryId Of(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new CuratedEntryId(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>One item a curated list proposes, referenced by catalog identity.</summary>
/// <param name="CatalogId">The catalog identity of the proposed item.</param>
/// <param name="EntryId">The curator's own identifier for this entry, when it keeps one.</param>
public sealed record CuratedReference(ExternalId CatalogId, CuratedEntryId? EntryId = null);

/// <summary>An external list that proposes items by catalog identity.</summary>
/// <typeparam name="TItem">
/// The media-owned item type the list is about, named once here and read back from this contract by
/// registration. No item of that type crosses this contract.
/// </typeparam>
/// <remarks>
/// A curator proposes what to look at; the cataloger owning each reference remains the authority on what
/// the item is.
/// </remarks>
public interface ICurator<TItem> : IProvider, IClosedCurator
    where TItem : class, IMediaItem
{
    TimeSpan MinimumRefreshInterval { get; }

    Task<CuratedListFetch<TItem>> FetchAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of fetching one curated list.</summary>
/// <typeparam name="TItem">The media-owned item type the list is about.</typeparam>
/// <param name="Items">The references the source proposed, in its own order.</param>
/// <param name="AnyFailure">Whether part of the fetch failed.</param>
/// <param name="Warnings">Operator-visible warnings.</param>
public sealed record CuratedListFetch<TItem>(
    IReadOnlyList<CuratedReference> Items,
    bool AnyFailure,
    IReadOnlyList<string> Warnings)
    where TItem : class, IMediaItem;
