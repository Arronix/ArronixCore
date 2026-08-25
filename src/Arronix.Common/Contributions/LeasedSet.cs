using System.Collections;

namespace Arronix.Common.Contributions;

/// <summary>
/// A set of leases acquired together, released together.
/// </summary>
/// <typeparam name="TValue">What was leased.</typeparam>
/// <remarks>
/// Enumerating a plain list of leases and disposing each element inside the loop leaks every lease after
/// the one whose callback threw, and a leaked lease is an extension that can never be torn down. The set
/// owns them all, so one <c>using</c> around the loop releases the whole acquisition however it ends.
/// </remarks>
internal sealed class LeasedSet<TValue> : IReadOnlyList<TValue>, IDisposable
{
    private readonly List<Leased<TValue>> _leases;
    private int _disposed;

    internal LeasedSet(List<Leased<TValue>> leases) => _leases = leases;

    /// <summary>Gets how many values were leased.</summary>
    public int Count => _leases.Count;

    /// <summary>Gets one leased value.</summary>
    /// <param name="index">Its position.</param>
    /// <returns>The value.</returns>
    public TValue this[int index] => _leases[index].Value;

    /// <inheritdoc />
    public IEnumerator<TValue> GetEnumerator()
    {
        foreach (var lease in _leases)
        {
            yield return lease.Value;
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Releases every lease in the set. Releasing twice releases once.
    /// </summary>
    /// <exception cref="AggregateException">
    /// One or more releases threw. Every lease was still attempted first: stopping at the first failure
    /// would leak the tickets after it, and a leaked ticket is an extension that can never be torn down.
    /// </exception>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<Exception>? failures = null;

        foreach (var lease in _leases)
        {
            try
            {
                lease.Dispose();
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more contribution leases could not be released. Every lease in the set was still "
                + "attempted.",
                failures);
        }
    }
}
