using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Tests.Support;

#pragma warning disable ARX0008 // Outbound-call contracts are experimental; these tests name one as a gated dependency.
#pragma warning disable ARX0014 // The extension model is experimental; these tests exercise it.

namespace Arronix.Plugins.Tests.Registration;

/// <summary>
/// Both halves of the rule, which only mean anything together.
/// </summary>
/// <remarks>
/// Declaring a privilege requires the registration that uses it, and making a gated registration requires
/// the privilege. Without the reverse direction an extension could contribute an indexer while declaring
/// only the privilege to fold diacritics, and its declaration would be a work of fiction. Without the
/// forward direction an extension could hold every privilege on the strength of one registration.
/// </remarks>
[TestFixture]
public sealed class BidirectionalCapabilityTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.bidirectional");

    private static (PluginRegistry Registry, PluginRegistrationLedger Ledger) Create(params Capability[] granted)
    {
        var ledger = new PluginRegistrationLedger(Plugin);
        return (new PluginRegistry(Plugin, CapabilitySet.Of(granted), ledger), ledger);
    }

    [Test]
    public void RegisteringAGatedContractWithoutTheCapabilityIsRefused()
    {
        var (registry, ledger) = Create(Capability.Parsing);

        var register = () => registry.AddIndexer(Declarations.IndexerRegistration("acme"));

        var failure = register.Should().Throw<PluginCapabilityException>().Which;
        failure.ErrorCode.Should().Be(CoreErrorCode.PluginCapabilityMissing);
        failure.Required.Should().Be(Capability.Indexing);
        failure.Plugin.Should().Be(Plugin);
        failure.ContractName.Should().Be("IndexerRegistration");
        failure.Message.Should().Contain(CapabilityNames.Indexing);

        ledger.Count.Should().Be(0, "a refused registration is refused, not recorded and removed later");
    }

    [Test]
    public void RegisteringAGatedContractWithTheCapabilityIsAdmitted()
    {
        var (registry, ledger) = Create(Capability.Indexing);

        registry.AddIndexer(Declarations.IndexerRegistration("acme"));

        ledger.Count.Should().Be(1);
        ledger.SatisfiedCapabilities.Has(Capability.Indexing).Should().BeTrue();
    }

    [Test]
    public void DeclaringACapabilityWithNoMatchingRegistrationIsRefused()
    {
        var (_, ledger) = Create(Capability.Indexing);

        ledger.TryVerifyDeclaredCapabilities(CapabilitySet.Of(Capability.Indexing), out var unsatisfied)
            .Should().BeFalse();

        unsatisfied.Should().Equal(Capability.Indexing);
    }

    [Test]
    public void DeclaringACapabilityAndUsingItPassesTheForwardCheck()
    {
        var (registry, ledger) = Create(Capability.Indexing);
        registry.AddIndexer(Declarations.IndexerRegistration("acme"));

        ledger.TryVerifyDeclaredCapabilities(CapabilitySet.Of(Capability.Indexing), out var unsatisfied)
            .Should().BeTrue();

        unsatisfied.Should().BeEmpty();
    }

    [Test]
    public void EveryUnusedDeclarationIsReportedNotOnlyTheFirst()
    {
        var (registry, ledger) = Create(Capability.Parsing, Capability.Quality, Capability.Renaming);
        registry.AddReleaseParser(new FakeReleaseParser());

        ledger.TryVerifyDeclaredCapabilities(
                CapabilitySet.Of(Capability.Parsing, Capability.Quality, Capability.Renaming),
                out var unsatisfied)
            .Should().BeFalse();

        unsatisfied.Should().BeEquivalentTo([Capability.Quality, Capability.Renaming]);
    }

    [Test]
    public void APrivilegeToUseSomethingIsNotForwardChecked()
    {
        var (registry, ledger) = Create(Capability.Parsing, Capability.Network, Capability.Storage);
        registry.AddReleaseParser(new FakeReleaseParser());

        ledger.TryVerifyDeclaredCapabilities(
                CapabilitySet.Of(Capability.Parsing, Capability.Network, Capability.Storage),
                out var unsatisfied)
            .Should().BeTrue(
                "no registration can ever account for reading files or making outbound calls, so requiring one would make the check a lie");

        unsatisfied.Should().BeEmpty();
    }

    [Test]
    public void AnAnyOfContractAccountsForEveryCapabilityItAccepts()
    {
        var (registry, ledger) = Create(Capability.Parsing, Capability.Renaming);
        registry.AddDiacriticFolding(new FakeDiacriticFolding());

        ledger.TryVerifyDeclaredCapabilities(
                CapabilitySet.Of(Capability.Parsing, Capability.Renaming),
                out _)
            .Should().BeTrue(
                "guessing which of the two the extension meant would report a false unsatisfied capability against an honest declaration");
    }

    [Test]
    public void TheReverseCheckReadsTheGrantedSetSoAnImpliedPrivilegeCounts()
    {
        // Declaring indexing implies the network privilege, which is what an indexer needs to reach a
        // remote. The implication must be visible to the reverse check, not only to the extension.
        var granted = CapabilitySet.Of(Capability.Indexing).WithImplied();

        granted.Has(Capability.Network).Should().BeTrue();
        CapabilityMatrix.IsPermitted(granted, typeof(Abstractions.Http.IHttpGateway)).Should().BeTrue();
    }

    [Test]
    public void AnUngatedRegistrationNeedsNoPrivilegeAtAll()
    {
        var (registry, ledger) = Create();

        registry.AddHealthContributor(new FakeHealthContributor());

        ledger.Count.Should().Be(1, "reporting on yourself needs no privilege");
    }

    [Test]
    public void EveryCapabilityTheVocabularyDefinesIsEitherForwardCheckableOrAUsePrivilege()
    {
        var usePrivileges = new[] { Capability.Network, Capability.Storage };

        foreach (var capability in Enum.GetValues<Capability>())
        {
            var forwardCheckable = CapabilityMatrix.ForwardCheckableCapabilities.Contains(capability);

            (forwardCheckable ^ usePrivileges.Contains(capability)).Should().BeTrue(
                $"'{CapabilityNames.ToWireName(capability)}' must be exactly one of the two");
        }
    }
}
