using Arronix.Abstractions.Health;
using Arronix.Abstractions.Wire;
using Arronix.Api.Serialization;
using Arronix.Host.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;


namespace Arronix.Api.Endpoints;

/// <summary>
/// Whether the platform is working, said twice for two different audiences.
/// </summary>
/// <remarks>
/// <para>
/// The versioned route is for a person: the whole aggregate, every check, with the message and the
/// remediation hint each contributor supplied, always answered with 200 because the question "how are you"
/// was answered successfully even when the answer is "badly".
/// </para>
/// <para>
/// The unversioned route is for a supervisor — a container orchestrator, a load balancer, an uptime probe —
/// which does not read JSON and decides on the status code alone. It maps healthy and degraded to 200 and
/// only unhealthy to 503, because degraded means "working, with something worth looking at" and restarting
/// a process for that turns a warning into an outage. It is unversioned deliberately: the thing that
/// contracts it is infrastructure configuration, which should not have to be edited when this API's
/// version moves.
/// </para>
/// </remarks>
internal static class HealthEndpoints
{
    /// <summary>
    /// Maps the versioned health route.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/health", GetHealth)
            .WithTags("Platform")
            .WithName("GetHealth")
            .WithSummary("Returns every health check the platform ran, and the overall status.");

        return group;
    }

    /// <summary>
    /// Maps the unversioned liveness route.
    /// </summary>
    /// <param name="app">The application being configured.</param>
    /// <returns>The same application, for chaining.</returns>
    internal static WebApplication MapPlatformHealthEndpoint(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health", GetLiveness)
            .WithTags("Platform")
            .WithName("GetLiveness")
            .WithSummary("Machine-readable health, with 503 reserved for a platform that is not working.");

        return app;
    }

    private static async Task<Ok<HealthSnapshotView>> GetHealth(
        IHealthAggregator health,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(health);

        var snapshot = await health.CollectAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(HealthProjection.ToView(snapshot));
    }

    private static async Task<IResult> GetLiveness(
        IHealthAggregator health,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(health);

        var snapshot = await health.CollectAsync(cancellationToken).ConfigureAwait(false);

        var status = snapshot.Status switch
        {
            HealthStatus.Healthy => StatusCodes.Status200OK,
            HealthStatus.Degraded => StatusCodes.Status200OK,
            HealthStatus.Unhealthy => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        return Results.Json(HealthProjection.ToView(snapshot), statusCode: status);
    }
}

/// <summary>
/// Turns the host's health aggregate into the shape that crosses the wire.
/// </summary>
/// <remarks>
/// The aggregate is a host type and stays one; the view is a contract type the client compiles against.
/// The overall status crosses as its name rather than its ordinal for the same reason every enumeration
/// does here — an ordinal is a number whose meaning changes the day somebody inserts a member.
/// </remarks>
internal static class HealthProjection
{
    /// <summary>
    /// Projects a snapshot onto the wire.
    /// </summary>
    /// <param name="snapshot">The host's aggregate.</param>
    /// <returns>The view.</returns>
    internal static HealthSnapshotView ToView(HealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new HealthSnapshotView(WireText.Name(snapshot.Status), snapshot.CheckedAt, snapshot.Checks);
    }
}
