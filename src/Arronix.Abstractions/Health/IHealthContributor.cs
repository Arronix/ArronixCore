using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Health;

/// <summary>
/// Contributes health checks for one subsystem. The host aggregates every registered contributor into
/// the health report it exposes; contributors never publish a report of their own.
/// </summary>
/// <remarks>
/// Implementations report the <see cref="HealthCheck"/> results they own and must not throw: a
/// contributor that fails should return an unhealthy result describing the failure, because an
/// exception escaping here degrades the whole report rather than one entry in it.
/// </remarks>
[Experimental(ExperimentalContracts.Health, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IHealthContributor
{
    /// <summary>
    /// Gets the stable identifier of this contributor, used to attribute results and to suppress a
    /// contributor by configuration.
    /// </summary>
    string ContributorId { get; }

    /// <summary>
    /// Runs this contributor's checks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The checks produced by this contributor, possibly empty.</returns>
    Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default);
}
