using Arronix.Abstractions.Caching;

namespace Arronix.Common.Caching;

/// <summary>
/// A group of caches a host can hand to one extension and later take back whole.
/// </summary>
/// <remarks>
/// The extension only ever sees the <see cref="ICacheProvider"/> face. Release must stop new operations,
/// wait for the ones already running, and drop every value, key and delegate the group held, because all
/// of those can come from a collectible load context.
/// </remarks>
public interface ICacheNamespace : ICacheProvider
{
    /// <summary>Gets the group's name.</summary>
    string Name { get; }

    /// <summary>Gets a value indicating whether the group has stopped admitting work.</summary>
    bool IsReleased { get; }

    /// <summary>Closes the group, waits for what it admitted, and drops what its caches held.</summary>
    /// <returns>A task that completes when nothing in the group is running or referenced.</returns>
    /// <remarks>Idempotent; every caller awaits the same completion.</remarks>
    ValueTask ReleaseAsync();
}
