using System;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Movies;
using Arronix.Provider.Tmdb.Settings;

namespace Arronix.Provider.Tmdb;

/// <summary>The provider package's entry module.</summary>
/// <remarks>
/// One closed registration each, naming <c>Movie</c> once. The package requires the movies package by
/// identifier and never references its executable assembly, so the <c>Movie</c> these generics close over
/// is the one the installation admitted rather than a copy shipped here.
/// </remarks>
public sealed class TmdbPluginModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("tmdb");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry
            .AddCataloger<Movie, TmdbMovieCataloger>(new ProviderDescriptor
            {
                LocalId = "tmdb-movies",
                Family = ProviderFamily.Cataloger,
                Name = "TMDb",
                Description = "Movie metadata and external identifiers from TMDb.",
                Settings = TmdbProviderSettings.Fields,
            })
            .AddCurator<Movie, TmdbMovieCurator>(new ProviderDescriptor
            {
                LocalId = "tmdb-popular",
                Family = ProviderFamily.Curator,
                Name = "TMDb Popular",
                Description = "TMDb's popular-movies list, as a curated selection source.",
                Settings = TmdbProviderSettings.Fields,
            });
    }
}
