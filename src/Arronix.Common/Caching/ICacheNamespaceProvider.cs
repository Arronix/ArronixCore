using Arronix.Abstractions.Caching;

namespace Arronix.Common.Caching;

/// <summary>
/// The host-side half of caching: the ability to hand out a group of caches and later take the whole group
/// back.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not part of <see cref="ICacheProvider"/> and deliberately not in the contract assembly. An
/// extension that could release a namespace could release another extension's, and an extension that could
/// enumerate namespaces could see that another exists. Both are host concerns, so the control lives on the
/// host side of the boundary and the extension keeps the plain provider it was always given.
/// </para>
/// <para>
/// It exists because a cache is the one platform service that holds extension objects indefinitely by
/// design. A cached value, the key's factory delegate, and the closed generic cache instance itself can all
/// come from a collectible load context, so a host that cannot take the group back cannot unload the
/// extension that filled it.
/// </para>
/// </remarks>
public interface ICacheNamespaceProvider : ICacheProvider
{
    /// <summary>
    /// Opens a releasable group of caches.
    /// </summary>
    /// <param name="name">
    /// The group's name, unique within the provider. It identifies the group in diagnostics; it does not
    /// namespace partition names, which is the caller's own concern.
    /// </param>
    /// <returns>The namespace. Caches taken from it belong to it until it is released.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">A live namespace already has that name.</exception>
    CacheNamespace CreateNamespace(string name);
}
