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
    /// Names a media kind, a media assembly, or a path some particular installation happens to serve.
    /// </summary>
    /// <remarks>
    /// The client renders whichever contract a host admitted, so anything it spells about one media kind is
    /// a kind it renders better than the others. A default payload path is the same mistake wearing a
    /// different hat: it is one installation's fixture compiled into every deployment, and the panel that
    /// carried it would stop being the panel an external consumer's contract proves itself through.
    /// </remarks>
    private static readonly string[] ForbiddenSpellings =
    [
        "Arronix.Media.",
        "Arronix.Format.",
        "Arronix.Plugin.",
        "fixtures/",
        "movie.json"
    ];

    [Test]
    public void ClientSpellsNoMediaKindAndNoInstallationsOwnPath()
    {
        var offenders = SourceScanner
            .Lines(RepositoryLayout.Client, "*.cs", "*.razor")
            .Where(entry => ForbiddenSpellings.Any(
                spelling => entry.Text.Contains(spelling, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "The client names a media assembly or a path one installation happens to serve. Both make it "
            + "a client for that installation rather than for whichever contract a host admitted.");
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

    private const string Composition =
        "src/Arronix.Client/Composition/ArronixClientServiceCollectionExtensions.cs";

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
        ("LoadInstallationAsync", [Reloader]),
        ("SweepStoreAsync", [Reloader]),
        ("ClearContractsAsync", [Reloader]),
        ("ListContractHashesAsync", [Reloader, Janitor]),

        // Bytes reach the runtime only through the pass that hashes and proves them, and a poisoned entry
        // is repaired by the same pass that refused it.
        ("ReadContractAsync", [Loader]),
        ("WriteContractAsync", [Loader]),
        ("RemoveContractAsync", [Janitor, Loader]),
    ];

    /// <summary>
    /// Names a reader without a compiler would confuse one of these operations with.
    /// </summary>
    /// <remarks>
    /// This fixture matches text, not symbols, so an operation sharing its name with an ordinary member of
    /// something else would refuse a caller that never touched the store — a stream read, a cache clear, a
    /// component's own load. The rule stays sound in both directions by the operations being named for what
    /// they act on, which is also what lets it match a member rather than only a call.
    /// </remarks>
    private static readonly string[] AmbiguousMemberNames =
    [
        "ReadAsync",
        "WriteAsync",
        "RemoveAsync",
        "ClearAsync",
        "LoadAsync",
        "KeysAsync",
        "SweepAsync",
        "FlushAsync",
        "SaveAsync",
        "GetAsync",
        "SetAsync"
    ];

    [Test]
    public void ClientChangesWhatItHoldsThroughOneTransactionOnly()
    {
        foreach (var (operation, mayBeCalledIn) in LifecycleOperations)
        {
            var reaches = SourceScanner
                .CodeLines(RepositoryLayout.Client, "*.cs", "*.razor")
                .Where(entry => Reaches(entry.Text, operation))
                .ToArray();

            var owned = reaches
                .Where(entry => mayBeCalledIn.Contains(entry.File, StringComparer.Ordinal))
                .ToArray();

            Assert.That(
                owned,
                Is.Not.Empty,
                $"'{operation}' is named as a lifecycle operation but is reached nowhere in the layer "
                + "that owns it. A stale vocabulary entry would otherwise make this rule pass vacuously "
                + "after the real operation was renamed.");

            var offenders = reaches
                .Where(entry => !mayBeCalledIn.Contains(entry.File, StringComparer.Ordinal))
                .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                offenders,
                Is.Empty,
                $"'{operation}' is reached outside the layer that owns it. Every change to "
                + "what this page holds of an installation runs under one lease, and every read of the "
                + "store that a page could pair with an installation is inside it, so a second caller "
                + "reintroduces exactly the overlap that lease exists to prevent.");
        }
    }

    [Test]
    public void ClientLifecycleOperationsAreNamedForWhatTheyActOn()
    {
        var ambiguous = LifecycleOperations
            .Select(entry => entry.Operation)
            .Where(operation => AmbiguousMemberNames.Contains(operation, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            ambiguous,
            Is.Empty,
            "A lifecycle operation shares its name with an ordinary member something else could carry. "
            + "This rule reads text rather than symbols, so that name would refuse a caller which never "
            + "touched the store - a stream read, a cache clear, a component loading its own data - and a "
            + "governance rule that cries wolf is a governance rule that gets suppressed.");
    }

    [TestCase("await body.ReadAsync(buffer, cancellationToken);", false)]
    [TestCase("var held = await _cache.ClearAsync();", false)]
    [TestCase("protected override Task OnInitializedAsync() => LoadAsync();", false)]
    [TestCase("await other.ReadContractAsyncTwice();", false)]
    [TestCase("public async Task<bool> ClearContractsAsync()", false)]
    [TestCase("await Store.ReadContractAsync(hash);", true)]
    [TestCase("await Store.ClearContractsAsync();", true)]
    [TestCase("await Contracts.LoadInstallationAsync();", true)]
    [TestCase("Func<Task<bool>> Escape => Store.ClearContractsAsync;", true)]
    [TestCase("_ = Task.Run(Store.ClearContractsAsync);", true)]
    [TestCase("var name = nameof(Store.RemoveContractAsync);", true)]
    public void ClientLifecycleRuleReadsTheMemberAndNotTheWordAroundIt(string line, bool refused)
    {
        var matched = LifecycleOperations.Any(entry => Reaches(line, entry.Operation));

        Assert.That(
            matched,
            Is.EqualTo(refused),
            refused
                ? "reaching a layer this rule owns must be seen, whether it is called or handed on"
                : "an ordinary member of something else must not be mistaken for one");
    }

    [TestCase("private readonly ContractReloader _reloader;", true)]
    [TestCase("@inject ContractReloader Reloader", true)]
    [TestCase("private readonly NotAContractReloader _other;", false)]
    [TestCase("private readonly ContractReloaderFactory _factory;", false)]
    [TestCase("private readonly ContractReloadResult _result;", false)]
    public void ClientTypeRuleReadsTheNameAndNotTheWordAroundIt(string line, bool refused)
    {
        Assert.That(
            Names(line, "ContractReloader"),
            Is.EqualTo(refused),
            refused
                ? "reaching the transaction must be seen"
                : "a type whose name merely contains it must not be mistaken for it");
    }

    /// <summary>Determines whether one line names a type, and not one whose name merely contains it.</summary>
    /// <param name="line">The source line.</param>
    /// <param name="type">The type name.</param>
    /// <returns>Whether the line names it.</returns>
    /// <remarks>
    /// Bounded on both sides: without a leading boundary <c>NotAContractReloader</c> would answer for
    /// <c>ContractReloader</c>, and without a trailing one <c>ContractReloaderFactory</c> would.
    /// </remarks>
    private static bool Names(string line, string type)
    {
        for (var index = line.IndexOf(type, StringComparison.Ordinal);
             index >= 0;
             index = line.IndexOf(type, index + 1, StringComparison.Ordinal))
        {
            if (!IsIdentifierPart(line, index - 1) && !IsIdentifierPart(line, index + type.Length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether one line reaches a member of the layer that owns an operation.</summary>
    /// <param name="line">The source line.</param>
    /// <param name="operation">The member name.</param>
    /// <returns>Whether the line reaches it.</returns>
    /// <remarks>
    /// The member, not the call. Requiring an open parenthesis would let a method group through — handing
    /// <c>Store.ClearContractsAsync</c> to something that invokes it later is the same second door with the
    /// call site moved. The leading dot skips the declaration, and the trailing boundary keeps a longer
    /// name that merely starts the same way from matching.
    /// </remarks>
    private static bool Reaches(string line, string operation)
    {
        var member = "." + operation;

        for (var index = line.IndexOf(member, StringComparison.Ordinal);
             index >= 0;
             index = line.IndexOf(member, index + 1, StringComparison.Ordinal))
        {
            if (!IsIdentifierPart(line, index + member.Length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether one position in a line is part of an identifier.</summary>
    /// <param name="line">The source line.</param>
    /// <param name="index">The position, which may fall outside the line.</param>
    /// <returns>Whether a name could continue there.</returns>
    private static bool IsIdentifierPart(string line, int index)
        => index >= 0
            && index < line.Length
            && (char.IsLetterOrDigit(line[index]) || line[index] == '_');

    /// <summary>Where the transaction that owns the lifecycle may be reached from.</summary>
    /// <remarks>
    /// Its own definition, the view that is the lifecycle surface, and the composition root that constructs
    /// it. A fourth consumer is a second surface: it would reload without committing what it produced, and
    /// two consumers would disagree about which installation this page is showing.
    /// </remarks>
    private static readonly string[] ReloaderConsumers =
    [
        Reloader,
        "src/Arronix.Client/Contracts/ContractView.cs",
        Composition
    ];

    [Test]
    public void ClientReachesTheTransactionThroughItsViewOnly()
    {
        var offenders = SourceScanner
            .CodeLines(RepositoryLayout.Client, "*.cs", "*.razor")
            .Where(entry => Names(entry.Text, "ContractReloader"))
            .Where(entry => !ReloaderConsumers.Contains(entry.File, StringComparer.Ordinal))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "The transaction is reached outside the view that shows what it produced. One surface commits "
            + "a transaction's sealed record and announces it; a second consumer reloading beside that "
            + "would change what this page holds without anything committing the result, and the two would "
            + "disagree about which installation is being shown.");
    }

    /// <summary>What a component is written in.</summary>
    /// <remarks>
    /// Markup and its code-behind both, because moving an injection from one to the other is not a change
    /// of layer. The client has no code-behind file today, which is exactly why this is stated rather than
    /// left to be noticed the day one appears.
    /// </remarks>
    private static readonly string[] ComponentSources = ["*.razor", "*.razor.cs"];

    [Test]
    public void ClientComponentSourcesCoverMarkupAndCodeBehind()
    {
        Assert.That(
            ComponentSources,
            Is.EquivalentTo(new[] { "*.razor", "*.razor.cs" }),
            "A component is its markup and its code-behind. A rule reading only one of them is a door "
            + "that opens the day somebody moves an injection across the file boundary.");
    }

    [Test]
    public void ClientMarkupShowsAnInstallationThroughItsViewOnly()
    {
        var offenders = SourceScanner
            .CodeLines(RepositoryLayout.Client, ComponentSources)
            .Where(entry => Names(entry.Text, "MediaContractLoader"))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "Markup reaches the loader. Its report is live state that moves whenever a transaction runs, so "
            + "a render reading it beside a committed snapshot would show one moment's installation next to "
            + "another's stored keys and failures - the pairing a sealed snapshot exists to prevent. This "
            + "rule covers a component's markup and its code-behind, and nothing else, on purpose: "
            + "ordinary code may hold the loader to read back what an admitted contract declares, which is "
            + "what typed projection needs and is not presentation state.");
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
