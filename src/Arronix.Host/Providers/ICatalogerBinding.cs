using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Providers;

/// <summary>
/// One cataloger, already bound to the item type it was closed over.
/// </summary>
/// <remarks>
/// The returned items are the media type's own; they cross this seam as <see cref="IMediaItem"/> because
/// the caller is kind-blind, and the exact type is intact on every value.
/// </remarks>
internal interface ICatalogerCall
{
    /// <summary>Searches the catalog.</summary>
    /// <param name="invocation">The call context.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The results, in the order the catalog returned them.</returns>
    Task<IReadOnlyList<IMediaItem>> SearchAsync(
        ProviderInvocation invocation,
        CatalogQuery query,
        CancellationToken cancellationToken);

    /// <summary>Fetches one item.</summary>
    /// <param name="invocation">The call context.</param>
    /// <param name="id">The identifier, which may be an alias the catalog redirects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when the catalog holds none.</returns>
    Task<IMediaItem?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        CancellationToken cancellationToken);
}

/// <summary>
/// A media runtime's ability to recognize a cataloger closed over its own item type.
/// </summary>
/// <remarks>
/// <para>
/// The sanctioned one-way erasure for catalog work. A kind-blind caller — an HTTP route, a scheduled
/// refresh — cannot write <c>ICataloger&lt;Movie&gt;</c>, and must not resolve it by constructing a generic
/// method at run time. The runtime that already holds the closed item type performs the one type test the
/// contract needs, and hands back something callable.
/// </para>
/// <para>
/// Host-internal, and implemented only by the runtime the model factory builds. A media extension neither
/// implements nor sees it.
/// </para>
/// </remarks>
internal interface ICatalogerBinding
{
    /// <summary>
    /// Binds a provider when it is a cataloger for this kind's item type.
    /// </summary>
    /// <param name="provider">The activated provider instance.</param>
    /// <returns>The bound cataloger, or <see langword="null"/> when it is not one for this item type.</returns>
    ICatalogerCall? Bind(IProvider provider);
}
