using Arronix.Client.Diagnostics;

namespace Arronix.Client.Contracts;

/// <summary>
/// Reads the installation and sheds the bytes it no longer names, as one serialized transaction.
/// </summary>
/// <remarks>
/// <para>
/// The loader serializes its own reads and nothing else. A sweep computed from an older read finishing
/// after a newer one would evict exactly the addresses the newer installation had just fetched, so load and
/// sweep have to be one step and that step has to be the only one. Every caller uses this: an operator
/// pressing reload and a tab reacting to an extension changing state race each other otherwise.
/// </para>
/// <para>
/// Each step is contained separately — one failing must not cancel the next — and each subscriber is told
/// in turn, so one refusal costs the others nothing. An unsound process is contained nowhere, and
/// cancellation belongs to the caller that asked for it.
/// </para>
/// </remarks>
public sealed class ContractReloader
{
    private readonly MediaContractLoader _contracts;
    private readonly ContractStoreJanitor _janitor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractReloader"/> class.
    /// </summary>
    /// <param name="contracts">The loader that reads and proves an installation.</param>
    /// <param name="janitor">Where the bytes an installation no longer names are discarded.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ContractReloader(MediaContractLoader contracts, ContractStoreJanitor janitor)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(janitor);

        _contracts = contracts;
        _janitor = janitor;
    }

    /// <summary>Occurs when a reload has finished, successfully or not, and after it has swept.</summary>
    /// <remarks>
    /// Raised while this reload still holds its lease, so no later reload can overtake it. A subscriber
    /// must therefore not call <see cref="ReloadAsync"/> back.
    /// </remarks>
    public event EventHandler? Completed;

    /// <summary>Gets why the last reload failed, or <see langword="null"/> when it did not.</summary>
    /// <remarks>
    /// The loader names every outcome it can describe in its own report, so a value here is a defect in
    /// this client — a consumer refusing a notification, most likely — rather than a fact about the
    /// installation. Nothing awaits a reload started by an event, so this is where one can be seen.
    /// </remarks>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// Reads the installation, discards the stored bytes it does not name, and says so.
    /// </summary>
    /// <param name="cancellationToken">Abandons the reload.</param>
    /// <returns>A task that completes when this reload has finished and every subscriber has been told.</returns>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            LastFailure = null;

            try
            {
                await _contracts.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                LastFailure = failure.Message;
            }

            try
            {
                // Whatever the loader last proved, which it records before it notifies anyone. Bytes only:
                // residency is untouched.
                if (_contracts.Report is { } report)
                {
                    await _janitor.SweepAsync(report).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                LastFailure = failure.Message;
            }

            // Inside the lease. A reload that announced after releasing would let the next one read, sweep
            // and record over it, so subscribers would learn of two installations out of order.
            Announce();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Tells each subscriber, one at a time.</summary>
    /// <remarks>
    /// Raising the delegate whole would let the first refusal deny every later subscriber the state this
    /// reload produced, and fault a task nothing awaits.
    /// </remarks>
    private void Announce()
    {
        if (Completed is not { } subscribers)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                LastFailure = failure.Message;
            }
        }
    }
}
