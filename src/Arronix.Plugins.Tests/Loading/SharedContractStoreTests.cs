using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Tests.Support;
using Arronix.Plugins.Versioning;
using FluentAssertions.Execution;

namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// The installation's shared contract authority, against real files on disk.
/// </summary>
/// <remarks>
/// The inputs are emitted rather than compiled because the decisive facts cannot be varied within one
/// build: the same assembly name at two versions, an assembly carrying a module initializer, a file whose
/// metadata reads perfectly and which the runtime refuses to load. Each is a real file the metadata reader
/// sees exactly as it would a shipped one.
/// </remarks>
[TestFixture]
internal sealed class SharedContractStoreTests
{
    private const string SharedName = "Emitted.Shared.Contract";
    private const string OtherName = "Emitted.Other.Contract";

    private static readonly Version One = new(1, 0, 0, 0);
    private static readonly Version Two = new(2, 0, 0, 0);
    private static readonly VersionRange Any = VersionRangeParser.Parse(">=0.1");

    private string _root = string.Empty;
    private readonly List<SharedContractStore> _stores = [];

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("arronix-shared-contracts").FullName;

    [TearDown]
    public void TearDown()
    {
        foreach (var store in _stores)
        {
            store.TryRequestUnload(out _);
        }

        _stores.Clear();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void AnOrdinaryContractIsAdmittedOnceAndResolvesToOneAssemblyObject()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var store = Admit(publisher);
        var scope = store.OpenScope(publisher);

        using var assertions = new AssertionScope();

        var admitted = store.Admitted.Should().ContainSingle().Which;
        admitted.Identity.Name.Should().Be(SharedName);
        admitted.Identity.Version.Should().Be(One);
        admitted.Publisher.Should().Be(PluginId.FromString("publisher"));

        var first = scope.Resolve(new AssemblyName(SharedName) { Version = One });
        var second = scope.Resolve(new AssemblyName(SharedName) { Version = One });

        first.Should().NotBeNull().And.BeSameAs(second, "sharing is the same object, not the same bytes");
        AssemblyLoadContext.GetLoadContext(first!)!.Name.Should().Be(SharedContractStore.ContextName);
        AssemblyLoadContext.GetLoadContext(first!)!.IsCollectible.Should().BeTrue();
    }

    [Test]
    public void AContractCarryingAModuleInitializerIsRefusedBeforeItIsLoaded()
    {
        var folder = Path.Combine(_root, "publisher");
        var path = EmittedContract.Write(folder, SharedName, One, moduleInitializer: true);

        // Asserted first, so the refusal below cannot pass for the wrong reason.
        StagedAssembly.TryStage(path, out var staged, out _).Should().BeTrue();
        staged!.HasModuleInitializer.Should().BeTrue();

        var refusal = RefusalOf(Package("publisher", folder, contracts: [SharedName + ".dll"]));

        using var assertions = new AssertionScope();
        refusal.Code.Should().Be(CoreErrorCode.PluginIsolationViolation);
        refusal.Defects.Should().ContainSingle().Which.Should().Contain("module initializer");
    }

    [Test]
    public void AnOtherwiseIdenticalContractWithoutAModuleInitializerIsAdmitted()
    {
        var publisher = Publisher("publisher", SharedName, One);

        Admit(publisher).Admitted.Should().ContainSingle();
    }

    [Test]
    public void TwoPackagesCannotPublishTheSameContractAssemblyName()
    {
        var first = Publisher("aaa", SharedName, One);
        var second = Publisher("zzz", SharedName, One);
        var store = Store();
        var admission = store.Admit(Graph(first, second));

        using var assertions = new AssertionScope();
        admission.Admitted.Should().ContainSingle().Which.Publisher.Should().Be(PluginId.FromString("aaa"));

        var refusal = admission.Refusals[PluginId.FromString("zzz")];
        refusal.Code.Should().Be(CoreErrorCode.PluginIdConflict);
        refusal.Defects.Should().ContainSingle().Which.Should().Contain("already published");
    }

