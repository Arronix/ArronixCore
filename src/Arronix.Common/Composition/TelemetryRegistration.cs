using System.Linq;
using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Telemetry;
using Arronix.Common.Configuration;
using Arronix.Common.Contributions;
using Arronix.Common.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arronix.Common.Composition;

/// <summary>
/// Registers <c>Telemetry/</c>.
/// </summary>
internal static class TelemetryRegistration
{
    /// <summary>
    /// Registers the telemetry pipeline and the redaction engine it masks with.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">Where the pipeline's bounds are read from.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The engine is compiled once, from every registered <see cref="IRedactionRuleProvider"/>, and a rule
    /// that will not compile fails composition rather than silently stopping. An extension's rules are
    /// added when its attempt publishes; they are reversible until that attempt is confirmed, and
    /// permanent afterwards.
    /// </remarks>
    internal static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The platform's own rules go in first, so they run first and nothing a contributor adds can take
        // their identifiers or come before them.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRedactionRuleProvider, CoreRedactionRules>(
            static provider => new CoreRedactionRules(provider.GetService<IOptions<RedactionOptions>>()?.Value)));

        services.TryAddSingleton(provider => RedactionEngine.Compile(
            provider.GetServices<IRedactionRuleProvider>()
                .Select(rules => new OwnedRedactionRules(
                    rules is CoreRedactionRules ? CoreRedactionRules.Owner : rules.GetType().FullName ?? "host",
                    rules.Rules)),
            provider.GetRequiredService<IOptions<TelemetryOptions>>().Value,
            provider.GetService<IOptions<RedactionOptions>>()?.Value));

        services.TryAddSingleton(static provider => new HostTelemetryEmitter(
            provider.GetServices<ITelemetryEnricher>(),
            provider.GetServices<ITelemetryEventFilter>(),
            provider.GetServices<ITelemetrySink>(),
            provider.GetRequiredService<RedactionEngine>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ILogger<HostTelemetryEmitter>>() ?? NullLogger<HostTelemetryEmitter>.Instance,
            provider.GetService<IOptions<TelemetryOptions>>(),
            provider.GetService<IPluginContributionSource>()));

        services.TryAddSingleton<ITelemetryEmitter>(static provider => provider.GetRequiredService<HostTelemetryEmitter>());
        services.TryAddSingleton<ITelemetryShutdown>(static provider => provider.GetRequiredService<HostTelemetryEmitter>());
        services.TryAddSingleton<IRedactionAdmission>(static provider => provider.GetRequiredService<RedactionEngine>());

        return services;
    }
}
