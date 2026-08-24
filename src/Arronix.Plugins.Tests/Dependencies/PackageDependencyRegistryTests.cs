using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Versioning;
using FluentAssertions.Execution;


namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// Pins, edges, and two-phase withdrawal, all by exact reference.
/// </summary>
/// <remarks>
/// Every case here is written so it can fail. The recurring shape is a second object that shares an
/// identifier with a first: an identifier is what two different installation attempts have in common, so
/// any rule expressed in identifiers is a rule that cannot tell them apart.
/// </remarks>
[TestFixture]
public sealed class PackageDependencyRegistryTests
{
    private static readonly VersionRange Any = VersionRangeParser.Parse(">=1.0");

    private PluginPublicationGate _publication = new();
    private PackageDependencyRegistry _registry = null!;
    private SharedContractStore? _contracts;
    private ResolvedPackageGraph? _graph;

    [SetUp]
    public void SetUp()
    {
        _publication = new PluginPublicationGate();
        _registry = new PackageDependencyRegistry(_publication);
        _contracts = null;
        _graph = null;
    }

    /// <remarks>
    /// Package lifetime and executable lifetime are separate values. A contract-only package has a receipt
    /// and a contract hold and no executable admission at all, and nothing is invented for it.
    /// </remarks>
    [Test]
    public void APackageWithNoExecutableAdmissionStillHasAnExactReceipt()
    {
        var core = Receipt("core");
        var lease = Lease(core);

        Publish(core).Should().BeSameAs(core);

        using var assertions = new AssertionScope();
        core.HasExecutableAdmission.Should().BeFalse(
            "a package may share contracts and contribute nothing executable");
        lease.Runtime.Should().BeNull("no executable half means no runtime lease");
        lease.Contracts.IsHeld.Should().BeTrue();
        _registry.RootedPackages.Should().Equal(PluginId.FromString("core"));
    }

    [Test]
    public void EachHalfOfAPackageLifetimeIsSingleAssignment()
    {
        var receipt = Receipt("core");
        receipt.AttachHostAdmission(new StubAttempt(PluginId.FromString("core")));

        var host = () => receipt.AttachHostAdmission(new StubAttempt(PluginId.FromString("core")));

        host.Should().Throw<InvalidOperationException>();
    }

    /// <remarks>
    /// Release is by reference and idempotent, so a repeated withdrawal pass cannot release a hold a second
    /// installation attempt of the same identifier is relying on.
    /// </remarks>
    [Test]
    public async Task AContractHoldIsReleasedExactlyOnceHoweverOftenWithdrawalIsRetried()
    {
        var core = Receipt("core");
        var lease = Lease(core);
        var store = _contracts!;

        (await lease.DisposeAsync()).Should().BeEmpty();
        (await lease.DisposeAsync()).Should().BeEmpty();

        lease.Contracts.IsHeld.Should().BeFalse();
        store.Holders.Should().BeEmpty();
    }

    [Test]
    public void PublicationRequiresThePreparationPinsItConverts()
    {
        var core = Receipt("core");

        _registry.TryPublish(core, out var defects).Should().BeFalse();
        defects.Should().ContainSingle().Which.Should().Contain("without holding preparation pins");
    }

    [Test]
    public void APinnedDependencyCannotBeWithdrawnWhileItsDependantIsStillBeingPrepared()
    {
        var core = Publish(Receipt("core"));
        var app = Receipt("app", Edge("app", core));

        _registry.TryPinDependencies(app, out _).Should().BeTrue();

        using var assertions = new AssertionScope();
        _registry.HasLiveDependants(core, out var pinning).Should().BeTrue(
            "a package that has resolved contracts and may be executing registration code against this "
            + "one's types is as live a dependant as one already serving");
        pinning.Should().Equal(PluginId.FromString("app"));
        _registry.BeginWithdrawal(core, out var blocked).Should().BeFalse();
        blocked.Should().Equal(PluginId.FromString("app"));
        _registry.PreparingPackages.Should().Equal(PluginId.FromString("app"));
    }

