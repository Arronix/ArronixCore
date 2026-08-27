using System.Globalization;
using System.Net.Http;
using Arronix.Client.Configuration;
using Arronix.Client.Contracts;
using Arronix.Client.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Client.Composition;

/// <summary>
/// Registers everything the client needs.
/// </summary>
/// <remarks>
/// One registration surface, so that the entry point says what the application is made of in one line and
/// nothing else in the project reaches for a container. Every service is a singleton: a browser runs one
/// instance of this application and there is no request to scope anything to.
/// </remarks>
public static class ArronixClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds the client's services.
    /// </summary>
    /// <param name="services">The collection to add to.</param>
    /// <param name="configuration">Where the deployment's settings are read from.</param>
    /// <param name="origin">The address the application was served from.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddArronixClient(
        this IServiceCollection services,
        IConfiguration configuration,
        Uri origin)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(origin);

        var options = ReadOptions(configuration, origin);

        services.TryAddSingleton(options);
        services.TryAddSingleton(_ => new HttpClient
        {
            BaseAddress = options.ServerAddress,
            Timeout = options.RequestTimeout,
        });

        services.TryAddSingleton<HostConnectivity>();
        services.TryAddSingleton<ArronixApiClient>();
        services.TryAddSingleton<DescriptorCache>();
        services.TryAddSingleton<EventStream>();
        services.TryAddSingleton<ActionDispatcher>();
        services.TryAddSingleton<ActivityLog>();

        // The media contracts this host admitted, acquired at run time rather than compiled in. Singleton
        // for the same reason the rest are, and for one more: a browser cannot unload an assembly, so the
        // record of what this page has already loaded has to be the one record.
        services.TryAddSingleton<ContractStore>();
        services.TryAddSingleton<MediaContractLoader>();

        // Reads one serialized entity through whichever contract this page admitted. Separate from the
        // loader because a payload that will not read says nothing about the installation that was.
        services.TryAddSingleton<ContractPayloadLoader>();

        return services;
    }

    private static ClientOptions ReadOptions(IConfiguration configuration, Uri origin)
    {
        var section = configuration.GetSection(ClientOptions.SectionName);

        return new ClientOptions
        {
            ServerAddress = ReadUri(section["ServerAddress"], origin),
            EventHubPath = section["EventHubPath"] is { Length: > 0 } hub ? hub : "hub/events",
            PageSize = ReadInt(section["PageSize"], 60, 1, 500),
            ProbeInitialDelay = ReadSeconds(section["ProbeInitialDelaySeconds"], 2),
            ProbeMaximumDelay = ReadSeconds(section["ProbeMaximumDelaySeconds"], 30),
            RequestTimeout = ReadSeconds(section["RequestTimeoutSeconds"], 20),
        };
    }

    private static Uri ReadUri(string? configured, Uri fallback)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var parsed))
        {
            throw new InvalidOperationException(
                $"'{ClientOptions.SectionName}:ServerAddress' is not an absolute address: '{configured}'.");
        }

        return parsed.AbsoluteUri.EndsWith('/') ? parsed : new Uri(parsed.AbsoluteUri + "/");
    }

    private static int ReadInt(string? configured, int fallback, int minimum, int maximum)
        => int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static TimeSpan ReadSeconds(string? configured, int fallbackSeconds)
        => TimeSpan.FromSeconds(ReadInt(configured, fallbackSeconds, 1, 3600));
}
