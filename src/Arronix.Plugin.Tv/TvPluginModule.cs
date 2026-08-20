
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Plugin.Tv.Providers;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv;

/// <summary>
/// The television extension's single entry point.
/// </summary>
/// <remarks>
/// <para>Exactly one public, parameterless-constructible module per assembly. <see cref="Configure"/>
/// registers and does nothing else — no I/O, no network, no long work — and a throwing <c>Configure</c>
/// quarantines this extension rather than failing the host.</para>
/// <para>Legacy media-engine seams are built here until Television completes its typed migration. Provider
/// registrations carry implementation types instead; the host activates those through DI only after the
/// capability declaration is admitted.</para>
/// </remarks>
public sealed class TvPluginModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString(TvIds.PluginIdValue);

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var catalog = TvCatalog.CreateSeeded();

        context.Registry

            // media-kind
            .AddMediaShape(new TvShape())
            .AddMediaItemSource(new TvItemSource(catalog))

            // parsing / matching / indexing / quality
            .AddReleaseParser(new TvReleaseParser())
            .AddReleaseMatcher(new TvReleaseMatcher(catalog))
            .AddReleaseQueryPlanner(new TvQueryPlanner(catalog))
            .AddQualityModel(new TvQualityModel())

            // renaming
            .AddRenamePolicy(new TvRenamePolicy(catalog))
            .AddLibraryLayout(new TvLibraryLayout(catalog))

            // metadata
            .AddMediaIdResolver(new TvIdResolver(catalog))
            // indexing, provider side
            .AddIndexer<TvIndexer>(TvIndexer.Describe())

            // declared intent - data only, never code
            .AddIntentSurface(TvIntent.Create());

        context.Telemetry.Info(
            $"Television extension configured with {catalog.Series.Count} library entries and "
            + $"{catalog.Episodes.Count} units across three addressing schemes.",
            clock: context.Clock);
    }
}
