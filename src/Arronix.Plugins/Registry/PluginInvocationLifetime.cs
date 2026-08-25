using Arronix.Abstractions.Plugins;
using Arronix.Common.Contributions;
using Arronix.Common.Lifetimes;

namespace Arronix.Plugins.Registry;

/// <summary>
/// One extension runtime's licence to be called, and the thing teardown waits on before disposing it.
/// </summary>
/// <remarks>
/// <para>
/// The rule the platform's dispatch paths follow: select contributions under the publication read gate and
/// take a lease from each owning runtime, release the gate, then invoke. Withdrawal closes the lifetime
/// under the publication write gate and removes the published result, so a caller either holds a lease and
/// is waited for, or cannot get one.
/// </para>
/// <para>
/// It is separate from <see cref="PluginPublicationGate"/> because that gate is a
/// <see cref="System.Threading.ReaderWriterLockSlim"/> and is thread-affine, so it can never be held across
/// an <c>await</c> of extension code. This can.
/// </para>
/// </remarks>
internal sealed class PluginInvocationLifetime : IInvocationLifetime
{
    private readonly QuiescenceGate _gate = new();

    internal PluginInvocationLifetime(PluginId plugin) => Plugin = plugin;

    /// <summary>Gets the extension whose invocations this governs.</summary>
    internal PluginId Plugin { get; }

    /// <inheritdoc />
    public bool IsClosed => _gate.IsClosed;

    /// <summary>Gets how many invocations are still holding a lease.</summary>
    internal int Outstanding => _gate.Admitted;

    /// <inheritdoc />
    public bool TryEnter(out IDisposable? ticket) => _gate.TryEnter(out ticket);

    /// <summary>Closes the runtime to new invocations. Called under the publication write gate.</summary>
    internal void Close() => _gate.Close();

    /// <summary>Closes the runtime and waits for every lease already taken.</summary>
    /// <returns>A task that completes when nothing is still invoking this extension.</returns>
    internal Task DrainAsync() => _gate.DrainAsync();
}
