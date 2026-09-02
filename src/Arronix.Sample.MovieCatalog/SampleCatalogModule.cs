using System;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;

namespace Arronix.Sample.MovieCatalog;

/// <summary>The sample package's entry module.</summary>
/// <remarks>
/// One closed registration, naming <c>Movie</c> once. The package requires the movies package by identifier
/// and never references its executable assembly, so the <c>Movie</c> this generic closes over is the one
/// the installation admitted rather than a copy shipped here.
/// </remarks>
public sealed class SampleCatalogModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("sample.movie.catalog");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry.AddCataloger<SampleMovieCataloger>(new ProviderDescriptor
        {
            LocalId = "sample-movies",
            Name = "Sample movie catalog",
            Description =
                "Invented titles shipped with this installation so the catalog path can be evaluated "
                + "without an account anywhere. It needs no settings; search for \"sample\" to see "
                + "everything it holds.",
            Settings = [],
        });
    }
}
