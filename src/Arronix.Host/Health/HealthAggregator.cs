using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Host.Configuration;
using Microsoft.Extensions.Options;

// The health contribution contract is experimental; this file aggregates over it.
#pragma warning disable ARX0006

namespace Arronix.Host.Health;

/// <summary>
/// Runs every contributor and turns their answers into one report.
/// </summary>
/// <remarks>
/// <para>
/// The three rules that matter are all about a contributor failing. A contributor that throws becomes an
/// unhealthy check attributed to itself; a contributor that hangs is abandoned after its timeout and becomes
/// the same; and neither of those degrades the report as a whole beyond the one entry. The contract already
/// says a contributor must not throw, but "must not" is not an enforcement, and a health endpoint that fails
/// because one subsystem is unwell is a health endpoint that stops working exactly when it is needed.
/// </para>
/// <para>
/// Extension contributors are wrapped so their check identifiers become qualified by the extension. Two
/// extensions choosing the same obvious identifier is not a hypothetical, and under a unified host their
/// results would silently overwrite each other in any keyed view.
/// </para>
/// <para>
/// Results are cached for a short lifetime and invalidated eagerly. Monitoring systems poll, and a poll that
/// re-runs every contributor turns a health endpoint into a load source; but a cache that is not invalidated
/// when something breaks reports health for as long as it lives.
/// </para>
/// </remarks>
public sealed class HealthAggregator : IHealthAggregator
{
    private readonly IReadOnlyList<IHealthContributor> _contributors;
    private readonly TimeProvider _clock;
    private readonly HealthOptions _options;
    private readonly Lock _gate = new();
    private HealthSnapshot? _cached;

    /// <summary>
    /// Creates an aggregator.
    /// </summary>
    /// <param name="contributors">Every registered contributor, host-supplied and extension-supplied alike.</param>
    /// <param name="clock">The clock the report is stamped with and the cache measured against.</param>
    /// <param name="options">The deployment's health settings.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public HealthAggregator(
        IEnumerable<IHealthContributor> contributors,
        TimeProvider clock,
        IOptions<HealthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _options = options.Value;

        var suppressed = _options.SuppressedContributors.ToHashSet(StringComparer.OrdinalIgnoreCase);

        _contributors =
        [
            .. contributors
                .Where(contributor => !suppressed.Contains(contributor.ContributorId))
                .OrderBy(contributor => contributor.ContributorId, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public async Task<HealthSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            if (_cached is { } cached && now - cached.CheckedAt < _options.CacheLifetime)
            {
                return cached;
            }
        }

        var checks = new List<HealthCheck>();

        foreach (var contributor in _contributors)
        {
            checks.AddRange(await RunAsync(contributor, cancellationToken).ConfigureAwait(false));
        }

        var snapshot = HealthSnapshot.From(now, checks);

        lock (_gate)
        {
            _cached = snapshot;
        }

        return snapshot;
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A contributor is arbitrary code, including extension code; anything it throws becomes an unhealthy check attributed to it rather than a failure of the whole report.")]
    private async Task<IReadOnlyList<HealthCheck>> RunAsync(
        IHealthContributor contributor,
        CancellationToken cancellationToken)
    {
        // The deadline is driven by the injected clock rather than by a real timer, so a test can prove the
        // timeout path without waiting for it.
        using var deadline = new CancellationTokenSource(_options.ContributorTimeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            return await contributor.CheckAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return
            [
                HealthCheck.Unhealthy(
                    contributor.ContributorId,
                    contributor.ContributorId,
                    HealthSeverity.Error,
                    $"This check did not answer within {_options.ContributorTimeout}.",
                    "Look at what the subsystem behind this check is waiting on; a health check that hangs usually means the thing it checks is hanging too."),
            ];
        }
        catch (Exception failure)
        {
            return
            [
                HealthCheck.Unhealthy(
                    contributor.ContributorId,
                    contributor.ContributorId,
                    HealthSeverity.Error,
                    $"This check failed: {failure.Message}",
                    "A check that throws is a defect in the check as well as a signal about what it checks."),
            ];
        }
    }
}