    [Test]
    public void PinningRefusesADependencyThatIsGoneOrWithdrawingOrADifferentAttempt()
    {
        var core = Publish(Receipt("core"));
        var replacement = Receipt("core");

        var stale = Receipt("stale", Edge("stale", replacement));
        _registry.TryPinDependencies(stale, out var staleDefects).Should().BeFalse();
        staleDefects.Should().ContainSingle().Which.Should().Contain("different installation attempt");

        _registry.BeginWithdrawal(core, out _).Should().BeTrue();
        var late = Receipt("late", Edge("late", core));
        _registry.TryPinDependencies(late, out var lateDefects).Should().BeFalse();
        lateDefects.Should().ContainSingle().Which.Should().Contain("being withdrawn");

        _registry.CompleteWithdrawal(core);
        var gone = Receipt("gone", Edge("gone", core));
        _registry.TryPinDependencies(gone, out var goneDefects).Should().BeFalse();
        goneDefects.Should().ContainSingle().Which.Should().Contain("no longer admitted");
    }

    [Test]
    public void CommitConvertsPinsIntoEdgesInOneStep()
    {
        var core = Publish(Receipt("core"));
        var app = Publish(Receipt("app", Edge("app", core)));

        using var assertions = new AssertionScope();
        _registry.PreparingPackages.Should().BeEmpty("the pins became edges rather than being released");
        _registry.DependantsOf(PluginId.FromString("core")).Should().Equal(PluginId.FromString("app"));
        _registry.Snapshot().Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Dependant = PluginId.FromString("app"),
            Dependency = PluginId.FromString("core"),
            DeclaredRange = ">=1.0",
            ResolvedVersion = SemanticVersion.Parse("1.0.0"),
        });
        app.Edges.Should().ContainSingle().Which.DependencyReceipt.Should().BeSameAs(core);
    }

    [Test]
    public void HidingADependantKeepsEveryDependencyItHoldsUntilItsCodeIsGone()
    {
        var core = Publish(Receipt("core"));
        var app = Publish(Receipt("app", Edge("app", core)));

        _registry.BeginWithdrawal(app, out _).Should().BeTrue();

        using (new AssertionScope())
        {
            _registry.BeginWithdrawal(core, out var blocked).Should().BeFalse(
                "the dependant's own disposers have not run yet and they execute against this package's types");
            blocked.Should().Equal(PluginId.FromString("app"));
            _registry.DependantsOf(PluginId.FromString("core")).Should().Equal(PluginId.FromString("app"));
        }

        _registry.CompleteWithdrawal(app);

        using var assertions = new AssertionScope();
        _registry.DependantsOf(PluginId.FromString("core")).Should().BeEmpty();
        _registry.BeginWithdrawal(core, out _).Should().BeTrue();
    }

    [Test]
    public void AHiddenPackageStillOccupiesItsIdentifierSoAReplacementCannotTakeIt()
    {
        var core = Publish(Receipt("core"));
        _registry.BeginWithdrawal(core, out _).Should().BeTrue();

        var replacement = Receipt("core");
        _registry.TryPinDependencies(replacement, out _).Should().BeTrue();
        _registry.TryPublish(replacement, out var defects).Should().BeFalse();

        defects.Should().ContainSingle().Which.Should().Contain("still withdrawing");
    }

    [Test]
    public void WithdrawalNeverRemovesWhatMerelySharesAnIdentifier()
    {
        var core = Publish(Receipt("core"));
        Publish(Receipt("app", Edge("app", core)));

        // A second attempt on the same identifier. Every rule here is by reference, so this object can
        // neither hide nor finalize the attempt that is actually rooted.
        var impostor = Receipt("core");
        _registry.BeginWithdrawal(impostor, out _).Should().BeTrue("an unrooted receipt withdraws nothing");
        _registry.CompleteWithdrawal(impostor);

        using var assertions = new AssertionScope();
        _registry.RootedPackages.Should().Equal(PluginId.FromString("core"), PluginId.FromString("app"));
        _registry.DependantsOf(PluginId.FromString("core")).Should().Equal(PluginId.FromString("app"));
        _registry.BeginWithdrawal(core, out var blocked).Should().BeFalse();
        blocked.Should().Equal(PluginId.FromString("app"));
    }

    [Test]
    public void ADependantHeldOpenKeepsEveryPackageItCanReachRooted()
    {
        var core = Publish(Receipt("core"));
        var middle = Publish(Receipt("middle", Edge("middle", core)));
        var leaf = Publish(Receipt("leaf", Edge("leaf", middle)));

        // 'leaf' is never withdrawn, exactly as an overrunning job or a failed unload leaves it.
        using var assertions = new AssertionScope();
        _registry.BeginWithdrawal(middle, out var middleBlocked).Should().BeFalse();
        middleBlocked.Should().Equal(PluginId.FromString("leaf"));
        _registry.BeginWithdrawal(core, out var coreBlocked).Should().BeFalse(
            "the transitive closure of a held-open dependant stays rooted, one edge at a time");
        coreBlocked.Should().Equal(PluginId.FromString("middle"));
        leaf.Edges.Should().ContainSingle().Which.DependencyReceipt.Should().BeSameAs(middle);
    }

    [Test]
    public void PublicationOrderIsWhatWithdrawalReverses()
    {
        var core = Publish(Receipt("core"));
        var app = Publish(Receipt("app", Edge("app", core)));

        using var assertions = new AssertionScope();
        _registry.PublicationOrderOf(core).Should().BeLessThan(_registry.PublicationOrderOf(app)!.Value);
        _registry.PublicationOrderOf(Receipt("core")).Should().BeNull(
            "an attempt that is not the rooted one has no place in the order");
        _registry.RootedPackages.Should().Equal(PluginId.FromString("core"), PluginId.FromString("app"));
    }

    /// <remarks>
    /// The edges a receipt publishes are the dependant's own liability, so a caller that could cast the
    /// published collection back to the array behind it could give a package edges it never declared.
    /// </remarks>
    [Test]
    public void APublishedReceiptsEdgesCannotBeCastBackAndEdited()
    {
        var core = Publish(Receipt("core"));
        var app = Receipt("app", Edge("app", core));

        using var assertions = new AssertionScope();
        ((object)app.Edges).Should().NotBeAssignableTo<PackageDependencyEdge[]>();

        var edit = () => ((IList<PackageDependencyEdge>)app.Edges).Clear();
        edit.Should().Throw<NotSupportedException>();
    }

    /// <remarks>
    /// The edges must be a one-for-one, ordered binding of the package's own declared requirements. A
    /// receipt built from a structurally equal requirement would describe a dependency this package did not
    /// declare.
    /// </remarks>
    [Test]
    public void AReceiptCarriesExactlyTheEdgesItsDeclarationStates()
    {
        var core = Publish(Receipt("core"));
        var package = new InstalledPackage(
            PluginId.FromString("app"),
            SemanticVersion.Parse("1.0.0"),
            "/extensions/app/plugin.json",
            "/extensions/app",
            requirements: [new PackageRequirement(core.Id, Any)]);

        var missing = () => new PackageAdmissionReceipt(package, []);
        var cloned = () => new PackageAdmissionReceipt(
            package,
            [new PackageDependencyEdge(package.Id, new PackageRequirement(core.Id, Any), core)]);

        using var assertions = new AssertionScope();
        missing.Should().Throw<ArgumentException>().WithMessage("*exactly the edges*");
        cloned.Should().Throw<ArgumentException>().WithMessage("*at that position*");
    }

    /// <summary>Opens the package-level lease that owns a receipt and its contract hold.</summary>
    private PackageAdmissionLease Lease(PackageAdmissionReceipt receipt)
    {
        _contracts ??= new SharedContractStore();
        _contracts.Admit(_graph ??= new PackageDependencyResolver().Resolve([receipt.Package]));
        return new PackageAdmissionLease(receipt, _contracts.OpenScope(receipt.Package));
    }

    private PackageAdmissionReceipt Publish(PackageAdmissionReceipt receipt)
    {
        _registry.TryPinDependencies(receipt, out var pinDefects).Should().BeTrue(
            "pins are taken before anything is read or run: {0}", string.Join("; ", pinDefects));
        _registry.TryPublish(receipt, out var defects).Should().BeTrue(string.Join("; ", defects));
        return receipt;
    }

    private static PackageAdmissionReceipt Receipt(string id, params PackageDependencyEdge[] edges)
        => new(
            new InstalledPackage(
                PluginId.FromString(id),
                SemanticVersion.Parse("1.0.0"),
                $"/extensions/{id}/plugin.json",
                $"/extensions/{id}",
                requirements: [.. edges.Select(static edge => edge.Requirement)]),
            edges);

    private static PackageDependencyEdge Edge(string dependant, PackageAdmissionReceipt dependency)
        => new(
            PluginId.FromString(dependant),
            new PackageRequirement(dependency.Id, Any),
            dependency);

    private sealed class StubAttempt(PluginId plugin) : IPluginAdmissionAttempt
    {
        public PluginId Plugin { get; } = plugin;

        public AdmittedInventory Inventory { get; } = AdmittedInventory.Empty;

        public bool TryCommit(out CoreErrorCode errorCode, out IReadOnlyList<string> defects)
        {
            errorCode = default;
            defects = [];
            return true;
        }

        public void Rollback()
        {
        }
    }
}
