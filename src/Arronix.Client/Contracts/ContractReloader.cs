using Arronix.Client.Diagnostics;

namespace Arronix.Client.Contracts;

/// <summary>Every change to what this page holds of an installation, as one serialized transaction.</summary>
/// <remarks>
/// The one thing in this client that loads, sweeps or empties the store. A step outside this lease could
/// have its bytes evicted by a sweep computed from an older read, or pair an installation with a store
/// another step has since changed, so there is no second path to any of them and an architecture rule holds
/// it that way. Each transaction is numbered and sealed here, over the keys it left behind.
/// </remarks>
internal sealed class ContractReloader
{
    private readonly MediaContractLoader _contracts;
    private readonly ContractStoreJanitor _janitor;
    private readonly ContractStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _sequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractReloader"/> class.
    /// </summary>
    /// <param name="contracts">The loader that reads and proves an installation.</param>
    /// <param name="janitor">Where the bytes an installation no longer names are discarded.</param>
    /// <param name="store">This browser's contract store.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ContractReloader(MediaContractLoader contracts, ContractStoreJanitor janitor, ContractStore store)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(janitor);
        ArgumentNullException.ThrowIfNull(store);

        _contracts = contracts;
        _janitor = janitor;
        _store = store;
    }

    /// <summary>Reads the installation and discards the stored bytes it does not name.</summary>
    /// <param name="cancellationToken">Abandons the reload.</param>
    /// <returns>This transaction's sealed record.</returns>
    public async Task<ContractReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var failures = new List<ContractFailure>();
            ContractLoadReport? read = null;

            try
            {
                read = await _contracts.LoadInstallationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                failures.Add(new ContractFailure(ContractFailureStage.Load, failure.Message));
            }

            try
            {
                // This read's own installation, so no sweep is ever computed from an older one. Bytes only.
                if (read is { } proved)
                {
                    await _janitor.SweepStoreAsync(proved).ConfigureAwait(false);
                }
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                failures.Add(new ContractFailure(ContractFailureStage.Sweep, failure.Message));
            }

            // A read that could not produce a report leaves the last one standing.
            return await SealAsync(read ?? _contracts.Report, failures).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Discards every stored byte, which is what a clean start means.</summary>
    /// <param name="cancellationToken">Abandons the discard.</param>
    /// <returns>This transaction's sealed record.</returns>
    /// <remarks>
    /// The bytes, not the assemblies: a browser cannot unload one, so the next load is a clean-store load
    /// and whatever this page holds stays where it is. Under the same lease as a reload, because emptying
    /// the store beside one would leave a page describing a moment that never existed.
    /// </remarks>
    public async Task<ContractReloadResult> DiscardStoredBytesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _store.ClearContractsAsync().ConfigureAwait(false);
            return await SealAsync(_contracts.Report, []).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Numbers one transaction and closes it over the store it is leaving behind.</summary>
    /// <remarks>
    /// Inside the lease, so the keys are this transaction's own. Listing degrades to nothing held rather
    /// than throwing, which is why this step needs no containment of its own.
    /// </remarks>
    private async Task<ContractReloadResult> SealAsync(
        ContractLoadReport? report,
        IReadOnlyList<ContractFailure> failures)
    {
        var keys = await _store.ListContractHashesAsync().ConfigureAwait(false);

        return new ContractReloadResult(Interlocked.Increment(ref _sequence), report, keys, failures);
    }
}
