#pragma warning disable ARX0011 // Telemetry contracts are experimental; they are the extension's diagnostic surface.
#pragma warning disable ARX0014 // Extension contracts are experimental; a media extension is their intended implementer.

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
/// <para>Every seam this extension contributes is built here and handed over already constructed. The host
/// never activates a type this assembly owns and never reflects over one, which is what makes the
/// capability gate an admission check: registering something the manifest did not declare is not a thing
/// this method can express.</para>
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
            .AddCataloger(TvCataloger.Registration(Id, catalog))

            // indexing, provider side
            .AddIndexer(TvIndexer.Registration(Id, catalog, context.Clock))

            // declared intent - data only, never code
            .AddIntentSurface(TvIntent.Create());

        context.Telemetry.Info(
            $"Television extension configured with {catalog.Series.Count} library entries and "
            + $"{catalog.Episodes.Count} units across three addressing schemes.",
            clock: context.Clock);
    }
}
