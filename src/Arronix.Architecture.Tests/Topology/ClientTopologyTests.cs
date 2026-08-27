using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// The client's contract path: one serialized transaction, one view over it, one page rendering it.
    /// </summary>
    /// <remarks>
    /// Scoped rather than repo-wide. These files are the ones where a dropped task or a second read of
    /// mutable state changes what a browser is told about an installation, and the client has other
    /// fire-and-forget work — a retry, a probe — that is not this.
    /// </remarks>
    private static readonly string[] ContractPath =
    [
        "src/Arronix.Client/Contracts/",
        "src/Arronix.Client/Services/ContractStateWatcher.cs",
        "src/Arronix.Client/Pages/ContractsPage.razor"
    ];

    private const string Reloader = "src/Arronix.Client/Contracts/ContractReloader.cs";
    private const string Janitor = "src/Arronix.Client/Contracts/ContractStoreJanitor.cs";
    private const string Loader = "src/Arronix.Client/Contracts/MediaContractLoader.cs";

    /// <summary>
    /// The operations that change what this page holds, and the only files that may call them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading an installation, shedding the bytes it no longer names and emptying the store are one
    /// serialized transaction. A call outside that lease is a second path into it: a load started beside
    /// one can write bytes a sweep already running against an older installation then evicts, and a store
    /// emptied beside one leaves a page describing a moment that never existed.
    /// </para>
    /// <para>
    /// Operations, deliberately, not types. Typed projection reads what a loaded contract declares through
    /// the same loader, and a rule that banned naming it would ban that too — reading what this page holds
    /// is not changing it. A definition does not match a dotted call, so a layer declaring one of these is
    /// not a caller of it.
    /// </para>
    /// </remarks>
    private static readonly (string Operation, string[] MayBeCalledIn)[] LifecycleOperations =
    [
        // Reading an installation, and shedding or emptying what this browser holds of one.
        (".LoadAsync(", [Reloader]),
        (".SweepAsync(", [Reloader]),
        (".ClearAsync(", [Reloader]),
        (".KeysAsync(", [Reloader, Janitor]),

        // Bytes reach the runtime only through the pass that hashes and proves them, and a poisoned entry
        // is repaired by the same pass that refused it.
        (".ReadAsync(", [Loader]),
        (".WriteAsync(", [Loader]),
        (".RemoveAsync(", [Janitor, Loader]),
    ];

    [Test]
    public void ClientChangesWhatItHoldsThroughOneTransactionOnly()
    {
        foreach (var (operation, mayBeCalledIn) in LifecycleOperations)
        {
            var offenders = SourceScanner
                .CodeLines(RepositoryLayout.Client, "*.cs", "*.razor")
                .Where(entry => entry.Text.Contains(operation, StringComparison.Ordinal))
                .Where(entry => !mayBeCalledIn.Contains(entry.File, StringComparer.Ordinal))
                .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                offenders,
                Is.Empty,
                $"'{operation.Trim('.', '(')}' is called outside the layer that owns it. Every change to "
                + "what this page holds of an installation runs under one lease, and every read of the "
                + "store that a page could pair with an installation is inside it, so a second caller "
                + "reintroduces exactly the overlap that lease exists to prevent.");
        }
    }

    [Test]
    public void ClientContractPathObservesTheWorkItsEventHandlersStart()
    {
        var offenders = SourceScanner
            .CodeLines(RepositoryLayout.Client, "*.cs", "*.razor")
            .Where(entry => ContractPath.Any(
                scope => entry.File.StartsWith(scope, StringComparison.Ordinal)))
            .Where(entry => entry.Text.Contains("_ =", StringComparison.Ordinal)
                && entry.Text.Contains("Async(", StringComparison.Ordinal))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "Every boundary in the client's contract path records the failures it contains, so the only "
            + "thing a discarded task can carry away is the class contained nowhere - an exhausted heap, "
            + "corrupted memory, a structured native failure. An event handler has no caller to return "
            + "one to, so it observes its own work as an async void boundary instead of dropping it.");
    }

    /// <summary>What a page may read off the view it renders.</summary>
    /// <remarks>
    /// The snapshot is the whole observation. The rest is subscription and command, neither of which is
    /// rendered state.
    /// </remarks>
    private static readonly string[] ReadableViewMembers =
        ["Snapshot", "Changed", "ReloadAsync", "DiscardStoredBytesAsync"];

    [Test]
    public void ClientPagesReadOneViewSnapshotPerRender()
    {
        var pages = RepositoryLayout
            .Files(RepositoryLayout.Client, "*.razor")
            .Select(path => (Path: RepositoryLayout.Relative(path), Text: File.ReadAllText(path)))
            .Where(page => page.Text.Contains("@inject ContractView ", StringComparison.Ordinal))
            .ToArray();

        Assert.That(pages, Is.Not.Empty, "no page injects the view, so this rule would pass vacuously");

        foreach (var (path, text) in pages)
        {
            var name = text.Split("@inject ContractView ")[1]
                .Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
            var read = Regex
                .Matches(text, $@"\b{Regex.Escape(name)}\.([A-Za-z_][A-Za-z0-9_]*)")
                .Select(match => match.Groups[1].Value)
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(
                    read.Count(member => member == "Snapshot"),
                    Is.EqualTo(1),
                    $"{path} must capture the view's snapshot exactly once and render from that local. A "
                    + "render that reads it twice describes two moments, and cannot tell that it did.");

                Assert.That(
                    read.Distinct().Except(ReadableViewMembers).Order(StringComparer.Ordinal),
                    Is.Empty,
                    $"{path} reads presentation state off the view outside its snapshot. Report, held "
                    + "keys and failures are published together and are read together.");
            });
        }
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
