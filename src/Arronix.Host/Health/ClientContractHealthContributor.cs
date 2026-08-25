using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Plugins.Registry;


namespace Arronix.Host.Health;

/// <summary>
/// Reports the client facets this host is withholding from a browser, and why.
/// </summary>
/// <remarks>
/// A withheld facet is invisible from a browser by construction: the extension is Active and healthy, and
/// the only symptom is a media kind that never appears. Reported here so an operator finds it beside every
/// other reason a running installation might not be doing what they expect. Degraded rather than unhealthy:
/// the installation works, and the cause is a packaging mistake in an extension.
/// </remarks>
/// <param name="contracts">The catalog that decides what a browser may load.</param>
public sealed class ClientContractHealthContributor(IClientContractCatalog contracts) : IHealthContributor
{
    private readonly IClientContractCatalog _contracts =
        contracts ?? throw new ArgumentNullException(nameof(contracts));

    /// <inheritdoc />
    public string ContributorId => "client-contracts";

    /// <inheritdoc />
    public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manifest = _contracts.Manifest();

        if (manifest.Refused.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<HealthCheck>>(
            [
                HealthCheck.Healthy(
                    "client-contracts",
                    "Client contracts",
                    manifest.Packages.Count == 0
                        ? "No installed package offers a contract assembly to a browser client."
                        : $"{manifest.Packages.Count} package(s) offer a contract assembly to a browser client."),
            ]);
        }

        return Task.FromResult<IReadOnlyList<HealthCheck>>(
        [
            .. manifest.Refused.Select(refusal => HealthCheck.Degraded(
                $"client-contracts.{refusal.Package.Value}",
                $"Client contract withheld: {refusal.Package}",
                HealthSeverity.Warning,
                refusal.Reason,
                refusal.CausedBy is { } cause
                    ? $"Fix package '{cause}' first; this package's facet was withheld because of it."
                    : "Correct the package's clientContracts declaration, or ship the contracts it references.")),
        ]);
    }
}