    [Test]
    public void TwoContractsDifferingOnlyInLetterCaseCannotBothBeAdmitted()
    {
        var first = Publisher("aaa", "Emitted.Case.Contract", One);
        var second = Publisher("zzz", "emitted.case.contract", One);

        Store().Admit(Graph(first, second)).Refusals[PluginId.FromString("zzz")]
            .Defects.Should().ContainSingle()
            .Which.Should().Contain("differs only in letter case");
    }

    [Test]
    public void AnAdmittedContractResolvesUnderAnyLetterCase()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var scope = Admit(publisher).OpenScope(publisher);

        scope.Resolve(new AssemblyName(SharedName.ToUpperInvariant()) { Version = One })
            .Should().NotBeNull("the runtime binds simple names case-insensitively, so Arronix must too");
    }

    [Test]
    public void AContractDeclarationNamingThePackagesEntryAssemblyIsRefused()
    {
        var folder = Path.Combine(_root, "publisher");
        EmittedContract.Write(folder, SharedName, One);

        var declare = () => Package(
            "publisher",
            folder,
            entry: SharedName + ".dll",
            contracts: [SharedName + ".dll"]);

        declare.Should().Throw<ArgumentException>()
            .WithMessage(
                "*entry assembly*",
                "the executable half's isolation, update and unload lifetime is what a package boundary "
                + "exists to keep separate from the types a dependant binds to");
    }

    [Test]
    public void APackageWithNoEntryAssemblyMayStillPublishAContract()
    {
        var publisher = Publisher("publisher", SharedName, One);

        publisher.HasEntryAssembly.Should().BeFalse();
        Admit(publisher).Admitted.Should().ContainSingle();
    }

    [Test]
    public void AContractWithNoDeclaredAssemblyVersionIsRefused()
    {
        var folder = Path.Combine(_root, "publisher");
        EmittedContract.Write(folder, SharedName, new Version(0, 0, 0, 0));

        var refusal = RefusalOf(Package("publisher", folder, contracts: [SharedName + ".dll"]));

        using var assertions = new AssertionScope();
        refusal.Code.Should().Be(CoreErrorCode.PluginContractMismatch);
        refusal.Defects.Should().ContainSingle().Which.Should().Contain("AssemblyVersion");
    }

    [Test]
    public void TheSharedContextRefusesAHostImplementationAssemblyAndTheHostContractItself()
    {
        var folder = Path.Combine(_root, "publisher");
        EmittedContract.Write(folder, "Arronix.Abstractions", One);

        RefusalOf(Package("publisher", folder, contracts: ["Arronix.Abstractions.dll"]))
            .Defects.Should().ContainSingle()
            .Which.Should().Contain("host or framework assembly");
    }

    /// <remarks>
    /// Global admission is not global visibility. The dependant declares no dependency on the publisher, so
    /// the name resolves for nobody but its own publisher and the request is refused rather than falling
    /// through to a private copy or the default context.
    /// </remarks>
    [Test]
    public void AnAdmittedContractOutsideAPackagesClosureIsRefusedRatherThanResolved()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var stranger = Package("stranger", Path.Combine(_root, "stranger"));
        Directory.CreateDirectory(stranger.Folder);

        var store = Admit(publisher, stranger);
        var scope = store.OpenScope(stranger);

        using var assertions = new AssertionScope();

        scope.VisibleNames.Should().BeEmpty();

        var resolve = () => scope.Resolve(new AssemblyName(SharedName) { Version = One });
        resolve.Should().Throw<PluginIsolationException>()
            .WithMessage("*global admission is not global visibility*");

        scope.Resolve(new AssemblyName("Something.Nobody.Published")).Should().BeNull(
            "a name the installation never admitted is the package's own private resolver's business");
    }

    [Test]
    public void AContractIsVisibleToAPackageThatDeclaredTheDependency()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var dependant = Package(
            "dependant",
            Path.Combine(_root, "dependant"),
            requirements: [new PackageRequirement(PluginId.FromString("publisher"), Any)]);
        Directory.CreateDirectory(dependant.Folder);

        var scope = Admit(publisher, dependant).OpenScope(dependant);

        using var assertions = new AssertionScope();
        scope.VisibleNames.Should().Equal(SharedName);
        scope.Resolve(new AssemblyName(SharedName) { Version = One }).Should().NotBeNull();
    }

    /// <remarks>
    /// The metadata half of the visibility rule. The runtime half is proved above; this one refuses the
    /// package before it is loaded, so a contract cannot even be admitted against a closure the installation
    /// did not grant it.
    /// </remarks>
    [Test]
    public void AContractReferencingAnAdmittedContractOutsideItsOwnClosureIsRefusedBeforeLoading()
    {
        var publisher = Publisher("publisher", SharedName, One);

        var sourcePath = Path.Combine(publisher.Folder, SharedName + ".dll");
        var referenced = LoadIsolated(sourcePath).GetType(EmittedContract.ItemTypeName)!;

        // A second publisher whose own contract binds to the first publisher's types, without declaring the
        // dependency that would make them visible to it.
        var stranger = Package("stranger", Path.Combine(_root, "stranger"), contracts: [OtherName + ".dll"]);
        EmittedContract.Write(stranger.Folder, OtherName, One, reference: referenced);

        var admission = Store().Admit(Graph(publisher, stranger));

        using var assertions = new AssertionScope();

        admission.Admitted.Should().ContainSingle().Which.Publisher.Should()
            .Be(PluginId.FromString("publisher"));

        admission.Refusals[PluginId.FromString("stranger")]
            .Defects.Should().ContainSingle()
            .Which.Should().Contain("outside package 'stranger's declared dependency closure");
    }

    /// <remarks>
    /// A shared contract is not an executable facet. The positive control comes first, so the refusal cannot
    /// pass because the detector never fired.
    /// </remarks>
    [Test]
    public void AnAssemblyWithAManagedEntryPointIsRefusedAsASharedContract()
    {
        var folder = Path.Combine(_root, "publisher");
        var executable = EmittedContract.WriteExecutable(folder, "Emitted.Executable.Contract", One);

        StagedAssembly.TryStage(executable, out var staged, out _).Should().BeTrue();
        staged!.HasEntryPoint.Should().BeTrue(
            "the fixture really does declare an entry point, so the refusal below means something");

        var refusal = RefusalOf(Package(
            "publisher",
            folder,
            contracts: [Path.GetFileName(executable)]));

        using var assertions = new AssertionScope();
        refusal.Code.Should().Be(CoreErrorCode.PluginIsolationViolation);
        refusal.Defects.Should().ContainSingle().Which.Should().Contain("managed entry point");
    }

    /// <remarks>
    /// Admission cannot be made atomic by withholding entries from a dictionary: a load context keeps its own
    /// binding cache, and that cache answers before any resolver runs. So the transaction is the context, and
    /// a publisher whose second contract fails at the real load boundary leaves neither of its contracts
    /// behind — including the sibling that had already loaded, which is precisely the poisoning this exists
    /// to prevent.
    /// </remarks>
    [Test]
    public void APublisherWhoseSecondContractCannotLoadLeavesNeitherBindingBehind()
    {
        var publisher = Package(
            "publisher",
            Path.Combine(_root, "publisher"),
            contracts: [SharedName + ".dll", OtherName + ".dll"]);

        EmittedContract.Write(publisher.Folder, SharedName, One);
        EmittedContract.WriteUnloadable(publisher.Folder, OtherName, One);

        var unrelated = Publisher("unrelated", "Emitted.Unrelated.Contract", One);

        var store = Store();
        var admission = store.Admit(Graph(publisher, unrelated));

        using var assertions = new AssertionScope();

        admission.Refusals.Keys.Should().Equal(PluginId.FromString("publisher"));
        admission.Admitted.Select(contract => contract.Identity.Name).Should().BeEquivalentTo(
            ["Emitted.Unrelated.Contract"],
            "a genuinely independent publisher is preserved");

        store.Admitted.Should().NotContain(contract => contract.Identity.Name == SharedName);

        // The surviving context is a different object, so the withdrawn sibling is not answering its own
        // name from a binding cache no map of ours governs.
        var scope = store.OpenScope(unrelated);
        scope.Resolve(new AssemblyName(SharedName) { Version = One }).Should().BeNull();

        // The control: the first contract really was loadable, so the withdrawal was the transaction's doing.
        var alone = Package("publisher", publisher.Folder, contracts: [SharedName + ".dll"]);
        Store().Admit(Graph(alone)).Admitted.Select(contract => contract.Identity.Name)
            .Should().BeEquivalentTo([SharedName]);
    }

    [Test]
    public void ResolvingAnAdmittedContractUnderAnotherIdentityThrowsRatherThanBinding()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var scope = Admit(publisher).OpenScope(publisher);

        var resolve = () => scope.Resolve(new AssemblyName(SharedName) { Version = Two });

        resolve.Should().Throw<SharedContractIdentityException>()
            .Which.Message.Should().Contain("Version=2.0.0.0").And.Contain("Version=1.0.0.0");
    }

    [Test]
    public void APackageCarryingItsOwnCopyOfAnAdmittedContractIsRefusedWithBothModuleIdentifiers()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var dependant = Package(
            "dependant",
            Path.Combine(_root, "dependant"),
            requirements: [new PackageRequirement(PluginId.FromString("publisher"), Any)]);

        // A different build of the same name and version: the compiler stamps a fresh module identifier
        // into every build, so this is the case a name-and-version comparison cannot see.
        EmittedContract.Write(dependant.Folder, SharedName, One);

        var store = Admit(publisher, dependant);

        store.TryCheckPackage(dependant, out var code, out var defects).Should().BeFalse();

        using var assertions = new AssertionScope();
        code.Should().Be(CoreErrorCode.PluginIsolationViolation);

        var defect = defects.Should().ContainSingle().Which;
        defect.Should().Contain("private copy");
        defect.Should().Contain("MVID");
        defect.Should().Contain("SHA-256");

        var admitted = store.Admitted.Single();
        defect.Should().Contain(admitted.ModuleVersionId.ToString());
        defect.Should().Contain(admitted.ContentHash);

        StagedAssembly.TryStage(Path.Combine(dependant.Folder, SharedName + ".dll"), out var planted, out _)
            .Should().BeTrue();
        planted!.ModuleVersionId.Should().NotBe(
            admitted.ModuleVersionId,
            "otherwise this fixture would be comparing a file with itself");
        defect.Should().Contain(planted.ModuleVersionId.ToString()).And.Contain(planted.ContentHash);
    }

    /// <remarks>
    /// <para>
    /// The isolation walk runs before the loader's per-package try, so a failure escaping it does not
    /// quarantine one package — it ends the load pass, and every package not yet attempted is never
    /// attempted at all. A folder whose contents cannot be listed is the reachable way to produce one:
    /// staging already contains an unreadable <i>file</i>, but nothing contained an unreadable
    /// <i>directory</i>.
    /// </para>
    /// <para>
    /// The directory is left executable and not readable, which is the state in which a known path still
    /// opens — so discovery reads the manifest exactly as it always would — and only enumeration is refused.
    /// The repository's proof rail is bash and its CI is Linux, so file modes are available wherever this
    /// runs.
    /// </para>
    /// </remarks>
    [Test]
    public void APackageWhoseFolderCannotBeListedIsRefusedRatherThanEndingTheLoadPass()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var dependant = Package(
            "dependant",
            Path.Combine(_root, "dependant"),
            requirements: [new PackageRequirement(PluginId.FromString("publisher"), Any)]);

        Directory.CreateDirectory(dependant.Folder);
        EmittedContract.Write(dependant.Folder, "Emitted.Dependant", One);

        var store = Admit(publisher, dependant);

        Unlistable(dependant.Folder);

        try
        {
            // The arrangement has to bite, or this fixture proves nothing.
            var enumerate = () => Directory.EnumerateFiles(dependant.Folder, "*.dll").ToArray();
            enumerate.Should().Throw<UnauthorizedAccessException>(
                "the fixture must actually deny enumeration for the refusal to mean anything");

            var checkedPackage = store.TryCheckPackage(dependant, out var code, out var defects);

            using var assertions = new AssertionScope();
            checkedPackage.Should().BeFalse();
            code.Should().Be(CoreErrorCode.PluginLoadFailure);
            defects.Should().ContainSingle().Which.Should()
                .Contain(dependant.Folder)
                .And.Contain("could not be listed");

            store.Admitted.Should().ContainSingle(
                "one package's unreadable folder says nothing about what the installation already shares");
            store.TryCheckPackage(publisher, out _, out var publisherDefects).Should().BeTrue(
                "and it says nothing about any other package either");
            publisherDefects.Should().BeEmpty();
        }
        finally
        {
            Relistable(dependant.Folder);
        }
    }

    [Test]
    public void ThePublishersOwnCopyIsNotTreatedAsADuplicateOfItself()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var store = Admit(publisher);

        store.TryCheckPackage(publisher, out _, out var defects).Should().BeTrue();
        defects.Should().BeEmpty();
    }

    [Test]
    public void APackageBuiltAgainstAnotherIdentityOfTheContractIsRefusedBeforeLoading()
    {
        var publisher = Publisher("publisher", SharedName, One);

        // Compiled against version 2 of the same contract name, which the installation did not admit.
        var strangerFolder = Path.Combine(_root, "reference-source");
        var strangerPath = EmittedContract.Write(strangerFolder, SharedName, Two);
        var strangerType = LoadIsolated(strangerPath).GetType(EmittedContract.ItemTypeName)!;

        var dependant = Package(
            "dependant",
            Path.Combine(_root, "dependant"),
            requirements: [new PackageRequirement(PluginId.FromString("publisher"), Any)]);
        EmittedContract.Write(dependant.Folder, "Emitted.Dependant", One, reference: strangerType);

        var store = Admit(publisher, dependant);

        store.TryCheckPackage(dependant, out var code, out var defects).Should().BeFalse();

        using var assertions = new AssertionScope();
        code.Should().Be(CoreErrorCode.PluginContractMismatch);
        defects.Should().ContainSingle().Which.Should()
            .Contain("Version=2.0.0.0").And.Contain("Version=1.0.0.0")
            .And.Contain("MVID").And.Contain("SHA-256");
    }

    [Test]
    public void StagedBytesAreReadOnceAndSurviveReplacementOfTheFile()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var store = Admit(publisher);
        var admitted = store.Admitted.Single();

        var path = Path.Combine(publisher.Folder, SharedName + ".dll");
        EmittedContract.Write(publisher.Folder, SharedName, One);

        StagedAssembly.TryStage(path, out var replaced, out _).Should().BeTrue();

        using var assertions = new AssertionScope();
        replaced!.ModuleVersionId.Should().NotBe(
            admitted.ModuleVersionId,
            "otherwise the invariance below would mean nothing");
        store.Admitted.Single().ModuleVersionId.Should().Be(admitted.ModuleVersionId);
    }

    [Test]
    public void AMalformedContractRefusesOnlyItsOwnPublisher()
    {
        var sound = Publisher("sound", OtherName, One);
        var broken = Package("broken", Path.Combine(_root, "broken"), contracts: ["Emitted.Broken.dll"]);
        EmittedContract.WriteMalformed(broken.Folder, "Emitted.Broken.dll", MalformedShape.Garbage);

        var admission = Store().Admit(Graph(sound, broken));

        using var assertions = new AssertionScope();
        admission.Admitted.Should().ContainSingle().Which.Publisher.Should().Be(PluginId.FromString("sound"));
        admission.Refusals.Keys.Should().Equal(PluginId.FromString("broken"));
    }

    /// <param name="shape">The way the candidate is malformed.</param>
    [TestCase(MalformedShape.Garbage)]
    [TestCase(MalformedShape.Empty)]
    [TestCase(MalformedShape.TruncatedHeader)]
    [TestCase(MalformedShape.TruncatedBody)]
    public void StagingReportsAMalformedCandidateRatherThanThrowing(MalformedShape shape)
    {
        var path = EmittedContract.WriteMalformed(_root, "Emitted.Malformed.dll", shape);

        StagedAssembly.TryStage(path, out var staged, out var error).Should().BeFalse();
        staged.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    /// <remarks>
    /// A deterministic sweep rather than a hand-picked file. Metadata that parses but is internally corrupt
    /// fails in ways no single example anticipates — a hostile culture string reaches
    /// <c>AssemblyName.CultureName</c> and throws a globalization failure — and an escape here would abort
    /// a whole installation's admission over one bad file in one package.
    /// </remarks>
    [Test]
    public void StagingNeverThrowsForAnyCorruptionOfAValidAssembly()
    {
        var source = EmittedContract.Write(Path.Combine(_root, "source"), SharedName, One);
        var length = new FileInfo(source).Length;
        var escaped = new List<string>();

        for (var offset = 0; offset < length; offset += 64)
        {
            var path = Path.Combine(_root, "corrupted", $"Emitted.Corrupted.{offset}.dll");
            EmittedContract.WriteCorrupted(source, path, offset, 48);

            try
            {
                StagedAssembly.TryStage(path, out _, out _);
            }
#pragma warning disable CA1031 // Recording exactly what escaped is the point of the sweep.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                escaped.Add($"{offset}: {failure.GetType().Name}");
            }
        }

        escaped.Should().BeEmpty();
    }

    /// <remarks>
    /// The declared package edge is the semantic authority, not the metadata a dependant's own contracts
    /// happen to reference. The dependant here never names the failed package's assemblies and is refused
    /// anyway, because it required the package; so is a package that publishes no contract at all.
    /// </remarks>
    [Test]
    public void AContractThatCannotLoadWithdrawsItsPackageDependantsWhateverTheirMetadataSays()
    {
        var failing = Package("failing", Path.Combine(_root, "failing"), contracts: [SharedName + ".dll"]);
        EmittedContract.WriteUnloadable(failing.Folder, SharedName, One);

        var dependant = Package(
            "dependant",
            Path.Combine(_root, "dependant"),
            contracts: [OtherName + ".dll"],
            requirements: [new PackageRequirement(PluginId.FromString("failing"), Any)]);
        EmittedContract.Write(dependant.Folder, OtherName, One);

        var transitive = Package(
            "transitive",
            Path.Combine(_root, "transitive"),
            requirements: [new PackageRequirement(PluginId.FromString("dependant"), Any)]);
        Directory.CreateDirectory(transitive.Folder);

        var unrelated = Publisher("unrelated", "Emitted.Unrelated.Contract", One);

        var store = Store();
        var admission = store.Admit(Graph(failing, dependant, transitive, unrelated));

        using var assertions = new AssertionScope();

        admission.Refusals.Keys.Select(id => id.Value).Should().BeEquivalentTo(
            ["failing", "dependant", "transitive"]);

        admission.Refusals[PluginId.FromString("dependant")].Reason.Should().Contain("failing");
        admission.Refusals[PluginId.FromString("transitive")].Code.Should()
            .Be(CoreErrorCode.PluginDependencyUnavailable);

        admission.Admitted.Select(contract => contract.Identity.Name).Should().BeEquivalentTo(
            ["Emitted.Unrelated.Contract"],
            "a genuinely independent publisher survives, and no withdrawn package's bytes remain live");

        store.Admitted.Should().ContainSingle();
    }

    [Test]
    public void AHoldIsReleasedByReferenceAndTheContextIsPinnedUntilEveryDependantHasWithdrawn()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var dependant = Package(
            "dependant",
            Path.Combine(_root, "dependant"),
            requirements: [new PackageRequirement(PluginId.FromString("publisher"), Any)]);
        Directory.CreateDirectory(dependant.Folder);

        var store = Admit(publisher, dependant);
        var first = store.OpenScope(publisher);
        var second = store.OpenScope(dependant);

        using var assertions = new AssertionScope();

        store.TryRequestUnload(out var refusal).Should().BeFalse();
        refusal.Should().Contain("publisher").And.Contain("dependant");

        first.Release();
        first.Release();
        store.Holders.Should().Equal(PluginId.FromString("dependant"));

        store.TryRequestUnload(out _).Should().BeFalse();

        second.Release();
        store.TryRequestUnload(out _).Should().BeTrue();
        store.UnloadRequested.Should().BeTrue();
        store.Admitted.Should().BeEmpty();

        var resolve = () => second.Resolve(new AssemblyName(SharedName) { Version = One });
        resolve.Should().Throw<InvalidOperationException>("a released hold serves nothing");
    }

    /// <remarks>
    /// The unload request runs an <c>Unloading</c> handler, which is code a package registered and may throw
    /// any type it likes. The file boundary's closed allowlist would let one escape; this boundary contains
    /// everything short of a process-fatal condition and leaves a terminal state that reports why.
    /// </remarks>
    [Test]
    public void AnUnloadingHandlerThrowingAnUnfamiliarExceptionLeavesATerminalReportedState()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var store = Admit(publisher);

        var context = AssemblyLoadContext.GetLoadContext(store.Admitted.Single().Assembly)!;
        context.Unloading += _ => throw new HostileUnloadException();

        using var assertions = new AssertionScope();

        store.TryRequestUnload(out var refusal).Should().BeFalse();
        refusal.Should().Contain("could not be released").And.Contain("deliberately hostile");
        store.State.Should().Be(SharedContractState.Failed);
        store.UnloadRequested.Should().BeFalse("nothing was released, so nothing claims it was");

        store.TryRequestUnload(out var again).Should().BeFalse("the failure is terminal, not retried");
        again.Should().Be(refusal);
    }

    [Test]
    public void AnInstallationThatSharesNothingAdmitsNothingAndStillHasItsAuthority()
    {
        var package = Package("plain", Path.Combine(_root, "plain"));
        Directory.CreateDirectory(package.Folder);

        var store = Admit(package);

        using var assertions = new AssertionScope();
        store.Admitted.Should().BeEmpty();
        store.AdmittedCount.Should().Be(0);
        store.State.Should().Be(SharedContractState.Active);
        store.OpenScope(package).VisibleNames.Should().BeEmpty();
    }

    [Test]
    public void AStoreAdmitsOneGraphAndRefusesToBorrowItsAnswerForAnother()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var store = Admit(publisher);
        var graph = Graph(publisher);

        var again = () => store.Admit(graph);

        again.Should().Throw<InvalidOperationException>()
            .WithMessage("*different resolved graph*",
                "matching identifiers, versions and file names do not prove matching bytes, identities or "
                + "dependency closures");
    }

    [Test]
    public void AScopeBelongsToTheExactPackageTheInstallationAdmitted()
    {
        var publisher = Publisher("publisher", SharedName, One);
        var store = Admit(publisher);

        var clone = Package("publisher", publisher.Folder, contracts: [SharedName + ".dll"]);
        var open = () => store.OpenScope(clone);

        open.Should().Throw<InvalidOperationException>()
            .WithMessage("*not the exact installed package*",
                "two installation attempts of one identifier are what exact-receipt withdrawal exists to "
                + "tell apart");
    }

    /// <param name="failure">A failure the platform must never absorb.</param>
    [TestCaseSource(nameof(ProcessFatalFailures))]
    public void AProcessFatalFailureIsNeverContainedAtEitherBoundary(Exception failure)
    {
        using var assertions = new AssertionScope();
        LoadFailurePolicy.IsProcessFatal(failure).Should().BeTrue();
        LoadFailurePolicy.IsContainableContractFailure(failure).Should().BeFalse();
        LoadFailurePolicy.IsContainablePackageFailure(failure).Should().BeFalse();
    }

    [Test]
    public void TheFileBoundaryContainsOnlyTheFailuresItCanProduce()
    {
        using var assertions = new AssertionScope();

        LoadFailurePolicy.IsContainableContractFailure(new BadImageFormatException()).Should().BeTrue();
        LoadFailurePolicy.IsContainableContractFailure(new FileLoadException()).Should().BeTrue();
        LoadFailurePolicy.IsContainableContractFailure(new InvalidOperationException()).Should().BeTrue();

        LoadFailurePolicy.IsContainableContractFailure(new HostileUnloadException()).Should().BeFalse(
            "a failure type this boundary cannot produce is not one the platform knows how to contain");

        LoadFailurePolicy.IsContainablePackageFailure(new HostileUnloadException()).Should().BeTrue(
            "a package's own code may throw any type, and refusing to contain an unfamiliar one would let a "
            + "novel extension bug stop the whole installation");
    }

    /// <remarks>
    /// A wrapped process-fatal condition is still process-fatal. A type initializer that ran out of memory
    /// surfaces as a type-initialization failure, and a filter reading only the outer type would absorb it.
    /// </remarks>
    [Test]
    public void AWrappedProcessFatalFailureIsNotContainedByItsOuterType()
    {
        var wrapped = new TypeInitializationException("Contoso.Type", new OutOfMemoryException());

        using var assertions = new AssertionScope();
        LoadFailurePolicy.IsContainableContractFailure(new TypeInitializationException("Contoso.Type", null))
            .Should().BeTrue();
        LoadFailurePolicy.IsContainableContractFailure(wrapped).Should().BeFalse();
        LoadFailurePolicy.IsContainablePackageFailure(wrapped).Should().BeFalse();
    }

    private static IEnumerable<Exception> ProcessFatalFailures()
    {
        yield return new OperationCanceledException();
        yield return new OutOfMemoryException();
        yield return new InsufficientMemoryException();
        yield return new InsufficientExecutionStackException();
        yield return new AccessViolationException();
        yield return new SEHException();
        yield return new AggregateException(new InvalidOperationException(), new OutOfMemoryException());
    }

    private static Assembly LoadIsolated(string path)
    {
        var context = new AssemblyLoadContext($"reference-source:{Guid.NewGuid():N}", isCollectible: true);
        using var stream = File.OpenRead(path);
        return context.LoadFromStream(stream);
    }

    /// <summary>Builds a package that publishes one emitted contract.</summary>
    private InstalledPackage Publisher(string id, string assemblyName, Version version)
    {
        var folder = Path.Combine(_root, id);
        EmittedContract.Write(folder, assemblyName, version);
        return Package(id, folder, contracts: [assemblyName + ".dll"]);
    }

    private static InstalledPackage Package(
        string id,
        string folder,
        string? entry = null,
        IReadOnlyList<string>? contracts = null,
        IReadOnlyList<PackageRequirement>? requirements = null)
        => new(
            PluginId.FromString(id),
            SemanticVersion.Parse("1.0.0"),
            Path.Combine(folder, "plugin.json"),
            folder,
            entry,
            contracts,
            requirements);

    private static ResolvedPackageGraph Graph(params InstalledPackage[] packages)
        => new PackageDependencyResolver().Resolve(packages);

    private SharedContractStore Store()
    {
        var store = new SharedContractStore();
        _stores.Add(store);
        return store;
    }

    /// <summary>Leaves a folder traversable by name but impossible to enumerate.</summary>
    private static void Unlistable(string folder)
        => File.SetUnixFileMode(folder, UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    /// <summary>Restores a folder so the fixture's own cleanup can delete it.</summary>
    private static void Relistable(string folder)
        => File.SetUnixFileMode(
            folder,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    private SharedContractStore Admit(params InstalledPackage[] packages)
    {
        var store = Store();
        store.Admit(Graph(packages));
        return store;
    }

    private SharedContractRefusal RefusalOf(InstalledPackage package)
        => Store().Admit(Graph(package)).Refusals[package.Id];

    /// <summary>An exception type the platform has never seen, thrown from a package-registered handler.</summary>
    private sealed class HostileUnloadException()
        : Exception("This unloading handler is deliberately hostile.");
}
