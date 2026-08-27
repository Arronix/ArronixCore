using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Providers;
using Arronix.Host.Configuration;
using Arronix.Host.Providers;
using Microsoft.Extensions.Options;


namespace Arronix.Host.Health;

/// <summary>
/// Reports on configured providers, storage roots and anything else the deployment needs and might not have.
/// </summary>
/// <remarks>
/// The orphan check is the one that earns its place. A definition whose implementation is gone keeps working
/// the moment the extension comes back, which is exactly why it must be visible: an operator who cannot see
/// that four of their release sources are inert will conclude the search is broken rather than that an
/// extension failed to load.
/// </remarks>
/// <param name="definitions">The configured definitions.</param>
/// <param name="status">How each of them has been behaving.</param>
/// <param name="library">The deployment's library settings.</param>
public sealed class ProviderHealthContributor(
    ProviderDefinitionStore definitions,
    ProviderStatusStore status,
    IOptions<LibraryOptions> library) : IHealthContributor
{
    private readonly ProviderDefinitionStore _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ProviderStatusStore _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly LibraryOptions _library = library?.Value ?? throw new ArgumentNullException(nameof(library));

    /// <inheritdoc />
    public string ContributorId => "providers";

    /// <inheritdoc />
    public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var checks = new List<HealthCheck>();
        var all = _definitions.All;

        var orphaned = all.Where(definition => definition.State == DefinitionState.Orphaned).ToList();

        if (orphaned.Count > 0)
        {
            checks.Add(HealthCheck.Degraded(
                "providers/orphaned",
                "Providers without an implementation",
                HealthSeverity.Warning,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{orphaned.Count} configured providers have no loaded implementation: {string.Join(", ", orphaned.Select(definition => definition.Name))}."),
                "Their settings are kept. They start working again as soon as the extension providing them loads."));
        }

        var incomplete = all.Where(definition => definition.State == DefinitionState.Incomplete).ToList();

        if (incomplete.Count > 0)
        {
            checks.Add(HealthCheck.Degraded(
                "providers/incomplete",
                "Providers missing a credential",
                HealthSeverity.Warning,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{incomplete.Count} configured providers are missing a value they require: {string.Join(", ", incomplete.Select(definition => definition.Name))}."),
                "A credential or secret is never read back, so it is not stored and does not survive a "
                + "restart. Enter it again to make the provider usable."));
        }

        var backedOff = all
            .Where(definition => definition.Enabled
                && definition.State == DefinitionState.Active
                && !_status.IsAvailable(definition.Id))
            .ToList();

        if (backedOff.Count > 0)
        {
            checks.Add(HealthCheck.Degraded(
                "providers/unavailable",
                "Providers currently backed off",
                HealthSeverity.Warning,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{backedOff.Count} providers are being rested after repeated failures: {string.Join(", ", backedOff.Select(definition => definition.Name))}."),
                "Test one of them to see the error it is failing with."));
        }

        if (checks.Count == 0)
        {
            checks.Add(HealthCheck.Healthy(
                "providers",
                "Providers",
                string.Create(CultureInfo.InvariantCulture, $"{all.Count} configured providers, all in service.")));
        }

        checks.Add(RootFolderCheck());

        return Task.FromResult<IReadOnlyList<HealthCheck>>(checks);
    }

    private HealthCheck RootFolderCheck()
    {
        if (_library.RootFolders.Count == 0)
        {
            return HealthCheck.Degraded(
                "storage/roots",
                "Root folders",
                HealthSeverity.Warning,
                "No root folder is configured, so nothing can be imported.",
                "Add at least one root folder under the library settings.");
        }

        var missing = _library.RootFolders
            .Where(path => !System.IO.Directory.Exists(path))
            .ToList();

        return missing.Count == 0
            ? HealthCheck.Healthy(
                "storage/roots",
                "Root folders",
                string.Create(CultureInfo.InvariantCulture, $"{_library.RootFolders.Count} root folders, all present."))
            : HealthCheck.Unhealthy(
                "storage/roots",
                "Root folders",
                HealthSeverity.Error,
                $"These configured root folders are not there: {string.Join(", ", missing)}.",
                "A root folder that disappears is usually an unmounted volume. Nothing is imported into a missing root, and nothing is deleted from one either.");
    }
}
