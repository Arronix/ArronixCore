using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// What the movies package actually contains once it is staged the way an operator would install it.
/// </summary>
/// <remarks>
/// <para>
/// The other packaged fixtures drive the package through the loader and assert what it produces. This one
/// asserts what it <i>is</i>, because the package split is a claim about a folder: one manifest, one entry
/// assembly, and the shared contract assembly a separately shipped provider compiles against. A layout
/// that quietly stopped matching that shape would still load today and would fail later, at the point a
/// second package tried to resolve one of these names.
/// </para>
/// <para>
/// The expected payload is written out rather than derived. Deriving it from the same build that produced
/// it would assert that the build agrees with itself; writing it down means the next person to change what
/// a package ships has to say so here.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class PackagedMoviesLayoutTests
{
    /// <summary>The isolated entry assembly, which the manifest names and the loader invokes.</summary>
    private const string EntryAssembly = "Arronix.Plugin.Movies.dll";

    /// <summary>The shared contract assembly this package publishes.</summary>
    private const string SharedContractAssembly = "Arronix.Media.Movies.dll";

    /// <summary>
    /// The video package assembly this package still carries privately.
    /// </summary>
    /// <remarks>
    /// A recorded consequence, not an endorsement. The movies extension composes video, and with no
    /// dependency declaration and no admitted-contract resolution yet, MSBuild copies the video domain
    /// assembly into this folder and the loader loads it privately. The moment Television is installed
    /// beside Movies as a separate package, each gets its own copy and therefore its own <c>Video</c> type,
    /// and nothing casts between them. Removing this name is the observable result of the manifest
    /// dependency and loader work; this list is what will change when it lands.
    /// </remarks>
    private static readonly string[] PrivatelyCarriedVideoPackage = ["Arronix.Format.Video.dll"];

    /// <summary>
    /// Assemblies no media extension payload may contain, whatever else changes.
    /// </summary>
    /// <remarks>
    /// Unlike the list above, this one is not waiting on the loader. A format's executable half has its own
    /// update and unload cadence and a media declaration needs nothing from it, so a copy appearing here
    /// would mean a reference was taken that should not have been - pinning two independently updatable
    /// assemblies together and shipping a second copy of code the video package already owns.
    /// </remarks>
    private static readonly string[] NeverInAMediaExtensionPayload =
    [
        "Arronix.Format.Video.Contributions.dll",
    ];

    /// <summary>
    /// The universal contracts, present in the folder but resolved from the default context at load time.
    /// </summary>
    private static readonly string[] ShadowedByTheLoadContext = ["Arronix.Abstractions.dll"];

    private static string PackageFolder =>
        Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", "movies");

    [Test]
    public void ThePackageShipsOneManifestOneEntryAssemblyAndItsSharedContract()
    {
        var assemblies = ManagedFileNames();

        using var assertions = new AssertionScope();

        File.Exists(Path.Combine(PackageFolder, "plugin.json")).Should().BeTrue(
            "the build must stage the real package before its layout can be asserted");

        assemblies.Should().Contain(
            EntryAssembly,
            "the manifest names one entry assembly and the loader has to find it");

        assemblies.Should().Contain(
            SharedContractAssembly,
            "a package that publishes an item type ships it as its own assembly, so a provider can pair "
            + "with the type without taking the extension");

        assemblies.Should().BeEquivalentTo(
            new[] { EntryAssembly, SharedContractAssembly }
                .Concat(PrivatelyCarriedVideoPackage)
                .Concat(ShadowedByTheLoadContext),
            "the package payload is a stated shape. An assembly appearing here that nobody wrote down is "
            + "either a new dependency or a private copy of somebody else's contract, and the difference "
            + "matters enough to be declared rather than discovered.");
    }

    /// <remarks>
    /// The manifest names the entry assembly and says nothing about the shared one. That silence is the
    /// current state, not the destination: which assemblies a package offers for sharing is a promise about
    /// resolution that the loader must act on before any code runs, so it belongs in the manifest. Until
    /// the manifest schema carries it, this fixture is the only place the shape is stated at all.
    /// </remarks>
    [Test]
    public void TheManifestNamesTheEntryAssemblyAndNotYetTheSharedOne()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(PackageFolder, "plugin.json")));

        var root = manifest.RootElement;

        using var assertions = new AssertionScope();
        root.GetProperty("entryAssembly").GetString().Should().Be(EntryAssembly);
        root.TryGetProperty("dependencies", out _).Should().BeFalse(
            "package dependencies enter the manifest with the loader work, not with the assembly split");
        root.TryGetProperty("facets", out _).Should().BeFalse(
            "so does the declaration of which assemblies this package offers for sharing");
    }

    /// <remarks>
    /// The direction of the reference inside the package. The entry assembly uses the shared one; if the
    /// shared one ever used the entry assembly, resolving the shared name would pull the extension in with
    /// it and the split would buy nothing.
    /// </remarks>
    [Test]
    public void TheEntryAssemblyUsesTheSharedContractAndNotTheReverse()
    {
        var entry = MetadataOnlyReferencesOf(EntryAssembly);
        var shared = MetadataOnlyReferencesOf(SharedContractAssembly);

        using var assertions = new AssertionScope();
        entry.Should().Contain("Arronix.Media.Movies");
        shared.Should().NotContain(
            "Arronix.Plugin.Movies",
            "a shared contract assembly that referenced its own package's entry assembly would share it too");
        shared.Where(name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(["Arronix.Abstractions"]);
    }

    /// <remarks>
    /// The rule stated where an operator would see it. A package payload is a folder, and a private copy of
    /// another package's executable assembly is a file in that folder - so this is the observation the
    /// reference rules in the architecture fixtures exist to make impossible earlier. Both video dependants
    /// are checked, because a rule proved against one package is a rule about that package.
    /// </remarks>
    /// <param name="payloadFolder">The staged payload directory.</param>
    [Test]
    [TestCase("PackagedPlugins/movies")]
    [TestCase("PackagePayloads/tv")]
    public void NoMediaExtensionPayloadCarriesAFormatsExecutableHalf(string payloadFolder)
    {
        var folder = Path.Combine(
            AppContext.BaseDirectory,
            payloadFolder.Replace('/', Path.DirectorySeparatorChar));

        var assemblies = ManagedFileNamesIn(folder);

        using var assertions = new AssertionScope();

        assemblies.Should().NotBeEmpty(
            "the build must stage the payload before this rule can assert anything about it");

        assemblies.Should().Contain(
            "Arronix.Format.Video.dll",
            "both dependants compose video, so the domain assembly is expected in the payload and its "
            + "absence would mean this rule was checking the wrong folder");

        assemblies.Should().NotIntersectWith(
            NeverInAMediaExtensionPayload,
            "a media declaration needs a format's domain semantics and nothing from its executable half, "
            + "so a copy here means a reference was taken that pins two independently updatable assemblies "
            + "together");
    }

    /// <summary>
    /// Every assembly in a staged payload is one the package's own dependency manifest names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the rule that makes the payload assertions worth anything, and it exists because the obvious
    /// way to stage a package is wrong. Copying a project's <c>bin</c> directory recursively looks like it
    /// produces the payload, but MSBuild does not delete an assembly that a removed <c>ProjectReference</c>
    /// stopped producing - so the folder can keep a file the project no longer depends on, the copy carries
    /// it, and a payload test then faithfully reports a dependency that does not exist.
    /// </para>
    /// <para>
    /// The staging targets publish instead, which computes the runtime closure from the current reference
    /// set rather than listing a directory. <c>deps.json</c> is that same computation written down, so
    /// comparing the folder against it catches anything that arrived by any other route - including a
    /// future change back to a recursive copy. <see cref="StagedPayloadDetectorTests"/> proves
    /// this rule fails when a stale assembly is present, so it cannot pass by not looking.
    /// </para>
    /// </remarks>
    /// <param name="payloadFolder">The staged payload directory.</param>
    /// <param name="dependencyManifest">The package's dependency manifest inside it.</param>
    [Test]
    [TestCase("PackagedPlugins/movies", "Arronix.Plugin.Movies.deps.json")]
    [TestCase("PackagePayloads/tv", "Arronix.Plugin.Tv.deps.json")]
    [TestCase("G02PackagedPlugins/g02.admission.fixture", "Arronix.Host.Tests.G02AdmissionFixture.deps.json")]
    public void NoStagedPayloadCarriesAnAssemblyItsDependencyManifestDoesNotName(
        string payloadFolder,
        string dependencyManifest)
    {
        var folder = Path.Combine(
            AppContext.BaseDirectory,
            payloadFolder.Replace('/', Path.DirectorySeparatorChar));

        var staged = ManagedFileNamesIn(folder);
        var declared = RuntimeAssembliesOf(Path.Combine(folder, dependencyManifest));

        using var assertions = new AssertionScope();

        staged.Should().NotBeEmpty("the build must stage the payload before this rule can assert anything");
        declared.Should().NotBeEmpty("a payload without a readable dependency manifest proves nothing");

        staged.Should().BeSubsetOf(
            declared,
            "a staged assembly the package's own dependency manifest does not name did not come from the "
            + "current reference set - it survived in a source directory and was copied");
    }

    /// <summary>
    /// Reads the runtime assembly names a package's dependency manifest declares.
    /// </summary>
    /// <param name="manifestPath">The <c>deps.json</c> inside the staged payload.</param>
    /// <returns>The bare assembly file names, or an empty list when the manifest is absent.</returns>
    internal static IReadOnlyList<string> RuntimeAssembliesOf(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        if (!manifest.RootElement.TryGetProperty("targets", out var targets))
        {
            return [];
        }

        return targets
            .EnumerateObject()
            .SelectMany(static target => target.Value.EnumerateObject())
            .Where(static library => library.Value.TryGetProperty("runtime", out _))
            .SelectMany(static library => library.Value.GetProperty("runtime").EnumerateObject())
            .Select(static entry => Path.GetFileName(entry.Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ManagedFileNames() => ManagedFileNamesIn(PackageFolder);

    internal static IReadOnlyList<string> ManagedFileNamesIn(string folder) =>
        Directory.Exists(folder)
            ? Directory
                .EnumerateFiles(folder, "*.dll")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

    /// <summary>
    /// Reads an assembly's reference table without running any of it.
    /// </summary>
    /// <param name="fileName">The file inside the staged package.</param>
    /// <returns>The referenced assembly simple names.</returns>
    /// <remarks>
    /// A reflection-only read, in a throwaway context, because this fixture is about what the staged bytes
    /// say. Loading the file for real would resolve it against the test process instead, which is the one
    /// place its references are guaranteed to be satisfiable.
    /// </remarks>
    private static IReadOnlyList<string> MetadataOnlyReferencesOf(string fileName)
    {
        using var stream = File.OpenRead(Path.Combine(PackageFolder, fileName));
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();

        return metadata
            .AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
