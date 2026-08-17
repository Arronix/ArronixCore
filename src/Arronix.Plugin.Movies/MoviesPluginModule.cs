#pragma warning disable ARX0014 // Extension contracts are experimental; a media extension is their intended implementer.

using Arronix.Abstractions.Plugins;

namespace Arronix.Plugin.Movies;

/// <summary>
/// The movie extension's single entry point.
/// </summary>
/// <remarks>
/// <para>Exactly one public, parameterless-constructible module per assembly. Zero is a load failure and so
/// is more than one: ambiguity about which module owns an assembly is a defect, not a feature.</para>
/// <para>
/// The whole extension is one call. A typed media kind is registered by naming its item type and its type
/// declaration and nothing else: the host replays <see cref="Movies.Configure"/> against its own builder,
/// reads <see cref="Movie"/>'s properties and attributes, and derives the structure, the intent surface and
/// the naming tokens from them. There is no shape to hand over because there is no second source of truth
/// for one.
/// </para>
/// <para>
/// One promise the old declarative module made is void and is retracted here rather than left standing: a
/// typed kind ships code — the dotted-acronym rewrite, the two recomputations and the template rule — so
/// the assembly is no longer eligible for unload once its declaration is captured, and the network
/// privilege is no longer structurally ungrantable. The capability gate is enforced by the manifest and the
/// loader, as it is for every other extension that ships code.
/// </para>
/// </remarks>
public sealed class MoviesPluginModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("movies");

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry.AddMediaType<Movie, Movies>();
    }
}
