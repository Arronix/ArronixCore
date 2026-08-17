using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Wire;

// The health contribution contract and the wire contracts are both experimental.
#pragma warning disable ARX0006
#pragma warning disable ARX0017

namespace Arronix.Host.Health;

/// <summary>
/// The platform's health at one moment.
/// </summary>
/// <param name="Status">The worst status among the checks.</param>
/// <param name="CheckedAt">When the checks were collected.</param>
/// <param name="Checks">The individual checks, worst first.</param>
/// <remarks>
/// Reuses the stable health check verbatim. Inventing a second health type so that the host could add a
/// field would mean every contributor's result had to be translated, and the translation would be the place
/// a field quietly stopped being carried.
/// </remarks>
public sealed record HealthSnapshot(
    HealthStatus Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<HealthCheck> Checks)
{
    /// <summary>
    /// Projects the snapshot onto the wire.
    /// </summary>
    /// <returns>The view a consumer receives.</returns>
    /// <remarks>
    /// The status crosses as text rather than as the enumeration so that a consumer built against an older
    /// contract can still present a status it has not heard of. This is the one place in the contract set
    /// where forward compatibility beats exhaustiveness: a health endpoint that fails to deserialize is the
    /// last thing that should break.
    /// </remarks>
    public HealthSnapshotView ToView() => new(Status.ToString(), CheckedAt, Checks);

    /// <summary>
    /// Builds a snapshot from a set of checks, deriving the overall status from the worst of them.
    /// </summary>
    /// <param name="checkedAt">When the checks were collected.</param>
    /// <param name="checks">The checks.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="checks"/> is <see langword="null"/>.</exception>
    public static HealthSnapshot From(DateTimeOffset checkedAt, IReadOnlyList<HealthCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var status = checks.Count == 0
            ? HealthStatus.Healthy
            : checks.Max(check => check.Status);

        return new HealthSnapshot(
            status,
            checkedAt,
            [.. checks.OrderByDescending(check => check.Status).ThenByDescending(check => check.Severity)]);
    }
}
