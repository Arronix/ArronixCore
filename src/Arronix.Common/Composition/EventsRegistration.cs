using Arronix.Abstractions.Events;
using Arronix.Common.Contributions;
using Arronix.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arronix.Common.Composition;

/// <summary>
/// Registers <c>Events/</c>.
/// </summary>
internal static class EventsRegistration
{
    /// <summary>
    /// Registers the platform's event bus.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Handlers are not registered here: a handler is contributed by whoever owns the subsystem that cares,
    /// under <c>IEventHandler&lt;TEvent&gt;</c>, and the publisher resolves them at publication. The log is
    /// resolved leniently so that a host which composed no logging still gets a working bus.
    /// </remarks>
    internal static IServiceCollection AddEvents(this IServiceCollection services)
    {
        services.TryAddSingleton<IEventPublisher>(static provider => new HostEventPublisher(
            provider,
            provider.GetService<ILogger<HostEventPublisher>>() ?? NullLogger<HostEventPublisher>.Instance,
            provider.GetService<IPluginContributionSource>()));

        return services;
    }
}
