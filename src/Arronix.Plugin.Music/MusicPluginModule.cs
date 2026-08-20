using Arronix.Abstractions.Plugins;
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
/// Legacy media-engine objects are still constructed here until this kind moves to the typed media path.
/// Providers are different: registration contributes the implementation type and the host activates it
/// through DI only after admission.
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
            .AddIndexer<MusicIndexer>(MusicIndexer.Describe())
            .AddIntentSurface(MusicIntent.Declaration);
    }
}
