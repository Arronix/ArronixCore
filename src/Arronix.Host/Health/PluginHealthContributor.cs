using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
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
/// <param name="publication">The shared extension-publication boundary.</param>
public sealed class PluginHealthContributor(
    IPluginRuntimeRegistry plugins,
    PluginPublicationGate publication) : IHealthContributor
{
    private readonly IPluginRuntimeRegistry _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
    private readonly ConcurrentDictionary<PluginId, List<IHealthContributor>> _contributed = new();
    private readonly PluginPublicationGate _publication = publication ?? throw new ArgumentNullException(nameof(publication));

    /// <summary>Creates a standalone contributor with its own publication boundary.</summary>
    public PluginHealthContributor(IPluginRuntimeRegistry plugins)
        : this(plugins, new PluginPublicationGate())
    {
    }

    /// <summary>Gets the publication boundary this contributor participates in.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

    /// <summary>Determines whether this contributor observes the exact runtime authority supplied.</summary>
    internal bool UsesRuntime(PluginRuntimeRegistry runtime) => ReferenceEquals(_plugins, runtime);

    /// <inheritdoc />
    public string ContributorId => "extensions";

    /// <summary>
    /// Takes on an extension's own contributors.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    /// <param name="contributors">What it registered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contributors"/> is <see langword="null"/>.</exception>
    internal void Add(PluginId plugin, IReadOnlyList<IHealthContributor> contributors)
    {
        var candidate = Prepare(plugin, contributors);

        if (!TryPublish(candidate))
        {
            throw new InvalidOperationException(
                $"Extension '{plugin}' already has published health contributors.");
        }
    }

    /// <summary>Snapshots an extension's contributors without publishing them.</summary>
    internal RegisteredHealthContribution Prepare(
        PluginId plugin,
        IReadOnlyList<IHealthContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        if (contributors.Any(static contributor => contributor is null))
        {
            throw new ArgumentException("A health-contributor collection cannot contain null.", nameof(contributors));
        }

        return new RegisteredHealthContribution(plugin, [.. contributors]);
    }

    /// <summary>Publishes one already-snapshotted health contribution.</summary>
    internal bool TryPublish(RegisteredHealthContribution candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Contributors.Count == 0)
        {
            return true;
        }

        using var publication = _publication.EnterWrite();
        return _contributed.TryAdd(candidate.Plugin, candidate.Contributors);
    }

    /// <summary>Removes exactly one health contribution and never a later replacement.</summary>
    internal bool Remove(RegisteredHealthContribution candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Contributors.Count == 0)
        {
            return true;
        }

        using var publication = _publication.EnterWrite();
        return ((ICollection<KeyValuePair<PluginId, List<IHealthContributor>>>)_contributed)
            .Remove(new KeyValuePair<PluginId, List<IHealthContributor>>(
                candidate.Plugin,
                candidate.Contributors));
    }

    /// <summary>
    /// Discards an extension's contributors.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    internal void RemoveByPlugin(PluginId plugin)
    {
        using var publication = _publication.EnterWrite();
        _contributed.TryRemove(plugin, out _);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<PluginStatusView> results;
        IReadOnlyList<KeyValuePair<PluginId, List<IHealthContributor>>> contributed;
        using (_publication.EnterRead())
        {
            results = _plugins.Snapshot();
            contributed = [.. _contributed];
        }

        var checks = new List<HealthCheck>();

        foreach (var result in results.Where(candidate => string.Equals(
                     candidate.State,
                     nameof(PluginState.Quarantined),
                     StringComparison.Ordinal)))
        {
            var name = result.Id;
            var detail = result.Defects.Count == 0
                ? result.Message ?? "No reason was recorded."
                : $"{result.Message} {string.Join(" ", result.Defects)}";

            checks.Add(HealthCheck.Unhealthy(
                $"extensions/{name}",
                $"Extension '{name}'",
                result.ErrorCode == (int)CoreErrorCode.PluginDisabled ? HealthSeverity.Info : HealthSeverity.Error,
                detail,
                "The extension is quarantined and its configuration is kept. It is admitted again once the fault is corrected and the host restarts."));
        }

        var active = results.Count(result => string.Equals(
            result.State,
            nameof(PluginState.Active),
            StringComparison.Ordinal));

        checks.Add(HealthCheck.Healthy(
            "extensions",
            "Extensions",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{active} of {results.Count} installed extensions are running.")));

        foreach (var (plugin, contributors) in contributed)
        {
            foreach (var contributor in contributors)
            {
                checks.AddRange(await RunAsync(plugin, contributor, cancellationToken).ConfigureAwait(false));
            }
        }

        return checks;
    }

    internal sealed record RegisteredHealthContribution(
        PluginId Plugin,
        List<IHealthContributor> Contributors);

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
