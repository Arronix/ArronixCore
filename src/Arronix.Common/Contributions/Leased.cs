namespace Arronix.Common.Contributions;

/// <summary>
/// Something a plugin contributed, together with the ticket that keeps its runtime alive while it is used.
/// </summary>
/// <typeparam name="TValue">What was leased.</typeparam>
/// <remarks>
/// Registries hand out this rather than the bare object, so the implementation is unreachable without a
/// ticket and the ticket is what teardown waits for. Dispose it when the call — including everything it
/// awaits — has finished. It carries no equality: comparing one would run the contributed object's own
/// equality, which is plugin code, outside the lifetime this handle exists to guard.
/// </remarks>
internal sealed class Leased<TValue> : IDisposable
{
    private IDisposable? _ticket;

    internal Leased(TValue value, IDisposable? ticket)
    {
        Value = value;
        _ticket = ticket;
    }

    /// <summary>Gets the contributed value.</summary>
    internal TValue Value { get; }

    /// <summary>Releases the ticket. Releasing twice releases once.</summary>
    public void Dispose() => Interlocked.Exchange(ref _ticket, null)?.Dispose();
}
