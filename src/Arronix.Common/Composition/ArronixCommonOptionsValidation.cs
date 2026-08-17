using Arronix.Common.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Common.Composition;

/// <summary>
/// Binds and validates every options type the platform implementation assembly owns.
/// </summary>
/// <remarks>
/// <para>
/// One method, one registration per options type, in one file: the set of things an operator can configure
/// is then answerable by reading a single screen of code rather than by searching for scattered
/// <c>Configure</c> calls.
/// </para>
/// <para>
/// Every options type is bound, validated against its data annotations and marked for validation at
/// startup. A configuration mistake therefore surfaces once, as the host starts, naming the section and the
/// member — not later, as a null reference from inside whichever subsystem happened to read the value
/// first.
/// </para>
/// </remarks>
internal static class ArronixCommonOptionsValidation
{
    /// <summary>
    /// Registers the platform's options types against the supplied configuration.
    /// </summary>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">The configuration the sections are read from.</param>
    /// <returns>The same collection, for chaining.</returns>
    internal static IServiceCollection AddArronixCommonOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatedOptions<HostIdentityOptions>(configuration, HostIdentityOptions.SectionName);
        services.AddValidatedOptions<LoggingOptions>(configuration, LoggingOptions.SectionName);
        services.AddValidatedOptions<FileSystemOptions>(configuration, FileSystemOptions.SectionName);
        services.AddValidatedOptions<HttpClientOptions>(configuration, HttpClientOptions.SectionName);
        services.AddValidatedOptions<ProxyOptions>(configuration, ProxyOptions.SectionName);
        services.AddValidatedOptions<RedactionOptions>(configuration, RedactionOptions.SectionName);

        return services;
    }

    /// <summary>
    /// Binds one options type to one configuration section and requires it to validate before the host is
    /// considered started.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The collection being built.</param>
    /// <param name="configuration">The configuration the section is read from.</param>
    /// <param name="sectionName">Path of the section holding the values.</param>
    /// <remarks>
    /// Binding the section rather than the root also registers a change token for it, so a subsystem that
    /// watches its options through a monitor observes a reload without any further wiring.
    /// </remarks>
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
