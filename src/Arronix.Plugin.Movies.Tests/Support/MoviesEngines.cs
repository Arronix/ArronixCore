#pragma warning disable ARX0013 // Shape contracts are experimental; these tests exercise the declaration.
#pragma warning disable ARX0014 // Plugin contracts are experimental; the contribution is one of them.
#pragma warning disable ARX0019
#pragma warning disable ARX0020 // Definition contracts are experimental; these tests exercise the declaration.

using System.Globalization;
using System.IO;
using System.Linq;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;
using Arronix.Host.Composition;
using Arronix.Host.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Plugin.Movies.Tests.Support;

/// <summary>
/// The declaration, bound through the host's real composition root, so a fixture can run a release title
/// through the engine the declaration actually builds.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here reaches into <c>Arronix.Host</c>'s internals, and it deliberately does not:
/// <see cref="MediaTypeBinder"/>, <see cref="DefinitionEngineCatalog"/> and
/// <see cref="RegisteredMediaKind"/> are all public, and the seams they hand back —
/// <see cref="IReleaseParser"/> and the rest — are contract types. A plugin's test project reaching for
/// <c>InternalsVisibleTo</c> would be the wrong precedent: the public binder exists precisely so it does
/// not have to, and a fixture that can only run against internals is a fixture asserting something no
/// consumer can observe.
/// </para>
/// <para>
/// The engines come from <c>AddArronixHost</c> rather than from a fixture catalog, so what these tests run
/// is what the host runs. Bound once: the definition is immutable and the engines compile it at
/// construction, so there is nothing per-test to reset.
/// </para>
/// <para>
/// <b>What is still out of reach from here.</b> The bound kind's item source is the host's pre-storage one
/// and holds no rows, because this milestone has no persistence and no metadata pipeline. Everything that
/// reads items through it — matching, query planning, catalog projection, and any rename that has to
/// resolve an item — therefore has nothing to answer with, and the fixtures that need those stay ignored
/// with that as their reason. Parsing needs no items at all, which is why the parse corpus runs here.
/// </para>
/// </remarks>
internal static class MoviesEngines
{
    private static readonly Lazy<RegisteredMediaKind> Bound = new(Bind, isThreadSafe: true);

    /// <summary>The admitted kind, with every seam the host built from the declaration.</summary>
    internal static RegisteredMediaKind Kind => Bound.Value;

    /// <summary>The host's parse engine, compiled from the declared release models.</summary>
    internal static IReleaseParser Parser => Kind.Parser!;

    /// <summary>Reads one release title through the parse engine.</summary>
    /// <param name="releaseTitle">The release title.</param>
    /// <returns>The reading, or <see langword="null"/> when the engine declined the text.</returns>
    internal static Abstractions.DTOs.ParsedRelease? Parse(string releaseTitle) => Parser.Parse(releaseTitle);

    /// <summary>
    /// The revision the parse engine read out of a release title.
    /// </summary>
    /// <param name="releaseTitle">The release title.</param>
    /// <returns>The revision.</returns>
    /// <remarks>
    /// The stable release DTO carries no revision field — a rung name is the whole of what it publishes —
    /// so the engine records the claim under three well-known metadata keys and this reassembles them.
    /// That indirection is the reason the boundary test below exists.
    /// </remarks>
    internal static QualityRevision Revision(string releaseTitle)
    {
        var parsed = Parse(releaseTitle)
            ?? throw new InvalidOperationException($"The parse engine declined '{releaseTitle}'.");

        var metadata = parsed.AdditionalMetadata
            ?? throw new InvalidOperationException($"'{releaseTitle}' carried no parse metadata.");

        return new QualityRevision(
            int.Parse(metadata["parse.revision.version"], CultureInfo.InvariantCulture),
            int.Parse(metadata["parse.revision.real"], CultureInfo.InvariantCulture),
            bool.Parse(metadata["parse.revision.isRepack"]));
    }

    private static RegisteredMediaKind Bind()
    {
        var root = Path.Combine(Path.GetTempPath(), "arronix-movies-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = Path.Combine(root, "extensions"),
                ["Arronix:Plugins:RootFolder"] = Path.Combine(root, "extensions"),
                ["Arronix:Library:RootFolders:0"] = root,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixHost(configuration);

        var provider = services.BuildServiceProvider();
        var binder = provider.GetRequiredService<MediaTypeBinder>();

        var admitted = binder.TryRegister(
            new TypedContribution
            {
                Plugin = new MoviesPluginModule().Id,
                PluginVersion = "0.1.0",
                Capabilities = CapabilitySet.Of(
                    Capability.MediaKind,
                    Capability.Parsing,
                    Capability.Matching,
                    Capability.Indexing,
                    Capability.Quality,
                    Capability.Renaming,
                    Capability.Metadata,
                    Capability.Notification),
                Registration = MediaTypeRegistration.For<Movie, Movies>(),
            },
            out var registered,
            out var defects);

        return admitted
            ? registered!
            : throw new InvalidOperationException(
                "The host refused the movie definition: "
                + string.Join("; ", defects.Select(defect => $"{defect.Path}: {defect.Message}")));
    }
}
