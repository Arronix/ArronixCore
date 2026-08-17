using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Topology;

/// <summary>
/// Rule 3 - the browser client is held to an extension's discipline.
/// </summary>
/// <remarks>
/// <para>
/// The client is the second isolation boundary in the platform and the less obvious of the two. Whatever
/// it references is downloaded by, and readable in, a browser. Holding it to exactly one project
/// reference means no host-side implementation assembly can be shipped to a client by accident: not the
/// runtime, not the loader, not the platform library, and not the HTTP surface whose secret-redaction
/// code would be an interesting read for anyone who received it.
/// </para>
/// <para>
/// Read from the project file rather than from the compiled assembly on purpose. This fixture must not
/// reference the client - doing so would put a browser-targeted assembly in a desktop test process and,
/// worse, would make the rule unenforceable whenever the client failed to build.
/// </para>
/// </remarks>
[TestFixture]
public class ClientTopologyTests
{
    private static readonly string[] ForbiddenProjects =
    [
        RepositoryLayout.Common,
        RepositoryLayout.Plugins,
        RepositoryLayout.Host,
        RepositoryLayout.Api
    ];

    [Test]
    public void ClientDeclaresExactlyOneProjectReferenceOnTheContractAssembly()
    {
        var project = ProjectFile.Load(RepositoryLayout.Client);

        Assert.That(
            project.ProjectReferences,
            Is.EqualTo(new[] { RepositoryLayout.Abstractions }),
            "The client references the contract assembly and nothing else, exactly as an extension does. "
            + "Anything else it referenced would be shipped to a browser.");
    }

    [Test]
    public void ClientNamesNoHostSideProjectAnywhereInItsProjectFile()
    {
        var project = ProjectFile.Load(RepositoryLayout.Client);

        var named = ForbiddenProjects
            .Where(forbidden => project.Text.Contains(forbidden + ".csproj", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            named,
            Is.Empty,
            "A host-side project is named in the client's project file. Even behind a condition or with "
            + "the output reference switched off, that is a reference a future edit can turn real.");
    }

    [Test]
    public void ClientReferencesNoHostSideNamespaceInItsSource()
    {
        var forbiddenNamespaces = ForbiddenProjects
            .SelectMany(static name => new[] { "using " + name, "@using " + name })
            .ToArray();

        var offenders = SourceScanner
            .Lines(RepositoryLayout.Client, "*.cs", "*.razor")
            .Where(entry => forbiddenNamespaces.Any(
                prefix => entry.Text.TrimStart().StartsWith(prefix, StringComparison.Ordinal)))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty, "The client names a host-side namespace.");
    }
}
