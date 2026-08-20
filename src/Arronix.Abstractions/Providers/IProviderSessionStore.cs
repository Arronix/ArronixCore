
namespace Arronix.Abstractions.Providers;

/// <summary>
/// Per-definition scratch state a provider needs between calls: a session token, the last item seen, a
/// cached capability snapshot.
/// </summary>
/// <remarks>
/// Host-owned and scoped to one definition, which is what makes a stateless provider practical. Without
/// it, the only place to keep such state would be a field on the implementation — and a field on a shared
/// implementation is exactly the mutation this design set out to make unrepresentable.
/// </remarks>
public interface IProviderSessionStore
{
    /// <summary>
    /// Reads a value.
    /// </summary>
    /// <param name="key">The key, scoped to the calling definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value, or <see langword="null"/> when the key is unset or expired.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a value.
    /// </summary>
    /// <param name="key">The key, scoped to the calling definition.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the key.</param>
    /// <param name="lifetime">How long the value stays readable, or <see langword="null"/> for the store's default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the value is stored.</returns>
    Task SetAsync(
        string key,
        string? value,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default);
}
