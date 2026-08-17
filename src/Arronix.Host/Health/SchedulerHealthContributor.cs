using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Host.Configuration;
using Arronix.Host.Scheduling;
using Microsoft.Extensions.Options;

// The health contribution contract is experimental.
#pragma warning disable ARX0006

namespace Arronix.Host.Health;

/// <summary>
/// Reports whether work is getting done.
/// </summary>
/// <remarks>
/// Two signals, both of which an operator can act on. A queue deeper than the configured warning depth means
/// work is arriving faster than the ceilings let it leave, which is either a ceiling to raise or a symptom
/// of something failing repeatedly. An entry that has climbed several rungs of the back-off ladder means
/// something it depends on has been unavailable for a while, and naming the job is what turns "the queue is
/// long" into a thing to look at.
/// </remarks>
/// <param name="queue">The queue.</param>
/// <param name="registry">The registered jobs.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The deployment's scheduler settings.</param>
public sealed class SchedulerHealthContributor(
    JobQueue queue,
    BackgroundTaskRegistry registry,
    TimeProvider clock,
    IOptions<SchedulerOptions> options) : IHealthContributor
{
    private const int RungsBeforeConcern = 4;

    private readonly JobQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly BackgroundTaskRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly SchedulerOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public string ContributorId => "scheduler";

    /// <inheritdoc />
    public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = _queue.Snapshot();
        var checks = new List<HealthCheck>
        {
            entries.Count > _options.BacklogWarningDepth
                ? HealthCheck.Degraded(
                    "scheduler/backlog",
                    "Job queue depth",
                    HealthSeverity.Warning,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{entries.Count} entries are queued, past the configured warning depth of {_options.BacklogWarningDepth}."),
                    "Either the concurrency ceilings are too low for what this deployment is being asked to do, or something is failing and being retried.")
                : HealthCheck.Healthy(
                    "scheduler/backlog",
                    "Job queue depth",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{entries.Count} entries queued across {_registry.Registrations().Count} registered jobs.")),
        };

        var stuck = entries.Where(entry => entry.Attempt > RungsBeforeConcern).ToList();

        if (stuck.Count > 0)
        {
            var worst = stuck.OrderByDescending(entry => entry.Attempt).First();
            var wait = worst.NotBefore - _clock.GetUtcNow();

            checks.Add(HealthCheck.Degraded(
                "scheduler/retries",
                "Repeatedly failing work",
                HealthSeverity.Warning,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{stuck.Count} queued entries have failed more than {RungsBeforeConcern} times; the worst is '{worst.JobId}' on attempt {worst.Attempt}, next tried in {(wait < TimeSpan.Zero ? TimeSpan.Zero : wait)}."),
                "Look at what that job depends on. Work that has climbed the back-off ladder is waiting on something that has been unavailable for some time."));
        }

        return Task.FromResult<IReadOnlyList<HealthCheck>>(checks);
    }
}
