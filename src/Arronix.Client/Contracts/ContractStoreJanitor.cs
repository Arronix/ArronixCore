using System.Linq;

namespace Arronix.Client.Contracts;

/// <summary>What one sweep of the contract store did.</summary>
/// <param name="Ran">Whether the report described an installation to sweep against.</param>
/// <param name="Evicted">The content hashes discarded.</param>
internal sealed record ContractStoreSweep(bool Ran, IReadOnlyList<string> Evicted);

/// <summary>Discards stored contract bytes the verified installation does not name.</summary>
/// <remarks>
/// A content-hash key names its own bytes, so an evicted address is refetched and reverified rather than
/// lost. Store only: a browser cannot unload an assembly and nothing here attempts it.
/// </remarks>
internal sealed class ContractStoreJanitor
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

    /// <summary>Removes every stored content hash the report's installation does not publish.</summary>
    /// <param name="report">The result of a load, this client's only description of an installation.</param>
    /// <returns>What was discarded, and whether anything was attempted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A null <see cref="ContractLoadReport.InstallationHash"/> means the manifest was never proved whole,
    /// so its empty package list is ignorance rather than an empty installation and the sweep is refused. A
    /// host that genuinely offers nothing still states a hash, and sweeping to empty is then correct.
    /// </remarks>
    public async Task<ContractStoreSweep> SweepStoreAsync(ContractLoadReport report)
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

        foreach (var key in await _store.ListContractHashesAsync().ConfigureAwait(false))
        {
            if (live.Contains(key))
            {
                continue;
            }

            if (await _store.RemoveContractAsync(key).ConfigureAwait(false))
            {
                evicted.Add(key);
            }
        }

        return new ContractStoreSweep(true, evicted.AsReadOnly());
    }
}
