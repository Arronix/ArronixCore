using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Tests.Support;

#pragma warning disable ARX0014 // The extension model is experimental; these tests exercise it.
#pragma warning disable ARX0019 // Definition contracts are experimental; these tests build one.
#pragma warning disable ARX0020 // The typed media surface is experimental; these tests register one.

namespace Arronix.Plugins.Tests.Registration;

/// <summary>
/// The typed registration path: one pair of types in, the same bidirectional rule as ever.
/// </summary>
/// <remarks>
/// A media kind is one ledger entry but many contributions in substance. Its sections stand in for the
/// per-seam registrations, so they demand the same capabilities those registrations would — and satisfy
/// them, so the forward check still quarantines a declaration nothing accounts for. These tests pin both
/// halves, section by section, exactly as they did when the sections arrived as a hand-written aggregate.
/// </remarks>
[TestFixture]
public sealed class MediaTypeRegistrationTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.definition");

    private static readonly Capability[] RequiredGrant =
    [
        Capability.MediaKind,
        Capability.Parsing,
        Capability.Matching,
        Capability.Indexing,
    ];

    private static (PluginRegistry Registry, PluginRegistrationLedger Ledger) Create(params Capability[] granted)
        => Create(MediaKindModels.RequiredSectionsOnly(), granted);

    private static (PluginRegistry Registry, PluginRegistrationLedger Ledger) Create(
        MediaKindModel model,
        params Capability[] granted)
    {
        var ledger = new PluginRegistrationLedger(Plugin);
        var registry = new PluginRegistry(
            Plugin,
            CapabilitySet.Of(granted),
            ledger,
            new StubCapabilityReader(model));

        return (registry, ledger);
    }

    /// <summary>
    /// A registry with no way to price a typed kind refuses it rather than pricing it at the gate alone.
    /// </summary>
    /// <remarks>
    /// The replacement for "a null definition is refused". A typed registration cannot be null — it is two
    /// type arguments — so the argument that can be missing moved to the host's side, and this is that
    /// argument going missing.
    /// </remarks>
    [Test]
    public void ATypedKindIsRefusedByARegistryThatCannotPriceIt()
    {
        var ledger = new PluginRegistrationLedger(Plugin);
        var registry = new PluginRegistry(Plugin, CapabilitySet.Of(RequiredGrant), ledger);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot tell which capabilities*");
        ledger.Count.Should().Be(0);
    }

    [Test]
    public void NothingIsAcceptedAfterConfigurationReturns()
    {
        var (registry, _) = Create(RequiredGrant);
        registry.Seal();

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*after its configuration returned*");
    }

    [Test]
    public void CapturingADefinitionRequiresTheMediaKindCapability()
    {
        var (registry, ledger) = Create(Capability.Parsing, Capability.Matching, Capability.Indexing);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        var failure = register.Should().Throw<PluginCapabilityException>().Which;
        failure.ErrorCode.Should().Be(CoreErrorCode.PluginCapabilityMissing);
        failure.Required.Should().Be(Capability.MediaKind);
        failure.ContractName.Should().Be("MediaKindModel");

        ledger.Count.Should().Be(0, "a refused registration is refused, not recorded and removed later");
    }

    [TestCase(Capability.Parsing, "MediaKindModel.Parsing")]
    [TestCase(Capability.Matching, "MediaKindModel.Matching")]
    [TestCase(Capability.Indexing, "MediaKindModel.Querying")]
    public void EveryRequiredSectionDemandsItsSeamCapability(Capability missing, string section)
    {
        var (registry, ledger) = Create([.. RequiredGrant.Where(capability => capability != missing)]);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        var failure = register.Should().Throw<PluginCapabilityException>().Which;
        failure.Required.Should().Be(missing);
        failure.ContractName.Should().Be(
            section,
            "the refusal names the section that carried the demand, so the author knows which part of the declaration needs the manifest changed");

        ledger.Count.Should().Be(0);
    }

    [Test]
    public void AKindWithOnlyRequiredSectionsIsAdmittedUnderExactlyTheirCapabilities()
    {
        var (registry, ledger) = Create(RequiredGrant);

        registry.AddMediaType<ExampleItem, ExampleKind>().Should().BeSameAs(registry);

        ledger.Count.Should().Be(1);
        ledger.Single<IMediaTypeRegistration>().Should().NotBeNull();

        ledger.TryVerifyDeclaredCapabilities(CapabilitySet.Of(RequiredGrant), out var unsatisfied)
            .Should().BeTrue("the kind's sections account for every capability it demands");
        unsatisfied.Should().BeEmpty();
    }

    [Test]
    public void ADefaultedSectionLeftAtItsDefaultDemandsNothingAndSatisfiesNothing()
    {
        var (registry, ledger) = Create([.. RequiredGrant, Capability.Quality]);

        registry.AddMediaType<ExampleItem, ExampleKind>();

        ledger.SatisfiedCapabilities.Has(Capability.Quality).Should().BeFalse(
            "a ladder-derived quality default is host behavior, not a contribution");

        ledger.TryVerifyDeclaredCapabilities(
                CapabilitySet.Of([.. RequiredGrant, Capability.Quality]),
                out var unsatisfied)
            .Should().BeFalse("declaring quality while contributing none of it is a fiction the forward check catches");
        unsatisfied.Should().Equal(Capability.Quality);
    }

    [Test]
    public void ACatalogSectionDemandsTheMetadataCapability()
    {
        var (registry, _) = Create(MediaKindModels.WithCatalog(), RequiredGrant);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        var failure = register.Should().Throw<PluginCapabilityException>().Which;
        failure.Required.Should().Be(Capability.Metadata);
        failure.ContractName.Should().Be("MediaKindModel.Catalog");
    }

    [Test]
    public void ANonDefaultQualitySectionDemandsTheQualityCapability()
    {
        var (registry, _) = Create(MediaKindModels.WithQuality(), RequiredGrant);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        register.Should().Throw<PluginCapabilityException>()
            .Which.Required.Should().Be(Capability.Quality);
    }

    [Test]
    public void ANonDefaultNamingSectionDemandsTheRenamingCapability()
    {
        var (registry, _) = Create(MediaKindModels.WithNaming(), RequiredGrant);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        register.Should().Throw<PluginCapabilityException>()
            .Which.Required.Should().Be(Capability.Renaming);
    }

    [Test]
    public void ANonDefaultNotificationSectionDemandsTheNotificationCapability()
    {
        var (registry, _) = Create(MediaKindModels.WithNotifications(), RequiredGrant);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        register.Should().Throw<PluginCapabilityException>()
            .Which.Required.Should().Be(Capability.Notification);
    }

    [Test]
    public void EverySectionSatisfiesTheCapabilityItDemanded()
    {
        var granted = new[]
        {
            Capability.MediaKind,
            Capability.Parsing,
            Capability.Matching,
            Capability.Indexing,
            Capability.Quality,
            Capability.Renaming,
            Capability.Metadata,
            Capability.Notification,
        };
        var everySection = MediaKindModels.WithCatalog() with
        {
            Quality = MediaKindModels.WithQuality().Quality,
            Naming = MediaKindModels.WithNaming().Naming,
            Notifications = MediaKindModels.WithNotifications().Notifications,
        };

        var (registry, ledger) = Create(everySection, granted);

        registry.AddMediaType<ExampleItem, ExampleKind>();

        ledger.TryVerifyDeclaredCapabilities(CapabilitySet.Of(granted), out var unsatisfied).Should().BeTrue();
        unsatisfied.Should().BeEmpty();
    }

    [Test]
    public void ARefusalChecksEveryDemandBeforeRecordingAnything()
    {
        // Metadata is the last demand checked; the earlier ones all pass. If anything had been recorded
        // before the failing check, the ledger would hold a contribution from a refused configuration.
        var (registry, ledger) = Create(MediaKindModels.WithCatalog(), RequiredGrant);

        var register = () => registry.AddMediaType<ExampleItem, ExampleKind>();

        register.Should().Throw<PluginCapabilityException>();
        ledger.Count.Should().Be(0);
        ledger.SatisfiedCapabilities.Should().Be(CapabilitySet.None);
    }

    [Test]
    public void TheSectionRequirementTableMatchesTheDesignsCapabilityMapping()
    {
        var requirements = DefinitionCapabilityRules.Requirements(MediaKindModels.RequiredSectionsOnly());

        requirements.Select(requirement => requirement.Capability).Should().Equal(
            Capability.MediaKind,
            Capability.Parsing,
            Capability.Matching,
            Capability.Indexing);
    }

    [Test]
    public void TheMediaKindRowIsGatedInTheMatrixLikeAnyOtherRegistration()
    {
        CapabilityMatrix.RegistrationRequirements.Should().ContainKey(typeof(IMediaTypeRegistration));
        CapabilityMatrix.IsPermitted(CapabilitySet.Of(Capability.MediaKind), typeof(IMediaTypeRegistration))
            .Should().BeTrue();
        CapabilityMatrix.IsPermitted(CapabilitySet.None, typeof(IMediaTypeRegistration)).Should().BeFalse();
    }
}
