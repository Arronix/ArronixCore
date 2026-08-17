// The plugin, shape and provider contracts are experimental; this extension implements all three.
#pragma warning disable ARX0013, ARX0014, ARX0015
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Plugin.Music.Providers;

namespace Arronix.Plugin.Music;

/// <summary>
/// The single entry point of the music extension.
/// </summary>
/// <remarks>
/// <para>
/// One method, called once, before anything is active. Everything the extension contributes is registered
/// here through the closed registration surface: there is no container to add to, no assembly to scan and
/// no way to contribute something that was not declared in the manifest. Registration of a seam whose
/// capability was not granted is refused at this point, and a capability granted with nothing registered
/// against it is refused immediately afterwards - the two checks run in opposite directions and between
/// them leave no room for a mismatch.
/// </para>
/// <para>
/// Objects are constructed here, by this extension, from what the context hands it. Nothing is activated
/// on its behalf, which is why per-definition state on a provider is not merely discouraged but
/// unrepresentable.
/// </para>
/// </remarks>
public sealed class MusicPluginModule : IPluginModule
{
    /// <summary>The identifier this extension is known by, matching its manifest.</summary>
    public const string Identifier = "music";

    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString(Identifier);

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry
            .AddMediaShape(new MusicShape())
            .AddMediaItemSource(new MusicItemSource())
            .AddReleaseParser(new MusicReleaseParser())
            .AddReleaseMatcher(new MusicReleaseMatcher())
            .AddReleaseQueryPlanner(new MusicQueryPlanner())
            .AddQualityModel(new MusicQualityModel())
            .AddRenamePolicy(new MusicRenamePolicy())
            .AddLibraryLayout(new MusicLibraryLayout())
            .AddIndexer(new IndexerRegistration(MusicIndexer.Describe(), new MusicIndexer(Id)))
            .AddCataloger(
                new CatalogerRegistration(
                    MusicCataloger.Describe(),
                    new MusicCataloger(Id)))
            .AddIntentSurface(MusicIntent.Declaration);
    }
}
