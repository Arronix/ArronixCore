using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// Rule 4 - the HTTP surface is a plain server and must never become a renderer.
/// </summary>
/// <remarks>
/// <para>
/// The server publishes a media-agnostic REST API and an event stream, and serves whichever client is
/// deployed beside it as static files. It does not render one. Keeping the two apart is what makes a
/// second front end - a terminal client, a native application, another web client - a peer of the first
/// rather than a fork of the server.
/// </para>
/// <para>
/// A markup file is the visible half of the rule; a rendering package is the half that arrives first.
/// Both are checked, and so is the strongest form of the statement: this project takes no package at all,
/// because everything it needs is in the shared framework.
/// </para>
/// </remarks>
[TestFixture]
public class ApiTopologyTests
{
    private static readonly string[] MarkupPatterns = ["*.razor", "*.razor.cs", "*.razor.css", "*.cshtml"];

    private static readonly string[] RenderingPackageMarkers =
    [
        "Blazor",
        "Razor",
        "Components",
        "Mvc"
    ];

    [Test]
    public void ApiContainsNoMarkupFile()
    {
        var markup = MarkupPatterns
            .SelectMany(pattern => RepositoryLayout.Files(RepositoryLayout.Api, pattern))
            .Select(RepositoryLayout.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            markup,
            Is.Empty,
            "The HTTP surface has acquired a markup file. It is a REST and event-stream server; rendering "
            + "belongs to a client that can be replaced without touching it.");
    }

    [Test]
    public void ApiDeclaresNoRenderingPackage()
    {
        var project = ProjectFile.Load(RepositoryLayout.Api);

        var rendering = project
            .PackageReferences
            .Where(package => RenderingPackageMarkers.Any(
                marker => package.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.That(rendering, Is.Empty);
    }

    [Test]
    public void ApiDeclaresNoPackageReferenceAtAll()
    {
        var project = ProjectFile.Load(RepositoryLayout.Api);

        Assert.That(
            project.PackageReferences,
            Is.Empty,
            "An empty package set is the strongest mechanically checkable form of 'the server is not a "
            + "renderer': a rendering package cannot arrive without this failing first.");
    }

    [Test]
    public void ApiDoesNotReferenceTheClient()
    {
        var project = ProjectFile.Load(RepositoryLayout.Api);

        Assert.That(
            project.ProjectReferences,
            Does.Not.Contain(RepositoryLayout.Client),
            "The server serves a configured client root rather than compiling a client in, which is what "
            + "keeps the client independently hostable and the server front-end agnostic.");
    }
}
