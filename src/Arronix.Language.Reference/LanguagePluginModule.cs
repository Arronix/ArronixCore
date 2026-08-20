
using System;
using Arronix.Abstractions.Plugins;

namespace Arronix.Languages.Reference;

/// <summary>Registers the reference language implementations by type.</summary>
public sealed class LanguagePluginModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("languages.reference");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry
            .AddLanguage<EnglishLanguageDefinition>()
            .AddLanguage<GermanLanguageDefinition>()
            .AddLanguage<FrenchLanguageDefinition>();
    }
}
