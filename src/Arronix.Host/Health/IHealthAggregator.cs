namespace Arronix.Host.Health;

/// <summary>
/// Collects every contributor's answer into one report.
/// </summary>
/// <remarks>
/// Contributors never publish a report of their own — that is the whole reason this exists. A subsystem that
/// published its own health would have to decide what "overall" means, and four subsystems deciding that
/// independently is four different answers to one question.
/// </remarks>
public interface IHealthAggregator
{
    /// <summary>
    /// Collects the current report.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<HealthSnapshot> CollectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards any cached report, so the next collection runs the contributors again.
    /// </summary>
    /// <remarks>
    /// Called when something the report depends on changes — an extension is quarantined, a provider's status
    /// moves. Without it, a caching health endpoint reports the state of the world as it was up to a cache
    /// lifetime ago, which is exactly wrong at the moment something breaks.
    /// </remarks>
    void Invalidate();
}
