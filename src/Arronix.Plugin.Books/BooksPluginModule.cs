// The plugin, shape and provider contracts are experimental; this extension implements all three.
#pragma warning disable ARX0013, ARX0014, ARX0015
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Plugin.Books.Providers;

namespace Arronix.Plugin.Books;

/// <summary>
/// The single entry point of the books extension.
/// </summary>
/// <remarks>
/// The registrations here are exactly the capabilities the manifest declares, no more and no fewer. That
/// is not a coincidence to be maintained by hand: registering a seam whose capability was not granted is
/// refused as it happens, and declaring a capability nothing was registered against is refused immediately
/// afterwards. Between them the two checks make the manifest and this method provably the same statement.
/// </remarks>
public sealed class BooksPluginModule : IPluginModule
{
    /// <summary>The identifier this extension is known by, matching its manifest.</summary>
    public const string Identifier = "books";

    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString(Identifier);

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry
            .AddMediaShape(new BooksShape())
            .AddMediaItemSource(new BooksItemSource())
            .AddReleaseParser(new BooksReleaseParser())
            .AddReleaseMatcher(new BooksReleaseMatcher())
            .AddReleaseQueryPlanner(new BooksQueryPlanner())
            .AddQualityModel(new BooksQualityModel())
            .AddRenamePolicy(new BooksRenamePolicy())
            .AddLibraryLayout(new BooksLibraryLayout())
            .AddIndexer(new IndexerRegistration(BooksIndexer.Describe(), new BooksIndexer(Id)))
            .AddCataloger(
                new CatalogerRegistration(
                    BooksCataloger.Describe(),
                    new BooksCataloger(Id)))
            .AddIntentSurface(BooksIntent.Declaration);
    }
}
