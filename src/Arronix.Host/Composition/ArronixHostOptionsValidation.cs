using Arronix.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Host.Composition;

/// <summary>
/// Binds and validates every options type the host owns.
/// </summary>
/// <remarks>
/// One method, one registration per options type, in one file: what an operator can configure is then
/// answerable by reading a single screen rather than by searching for scattered configuration calls. Every
/// type is validated at startup, so a mistake surfaces once, as the host starts, naming the section and the
/// member — rather than later, as a null reference from inside whichever subsystem read the value first.
/// </remarks>
internal static class ArronixHostOptionsValidation
{
    /// <summary>
    /// Registers the host's options types against the supplied configuration.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">The configuration the sections are read from.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddArronixHostOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatedOptions<HostOptions>(configuration, HostOptions.SectionName);
        services.AddValidatedOptions<LibraryOptions>(configuration, LibraryOptions.SectionName);
        services.AddValidatedOptions<SchedulerOptions>(configuration, SchedulerOptions.SectionName);
        services.AddValidatedOptions<HealthOptions>(configuration, HealthOptions.SectionName);
        services.AddValidatedOptions<StoreOptions>(configuration, StoreOptions.SectionName);

        return services;
    }

    private static void AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services
            .AddOptionsWithValidateOnStart<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations();
    }
}
