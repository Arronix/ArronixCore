using Arronix.Host.Languages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arronix.Host.Composition;

/// <summary>Registers the language-capability runtime.</summary>
internal static class LanguageRegistration
{
    internal static IServiceCollection AddLanguageRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<LanguageDefinitionRegistry>();
        services.TryAddSingleton<LanguageTextService>();
        return services;
    }
}
