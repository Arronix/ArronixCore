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

    /// <summary>
    /// Members whose whole purpose is to enumerate a type surface the compiler never saw.
    /// </summary>
    /// <remarks>
    /// The client loads media contract assemblies at run time, so it is permanently one careless call away
    /// from being a reflection host. Two consequences, and both are why this rule exists rather than a
    /// review convention: an application that enumerates an unknown assembly's members cannot be trimmed
    /// or compiled ahead of time, and discovery by enumeration is a second, undeclared media schema - the
    /// client would be deciding what a media kind contains by reading its properties, which is exactly the
    /// string-bag model the typed contracts exist to replace.
    ///
    /// What the loader may do is bounded and named: read an assembly's identity, its manifest module and
    /// its reference table. None of those describes a type, and all three are what proving an identity
    /// needs.
    /// </remarks>
    private static readonly string[] ForbiddenReflection =
    [
        ".GetTypes(",
        ".GetExportedTypes(",
        ".GetProperties(",
        ".GetFields(",
        ".GetMethods(",
        ".GetMembers(",
        "Activator.CreateInstance"
    ];

    [Test]
    public void ClientDiscoversNothingByEnumeratingALoadedAssembly()
    {
        var offenders = SourceScanner
            .Lines(RepositoryLayout.Client, "*.cs", "*.razor")
            .Where(entry => !entry.Text.TrimStart().StartsWith("///", StringComparison.Ordinal)
                && !entry.Text.TrimStart().StartsWith("//", StringComparison.Ordinal)
                && ForbiddenReflection.Any(member => entry.Text.Contains(member, StringComparison.Ordinal)))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "The client acquires media contracts at run time. Enumerating what one contains would make the "
            + "client untrimmable and would make property reflection a second media schema beside the typed "
            + "contracts. Generated metadata is how a contract says what it holds.");
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
