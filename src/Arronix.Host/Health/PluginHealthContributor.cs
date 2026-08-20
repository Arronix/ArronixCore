using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;


namespace Arronix.Host.Health;

/// <summary>
/// Reports which extensions are running, why any of them is not, and whatever the running ones say about
/// themselves.
/// </summary>
/// <remarks>
/// <para>
/// A quarantined extension produces a permanently unhealthy check rather than a transient one. It will not
/// recover on its own — its state changes when an operator changes something — so a check that cleared
/// itself would be reporting a recovery that had not happened. The check carries the machine-readable code
/// and every individual defect, because "the extension failed to load" without the list is a message nobody
/// can act on.
/// </para>
/// <para>
/// The extensions' own contributors are held here rather than registered into the container, for two
/// reasons. Extensions are admitted after the container is built and never mutate it, which is what removes
/// the entire two-phase-container class of bug; and holding them here is where their check identifiers get
/// qualified by the extension that produced them, so a collision between two extensions choosing the same
/// obvious identifier is structurally impossible rather than merely unlikely.
/// </para>
/// </remarks>
/// <param name="plugins">The extension runtime registry.</param>
public sealed class PluginHealthContributor(IPluginRuntimeRegistry plugins) : IHealthContributor
{
    private readonly IPluginRuntimeRegistry _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
    private readonly ConcurrentDictionary<PluginId, List<IHealthContributor>> _contributed = new();

    /// <inheritdoc />
    public string ContributorId => "extensions";

    /// <summary>
    /// Takes on an extension's own contributors.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    /// <param name="contributors">What it registered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contributors"/> is <see langword="null"/>.</exception>
    public void Add(PluginId plugin, IReadOnlyList<IHealthContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);

        if (contributors.Count > 0)
        {
            _contributed[plugin] = [.. contributors];
        }
    }

    /// <summary>
    /// Discards an extension's contributors.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    public void RemoveByPlugin(PluginId plugin) => _contributed.TryRemove(plugin, out _);

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = _plugins.All;
        var checks = new List<HealthCheck>();

        foreach (var result in results.Where(candidate => candidate.State == PluginState.Quarantined))
        {
            var name = result.Id?.ToString() ?? result.Source;
            var detail = result.Defects.Count == 0
                ? result.Message ?? "No reason was recorded."
                : $"{result.Message} {string.Join(" ", result.Defects)}";

            checks.Add(HealthCheck.Unhealthy(
                $"extensions/{name}",
                $"Extension '{name}'",
                result.ErrorCode == CoreErrorCode.PluginDisabled ? HealthSeverity.Info : HealthSeverity.Error,
                detail,
                "The extension is quarantined and its configuration is kept. It is admitted again once the fault is corrected and the host restarts."));
        }

        var active = results.Count(result => result.IsActive);

        checks.Add(HealthCheck.Healthy(
            "extensions",
            "Extensions",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{active} of {results.Count} installed extensions are running.")));

        foreach (var (plugin, contributors) in _contributed)
        {
            foreach (var contributor in contributors)
            {
                checks.AddRange(await RunAsync(plugin, contributor, cancellationToken).ConfigureAwait(false));
            }
        }

        return checks;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An extension's contributor is third-party code; anything it throws becomes an unhealthy check attributed to that extension rather than a failure of the report.")]
    private static async Task<IReadOnlyList<HealthCheck>> RunAsync(
        PluginId plugin,
        IHealthContributor contributor,
        CancellationToken cancellationToken)
    {
        try
        {
            var produced = await contributor.CheckAsync(cancellationToken).ConfigureAwait(false);

            return [.. produced.Select(check => check with { CheckId = $"{plugin}/{check.CheckId}" })];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            return
            [
                HealthCheck.Unhealthy(
                    $"{plugin}/{contributor.ContributorId}",
                    $"Extension '{plugin}' health check",
                    HealthSeverity.Error,
                    $"This check failed: {failure.Message}",
                    "A check that throws is a defect in the check as well as a signal about what it checks."),
            ];
        }
    }
}
