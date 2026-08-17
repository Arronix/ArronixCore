using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// A source of release candidates.
/// </summary>
/// <remarks>
/// <para>
/// One search method, not an overload set. The surveyed applications carry between one and seven fetch
/// overloads each, and the arity of the set is itself per-media-kind — so overload resolution, which is a
/// compile-time mechanism, would have to survive an extension boundary. It cannot. A media kind is added
/// by declaring a search, never by editing this interface.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IIndexer : IProvider
{
    /// <summary>
    /// Reports what this configured source can do.
    /// </summary>
    /// <param name="invocation">The definition asking, and its session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The source's profile.</returns>
    Task<IndexerProfile> DescribeAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one query.
    /// </summary>
    /// <param name="invocation">The definition being queried, and its session.</param>
    /// <param name="query">The query. Arguments the source did not declare are ignored rather than sent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The releases found.</returns>
    Task<ReleaseQueryResult> SearchAsync(
        ProviderInvocation invocation,
        ReleaseQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the artifact that starts a transfer.
    /// </summary>
    /// <param name="invocation">The definition being fetched from, and its session.</param>
    /// <param name="link">The address carried by the release candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fetched content.</returns>
    Task<ReleaseFetch> FetchAsync(
        ProviderInvocation invocation,
        Uri link,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of one query against one release source.
/// </summary>
/// <param name="Releases">The releases found. Reuses the shared release candidate.</param>
/// <param name="IsPartialResult">Whether the source failed part-way and the list is incomplete.</param>
/// <param name="Warnings">Anything an operator should know about how the query went.</param>
/// <remarks>
/// An envelope rather than a bare list, because a partial answer and an empty answer must not look alike:
/// treating a truncated result as complete is how an automatic search concludes that nothing exists.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ReleaseQueryResult(
    IReadOnlyList<ReleaseCandidate> Releases,
    bool IsPartialResult,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The artifact that starts a transfer.
/// </summary>
/// <param name="Content">The bytes.</param>
/// <param name="FileName">The file name the source suggested, when it did.</param>
/// <param name="ContentType">The media type the source reported, when it did.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ReleaseFetch(ReadOnlyMemory<byte> Content, string? FileName, string? ContentType);
