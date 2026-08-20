using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>An external catalog authority, including recognition of its own identifier markers.</summary>
/// <remarks>
/// The non-generic floor lets the kind-blind host compose identifier readings after DI activation. Shaped
/// search and fetch operations remain on <see cref="ICataloger{TItem}"/>.
/// </remarks>
public interface ICataloger : IProvider
{
    /// <summary>
    /// Recognizes identifiers from this catalog when they are embedded in a release, file, or folder name.
    /// </summary>
    /// <param name="text">The complete text being interpreted.</param>
    /// <returns>Every unambiguous marker this cataloger recognized, in source order.</returns>
    /// <remarks>
    /// Recognition is local and side-effect free. It must not perform a provider call. The cataloger owns
    /// both the identifier namespace and the accepted marker spellings; media types must not copy vendor
    /// names or identifier regular expressions into their parsers.
    /// </remarks>
    IReadOnlyList<ExternalIdReading> ReadExternalIds(string text);
}

/// <summary>An external authority that returns the media type's own catalog shape.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
/// <remarks>
/// The generic is the boundary: a movie cataloger returns <c>Movie</c>, a television cataloger returns its
/// series aggregate, and neither can substitute a field dictionary. Registration pairs this contract to
/// the media kind whose typed definition declares the same item type.
/// </remarks>
public interface ICataloger<TItem> : ICataloger
    where TItem : class, IMediaItem
{
    CatalogerCapabilities Capabilities { get; }

    Task<IReadOnlyList<TItem>> SearchAsync(
        ProviderInvocation invocation,
        CatalogQuery query,
        CancellationToken cancellationToken = default);

    Task<TItem?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}

[Flags]
public enum CatalogerCapabilities
{
    None = 0,
    Search = 1,
    DeltaSync = 2,
    Discovery = 4,
    BulkFetch = 8,
    IdentifierRedirects = 16
}

/// <summary>A kind-bound catalog search request.</summary>
/// <param name="Text">Text to search for.</param>
/// <param name="Id">An identifier to resolve instead, when known.</param>
public sealed record CatalogQuery(string Text, ExternalId? Id = null);
