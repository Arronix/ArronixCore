
using Arronix.Abstractions.Plugins;

namespace Arronix.Plugin.Movies;

/// <summary>Registers the movie media type with the plugin host.</summary>
public sealed class MoviesPluginModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("movies");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry.AddMediaType<Movies>();
    }
}
