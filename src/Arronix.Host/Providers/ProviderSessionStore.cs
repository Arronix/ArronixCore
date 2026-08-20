using System.Collections.Concurrent;
using Arronix.Abstractions.Providers;


namespace Arronix.Host.Providers;

/// <summary>
/// The scratch space a provider is allowed to keep between calls.
/// </summary>
/// <remarks>
/// <para>
/// Providers are stateless, which raises an obvious objection: a login cookie, a capability snapshot and a
/// last-seen marker are all things a real provider legitimately keeps. They are kept here instead — host
/// owned, partitioned per configured definition, and handed to the provider on the invocation.
/// </para>
/// <para>
/// That is not a workaround for statelessness; it is what makes statelessness affordable. The state now has
/// an owner that can expire it, clear it when the definition changes and refuse to share it between two
/// definitions of the same provider, none of which a field on a shared singleton could do.
/// </para>
/// </remarks>
/// <param name="clock">The clock lifetimes are measured against.</param>
public sealed class ProviderSessionStore(TimeProvider clock)
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, Entry>> _partitions = new();
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Gets the session for one configured definition.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <returns>Its session.</returns>
    public IProviderSessionStore For(int definitionId)
        => new Partition(_partitions.GetOrAdd(definitionId, static _ => new ConcurrentDictionary<string, Entry>()), _clock);

    /// <summary>
    /// Discards everything stored for one definition.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <remarks>
    /// Called when a definition's settings change. A cookie obtained with the old credentials is not merely
    /// useless after an edit, it is misleading: it makes a wrong password look like a working one until it
    /// expires.
    /// </remarks>
    public void Clear(int definitionId) => _partitions.TryRemove(definitionId, out _);

    private readonly record struct Entry(string? Value, DateTimeOffset? ExpiresAt);

    private sealed class Partition(ConcurrentDictionary<string, Entry> entries, TimeProvider clock)
        : IProviderSessionStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            cancellationToken.ThrowIfCancellationRequested();

            if (!entries.TryGetValue(key, out var entry))
            {
                return Task.FromResult<string?>(null);
            }

            if (entry.ExpiresAt is { } expiry && expiry <= clock.GetUtcNow())
            {
                entries.TryRemove(key, out _);
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult(entry.Value);
        }

        public Task SetAsync(
            string key,
            string? value,
            TimeSpan? lifetime = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            cancellationToken.ThrowIfCancellationRequested();

            if (value is null)
            {
                entries.TryRemove(key, out _);
                return Task.CompletedTask;
            }

            entries[key] = new Entry(value, lifetime is { } span ? clock.GetUtcNow() + span : null);
            return Task.CompletedTask;
        }
    }
}
