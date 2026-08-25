using Arronix.Abstractions.Caching;

namespace Arronix.Common.Caching;

/// <summary>
/// The host-side half of caching: the ability to hand out a group of caches and later take the whole group
/// back.
/// </summary>
/// <remarks>
/// Host-side and not in the contract assembly: an extension that could release a namespace could release
/// another's. It exists because a cached value, its factory, and the constructed generic cache instance can
/// all come from a collectible context, so a host that cannot take the group back cannot unload it.
/// </remarks>
public interface ICacheNamespaceProvider : ICacheProvider
{
    /// <summary>
    /// Opens a releasable group of caches.
    /// </summary>
    /// <param name="name">The group's name, unique within the provider.</param>
    /// <returns>The namespace. Caches taken from it belong to it until it is released.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">A live namespace already has that name.</exception>
    ICacheNamespace CreateNamespace(string name);
}
