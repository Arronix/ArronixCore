using Arronix.Abstractions.Scheduling;
using Arronix.Host.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Host.Composition;

/// <summary>
/// Registers the one scheduler and the one queue.
/// </summary>
/// <remarks>
/// The scheduler is registered twice on purpose: once as itself, because the extension bootstrapper has to
/// tell it when startup-scheduled work may begin, and once as a hosted service so the framework runs its
/// loop. Registering it only as a hosted service would give the bootstrapper a different instance from the
/// one that is running.
/// </remarks>
internal static class SchedulingRegistration
{
    /// <summary>
    /// Registers the scheduling subsystem.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddScheduling(this IServiceCollection services)
    {
        services.TryAddSingleton<JobQueue>();
        services.TryAddSingleton<ConcurrencyGovernor>();
        services.TryAddSingleton<FailureClassifier>();
        services.TryAddSingleton<BackgroundTaskRegistry>();
        services.TryAddSingleton<IBackgroundTaskRegistry>(
            provider => provider.GetRequiredService<BackgroundTaskRegistry>());

        services.TryAddSingleton<JobScheduler>();
        services.AddHostedService(provider => provider.GetRequiredService<JobScheduler>());

        return services;
    }
}
