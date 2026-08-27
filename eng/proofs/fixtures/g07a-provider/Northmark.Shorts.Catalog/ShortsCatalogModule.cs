using System;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;

namespace Northmark.Shorts.Catalog;

/// <summary>Registers the Northmark short-film catalog.</summary>
public sealed class ShortsCatalogModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("northmark.shorts.catalog");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry.AddCataloger<ShortFilmCataloger>(new ProviderDescriptor
        {
            LocalId = "northmark-shorts",
            Name = "Northmark",
            Description = "Short-film metadata and identifiers from the Northmark catalog.",
            Settings = []
        });
    }
}
