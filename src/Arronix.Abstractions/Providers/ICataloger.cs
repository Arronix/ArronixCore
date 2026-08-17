using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// An external authority that answers what a thing <i>is</i>.
/// </summary>
/// <remarks>
/// <para>
/// A cataloger supplies canonical facts — title, year, structure, identifiers — and those facts populate
/// the catalog record, the thing <see cref="Shape.LevelIdentity.HasCatalogRecord"/> declares a level to
/// have. It never decides what belongs in the library; that is a <see cref="ICurator"/>'s question. The
/// two are separate families precisely because "what is this?" and "do I want this?" are answered by
/// different services, under different privileges, at different times.
/// </para>
/// <para>
/// Capabilities are declared rather than assumed: two of the surveyed applications can ask their catalog
/// what changed since a given moment and two cannot, and a platform that assumes the former re-fetches
/// everything forever against a catalog that does not support it.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface ICataloger : IProvider
{
    /// <summary>
    /// Gets what this catalog can do.
    /// </summary>
    CatalogerCapabilities Capabilities { get; }

    /// <summary>
    /// Searches the catalog.
    /// </summary>
    /// <param name="invocation">The definition being searched, and its session.</param>
    /// <param name="query">What to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hits, each discriminated by the level it sits at.</returns>
    Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(
        ProviderInvocation invocation,
        MetadataQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one subject and everything beneath it that the policy admits.
    /// </summary>
    /// <param name="invocation">The definition being fetched from, and its session.</param>
    /// <param name="id">The catalog's identifier for the subject.</param>
    /// <param name="policy">Which sub-items should exist at all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fetched graph, or <see langword="null"/> when the catalog has no such subject.</returns>
    Task<MetadataGraph?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        SelectionPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subjects that changed since a given moment.
    /// </summary>
    /// <param name="invocation">The definition being asked, and its session.</param>
    /// <param name="since">The moment to compare against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The changed identifiers, or an empty list when
    /// <see cref="CatalogerCapabilities.DeltaSync"/> is not declared.
    /// </returns>
    Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a cataloger can do.
/// </summary>
[Flags]
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum CatalogerCapabilities
{
    /// <summary>Nothing beyond fetching a subject by identifier.</summary>
    None = 0,

    /// <summary>The catalog can be searched by text.</summary>
    Search = 1,

    /// <summary>The catalog can report what changed since a given moment.</summary>
    DeltaSync = 2,

    /// <summary>The catalog can suggest subjects the user has not asked for.</summary>
    Discovery = 4,

    /// <summary>The catalog can answer for many subjects in one call.</summary>
    BulkFetch = 8
}

/// <summary>
/// What to look for in a catalog.
/// </summary>
/// <param name="Kind">The media kind being searched for.</param>
/// <param name="Level">The level being searched for, or <see langword="null"/> for any.</param>
/// <param name="Text">The query text.</param>
/// <param name="Id">An identifier to resolve instead of searching, when one is known.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MetadataQuery(MediaKindId Kind, MediaLevelId? Level, string Text, ExternalId? Id);

/// <summary>
/// One result from a cataloger search.
/// </summary>
/// <param name="Id">The catalog's identifier for the subject.</param>
/// <param name="Level">The level the subject sits at.</param>
/// <param name="Title">The subject's title.</param>
/// <param name="Year">The year associated with the subject, when there is one.</param>
/// <param name="Disambiguation">What distinguishes this subject from others of the same title.</param>
/// <param name="Artwork">Images representing the subject.</param>
/// <remarks>
/// Discriminated by level, which is the direct fix for a surveyed search contract that returns a list of
/// untyped objects and leaves every caller to test the runtime type. The level comes free from the shape
/// model — a shape paying a dividend in an unrelated subsystem is good evidence it is the right shape.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MetadataSearchHit(
    ExternalId Id,
    MediaLevelId Level,
    string Title,
    int? Year,
    string? Disambiguation,
    IReadOnlyList<ArtworkRef> Artwork);

/// <summary>
/// A fetched subject and everything beneath it.
/// </summary>
/// <param name="Nodes">The nodes, each naming its level and its parent.</param>
/// <remarks>
/// The generalization of the ad hoc tuples the surveyed applications return from a catalog fetch, which
/// rhyme exactly with one another: a parent draft plus a list of child drafts, in a shape unique to each.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MetadataGraph(IReadOnlyList<MetadataNode> Nodes);

/// <summary>
/// One item's worth of fetched metadata.
/// </summary>
/// <param name="Level">The level this node sits at.</param>
/// <param name="Id">The catalog's identifier for it.</param>
/// <param name="ParentId">The catalog's identifier for its parent, or <see langword="null"/> at the root.</param>
/// <param name="Title">Its title.</param>
/// <param name="Fields">Its field values, keyed by the shape's field identifiers.</param>
/// <param name="Coordinates">Its positions in the coordinate spaces its level admits.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MetadataNode(
    MediaLevelId Level,
    ExternalId Id,
    ExternalId? ParentId,
    string Title,
    IReadOnlyDictionary<string, FieldValue> Fields,
    CoordinateSet Coordinates);
