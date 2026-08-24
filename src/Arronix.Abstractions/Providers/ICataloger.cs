using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>An external catalog authority, including recognition of its own identifier markers.</summary>
/// <remarks>
/// The non-generic floor lets the kind-blind host compose identifier readings after constrained activation. Shaped
/// search and fetch operations remain on <see cref="ICataloger{TItem}"/>.
/// </remarks>
public interface ICataloger : IProvider
{
    /// <summary>
    /// Gets the external identifier scheme this cataloger is the authority for, lower-case.
    /// </summary>
    /// <remarks>
    /// The host routes catalog work by this scheme rather than by provider identifier or implementation
    /// type. Every marker <see cref="ReadExternalIds"/> recognizes is in this scheme, and every item the
    /// cataloger returns carries exactly one identifier in it as that catalog's identity. An item may also
    /// carry identifiers from other catalogs as cross-references.
    /// </remarks>
    string CatalogScheme { get; }

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
/// <para>
/// The generic is the boundary: a movie cataloger returns <c>Movie</c>, a television cataloger returns its
/// series aggregate, and neither can substitute a field dictionary. <typeparamref name="TItem"/> is the only
/// place an author names it — registration reads the pairing back from this contract as the implementation
/// actually implements it — and admission accepts the provider only while a media kind supplying that exact
/// item type is active.
/// </para>
/// <para>
/// Every returned item carries exactly one identifier in <see cref="ICataloger.CatalogScheme"/> among its
/// <see cref="IMediaEntity.ExternalIds"/>; that identifier is the item's catalog identity. Durable library
/// identity is assigned by the host and is not visible here.
/// </para>
/// </remarks>
public interface ICataloger<TItem> : ICataloger, IClosedCataloger
    where TItem : class, IMediaItem
{
    CatalogerCapabilities Capabilities { get; }

    Task<IReadOnlyList<TItem>> SearchAsync(
        ProviderInvocation invocation,
        CatalogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches one item.</summary>
    /// <param name="invocation">The call context.</param>
    /// <param name="id">
    /// The identifier asked for, in <see cref="ICataloger.CatalogScheme"/>. It may be an alias this catalog
    /// redirects.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item the catalog resolved to, or <see langword="null"/> when it holds none.</returns>
    Task<TItem?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        CancellationToken cancellationToken = default);

    /// <summary>Lists records this catalog reports as changed since an instant.</summary>
    /// <param name="invocation">The call context.</param>
    /// <param name="since">The instant to report changes after.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Identifiers in <see cref="ICataloger.CatalogScheme"/>.</returns>
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
/// <param name="Id">
/// An identifier to resolve instead, when known. Its scheme may belong to another catalog when the selected
/// catalog supports cross-catalog lookup.
/// </param>
public sealed record CatalogQuery(string Text, ExternalId? Id = null);
