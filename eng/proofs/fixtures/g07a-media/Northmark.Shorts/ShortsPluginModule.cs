using System;
using Arronix.Abstractions.Plugins;

namespace Northmark.Shorts.Extension;

/// <summary>Registers the short-film media kind.</summary>
public sealed class ShortsPluginModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("northmark.shorts");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry.AddMediaType<Shorts>();
    }
}
