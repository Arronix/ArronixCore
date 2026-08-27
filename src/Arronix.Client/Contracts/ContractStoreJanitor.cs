using System.Linq;

namespace Arronix.Client.Contracts;

/// <summary>What one sweep of the contract store did.</summary>
/// <param name="Ran">Whether the report described an installation worth sweeping against.</param>
/// <param name="Evicted">The content hashes discarded.</param>
public sealed record ContractStoreSweep(bool Ran, IReadOnlyList<string> Evicted);

/// <summary>
/// Discards stored contract bytes the installation a client just verified does not name.
/// </summary>
/// <remarks>
/// <para>
/// A content-hash key names its own bytes, so eviction cannot make a load wrong: an address that is gone is
/// refetched over the network and verified exactly as it would have been from the store. This exists so a
/// browser stops accumulating every build a host has ever published, not to keep the client correct.
/// </para>
/// <para>
/// It touches the store and nothing else. A browser cannot unload an assembly, and nothing here attempts it
/// or reports one as removed.
/// </para>
/// </remarks>
public sealed class ContractStoreJanitor
{
    private readonly ContractStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractStoreJanitor"/> class.
    /// </summary>
    /// <param name="store">This browser's contract store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ContractStoreJanitor(ContractStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Removes every stored content hash the report's installation does not publish.
    /// </summary>
    /// <param name="report">The result of a load, which is this client's only description of an installation.</param>
    /// <returns>What was discarded, and whether anything was attempted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A null <see cref="ContractLoadReport.InstallationHash"/> is a manifest that was never proved whole —
    /// unreachable, or not a description of an installation — and its empty package list is an absence of
    /// knowledge rather than a host that publishes nothing. Sweeping against one would empty the store on
    /// every failed fetch, so it is refused. A host that genuinely offers nothing still states a hash, and
    /// sweeping to empty is then the correct answer.
    /// </remarks>
    public async Task<ContractStoreSweep> SweepAsync(ContractLoadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.InstallationHash is null)
        {
            return new ContractStoreSweep(false, []);
        }

        var live = report.Packages
            .SelectMany(package => package.Assemblies)
            .Select(assembly => assembly.Published.ContentHash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var evicted = new List<string>();

        foreach (var key in await _store.KeysAsync().ConfigureAwait(false))
        {
            if (live.Contains(key))
            {
                continue;
            }

            if (await _store.RemoveAsync(key).ConfigureAwait(false))
            {
                evicted.Add(key);
            }
        }

        return new ContractStoreSweep(true, evicted.AsReadOnly());
    }
}
