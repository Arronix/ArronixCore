using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Health;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// The platform's health at one moment.
/// </summary>
/// <param name="Status">The worst status among the checks.</param>
/// <param name="CheckedAt">When the checks were collected.</param>
/// <param name="Checks">The individual checks. Reuses the stable health check verbatim.</param>
/// <remarks>
/// The status is carried as text rather than as the stable status enumeration so that a consumer built
/// against an older contract can still present a status it has not heard of, which is the one place in
/// this contract area where forward compatibility matters more than exhaustiveness: a health endpoint
/// that fails to deserialize is the last thing that should break.
/// </remarks>
[Experimental(ExperimentalContracts.Wire, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record HealthSnapshotView(
    string Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<HealthCheck> Checks);
