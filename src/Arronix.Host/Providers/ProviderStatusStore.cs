using System.Collections.Concurrent;
using System.Linq;
using Arronix.Host.Scheduling;

namespace Arronix.Host.Providers;

/// <summary>
/// How one configured provider has been behaving.
/// </summary>
/// <param name="DefinitionId">The definition.</param>
/// <param name="InitialFailure">When the current run of failures began.</param>
/// <param name="MostRecentFailure">When it last failed.</param>
/// <param name="EscalationLevel">How far up the back-off ladder it has climbed.</param>
/// <param name="DisabledTill">When it may be used again.</param>
public readonly record struct ProviderStatus(
    int DefinitionId,
    DateTimeOffset? InitialFailure,
    DateTimeOffset? MostRecentFailure,
    int EscalationLevel,
    DateTimeOffset? DisabledTill);

/// <summary>
/// Keeps a failing provider from being asked again until it has had time to recover.
/// </summary>
/// <remarks>
/// <para>
/// This reproduces a subsystem that is byte-identical in all four surveyed applications, including the two
/// details that look like accidents and are not. The first is the grace window after an initial failure: a
/// provider is not backed off at all for the first few minutes, because the overwhelming majority of single
/// failures are a blip and backing off immediately turns a five-second outage into a fifteen-minute one. The
/// second is that recording a connection failure escalates while recording other failures does not
/// necessarily, because a refused connection is evidence about the provider while a malformed response is
/// evidence about one request.
/// </para>
/// <para>
/// This is a separate back-off domain from the scheduler's, exactly as it is in every surveyed application.
/// The scheduler asks "should I try this work again?"; this asks "should I use this provider at all?". A
/// single ladder would mean one provider's outage delayed unrelated work that happened to be queued behind
/// it.
/// </para>
/// </remarks>
/// <param name="clock">The clock the windows are measured against.</param>
public sealed class ProviderStatusStore(TimeProvider clock)
{
    /// <summary>
    /// How long after the first failure a provider is still used normally.
    /// </summary>
    public static readonly TimeSpan InitialFailureGrace = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long after the host starts a provider is still used normally, whatever its stored status says.
    /// </summary>
    /// <remarks>
    /// A deployment that was offline overnight starts with every provider deep in escalation and would sit
    /// idle for hours. The window lets one attempt through so the ladder can be reset by success.
    /// </remarks>
    public static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<int, ProviderStatus> _statuses = new();
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly DateTimeOffset _startedAt = (clock ?? throw new ArgumentNullException(nameof(clock))).GetUtcNow();

    /// <summary>
    /// Gets every recorded status, ordered by definition.
    /// </summary>
    public IReadOnlyList<ProviderStatus> All
        => [.. _statuses.Values.OrderBy(status => status.DefinitionId)];

    /// <summary>
    /// Gets one provider's status.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <returns>The status, or <see langword="null"/> when it has never failed.</returns>
    public ProviderStatus? Find(int definitionId)
        => _statuses.TryGetValue(definitionId, out var status) ? status : null;

    /// <summary>
    /// Determines whether a provider may be used right now.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <returns><see langword="true"/> when it may be used.</returns>
    public bool IsAvailable(int definitionId)
    {
        var now = _clock.GetUtcNow();

        if (now - _startedAt < StartupGrace)
        {
            return true;
        }

        return Find(definitionId) is not { DisabledTill: { } till } || till <= now;
    }

    /// <summary>
    /// Records a failure and escalates the back-off.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <param name="minimumBackoff">
    /// A wait the failure itself asked for, which is honored in preference to the ladder when it is longer.
    /// </param>
    /// <returns>The resulting status.</returns>
    public ProviderStatus RecordFailure(int definitionId, TimeSpan? minimumBackoff = null)
    {
        var now = _clock.GetUtcNow();

        return _statuses.AddOrUpdate(
            definitionId,
            _ => Escalate(new ProviderStatus(definitionId, now, now, 0, null), now, minimumBackoff),
            (_, existing) => Escalate(existing, now, minimumBackoff));
    }

    /// <summary>
    /// Records a failure without escalating.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    /// <returns>The resulting status.</returns>
    /// <remarks>
    /// Used for failures that say nothing about the provider's availability — a malformed response, a
    /// rejected query. The failure is visible to an operator without a single bad request costing an hour of
    /// the provider's usefulness.
    /// </remarks>
    public ProviderStatus RecordConnectionFailure(int definitionId)
    {
        var now = _clock.GetUtcNow();

        return _statuses.AddOrUpdate(
            definitionId,
            _ => new ProviderStatus(definitionId, now, now, 0, null),
            (_, existing) => existing with { MostRecentFailure = now, InitialFailure = existing.InitialFailure ?? now });
    }

    /// <summary>
    /// Records a success, clearing any back-off.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    public void RecordSuccess(int definitionId) => _statuses.TryRemove(definitionId, out _);

    /// <summary>
    /// Forgets everything about a definition.
    /// </summary>
    /// <param name="definitionId">The definition.</param>
    public void Forget(int definitionId) => _statuses.TryRemove(definitionId, out _);

    private static ProviderStatus Escalate(ProviderStatus status, DateTimeOffset now, TimeSpan? minimumBackoff)
    {
        var initial = status.InitialFailure ?? now;
        var level = status.EscalationLevel + 1;

        // Inside the grace window the failure is recorded but the provider stays in service.
        var ladder = now - initial < InitialFailureGrace
            ? TimeSpan.Zero
            : BackoffLadder.PeriodFor(level);

        var wait = minimumBackoff is { } asked && asked > ladder ? asked : ladder;

        return new ProviderStatus(
            status.DefinitionId,
            initial,
            now,
            level,
            wait <= TimeSpan.Zero ? null : now + wait);
    }
}
