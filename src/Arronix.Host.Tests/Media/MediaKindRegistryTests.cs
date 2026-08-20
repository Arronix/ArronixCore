using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Host.Configuration;
using Arronix.Host.Media;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Media;

/// <summary>
/// Admission: all of a media kind or none of it.
/// </summary>
[TestFixture]
internal sealed class MediaKindRegistryTests
{
    private static MediaKindRegistry Empty(params string[] rootFolders)
    {
        var library = new LibraryOptions();

        foreach (var folder in rootFolders)
        {
            library.RootFolders.Add(folder);
        }

        return new MediaKindRegistry(TestOptions.Of(library));
    }

    [Test]
    public void AdmittingASoundContributionPublishesItsProjectionAndItsBundle()
    {
        var registry = Empty("/library");

        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Layered()), out var registered, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => defect.Message)));

        registered!.Kind.Should().Be(ShapeFixtures.Kind);
        registered.Projection.Name.Should().Be("Fixture");
        registered.Projection.Capabilities.Should().Contain("media-kind");
        registered.Projection.Capabilities.Should().Contain("network", "indexing implies it");
        registered.Descriptor.Levels.Should().HaveCount(4);
        registered.Descriptor.Plugin.Should().Be(ContributionFixtures.Plugin);
    }

    [Test]
    public void AMalformedShapeIsRefusedAndNothingIsPublished()
    {
        var registry = Empty();

        registry.TryRegister(
            ContributionFixtures.For(ShapeFixtures.Fused() with { Levels = [] }),
            out var registered,
            out var defects)
            .Should().BeFalse();

        registered.Should().BeNull();
        defects.Should().NotBeEmpty();
        registry.All.Should().BeEmpty();
    }

    [Test]
    public void TwoExtensionsClaimingOneMediaKindIsRefusedRatherThanResolved()
    {
        var registry = Empty();

        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Fused()), out _, out _).Should().BeTrue();

        registry.TryRegister(
            ContributionFixtures.For(ShapeFixtures.Layered()) with
            {
                Plugin = PluginId.FromString("other"),
            },
            out _,
            out var defects)
            .Should().BeFalse();

        defects.Should().ContainSingle().Which.Code.Should().Be(CoreErrorCode.MediaKindConflict);
    }

    [Test]
    public void AMediaKindThatWasNeverAdmittedIsNotFound()
    {
        var registry = Empty();

        registry.TryGet(MediaKindId.FromString("absent"), out var registered).Should().BeFalse();
        registered.Should().BeNull();

        var act = () => registry.Require(MediaKindId.FromString("absent"));

        act.Should().Throw<ArronixException>()
            .Which.ErrorCode.Should().Be(CoreErrorCode.MediaKindNotFound);
    }

    [Test]
    public void WithdrawingAnExtensionWithdrawsItsMediaKinds()
    {
        var registry = Empty();

        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Fused()), out _, out _).Should().BeTrue();

        registry.RemoveByPlugin(ContributionFixtures.Plugin).Should().Be(1);
        registry.All.Should().BeEmpty();
    }

    [Test]
    public void ADeclaredSurfaceThatDoesNotFitItsShapeIsRefused()
    {
        var registry = Empty();

        var contribution = ContributionFixtures.For(ShapeFixtures.Fused()) with
        {
            Intent = new PluginIntentSurface
            {
                MediaKind = ShapeFixtures.Kind,
                Sorts = [new SortOption("no-such-field", "Nope", SortDirection.Ascending)],
            },
        };

        registry.TryRegister(contribution, out _, out var defects).Should().BeFalse();
        defects.Should().Contain(defect => defect.Message.Contains("not declared by any level", StringComparison.Ordinal));
    }

    [Test]
    public void AnExtensionThatDeclaresNoSurfaceGetsAnEmptyOne()
    {
        var registry = Empty();

        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Fused()), out var registered, out _)
            .Should().BeTrue();

        registered!.Intent.MediaKind.Should().Be(ShapeFixtures.Kind);
        registered.Intent.Actions.Should().BeEmpty();
    }

    [Test]
    public void AnActionWithNoTargetLevelAppearsOnEveryLevel()
    {
        var registry = Empty();

        var contribution = ContributionFixtures.For(ShapeFixtures.Layered()) with
        {
            Intent = new PluginIntentSurface
            {
                MediaKind = ShapeFixtures.Kind,
                Actions =
                [
                    new ActionDescriptor
                    {
                        ActionId = "everywhere",
                        Name = "Everywhere",
                        Scope = ActionScope.Item,
                        Consequence = Consequence.Safe,
                        Confirmation = ConfirmationRequirement.None,
                    },
                    new ActionDescriptor
                    {
                        ActionId = "just-here",
                        Name = "Just here",
                        Scope = ActionScope.Item,
                        TargetLevelId = ShapeFixtures.Part,
                        Consequence = Consequence.Safe,
                        Confirmation = ConfirmationRequirement.None,
                    },
                ],
            },
        };

        registry.TryRegister(contribution, out var registered, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => defect.Message)));

        registered!.Descriptor.Levels
            .Single(level => level.Level == ShapeFixtures.Catalog).Actions
            .Select(action => action.ActionId).Should().Equal("everywhere");

        registered.Descriptor.Levels
            .Single(level => level.Level == ShapeFixtures.Part).Actions
            .Select(action => action.ActionId).Should().BeEquivalentTo("everywhere", "just-here");
    }

    [Test]
    public void RefreshingRebuildsTheBundleWhenTheDeploymentChanges()
    {
        var registry = Empty("/library");

        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Layered()), out var registered, out _)
            .Should().BeTrue();

        registered!.Descriptor.Levels
            .Single(level => level.Level == ShapeFixtures.Work).Affordances
            .Should().NotContain(Affordance.Downloadable);

        registry.Refresh(releaseSourceConfigured: true);

        registry.Require(ShapeFixtures.Kind).Descriptor.Levels
            .Single(level => level.Level == ShapeFixtures.Work).Affordances
            .Should().Contain(Affordance.Downloadable);
    }

    [Test]
    public void EveryDeclaredIdentifierSchemeReachesTheStableProjection()
    {
        var registry = Empty();

        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Fused()), out var registered, out _)
            .Should().BeTrue();

        registered!.Projection.SupportedIdentifiers.Should().Equal("fixture");
        registered.Projection.NamingTokens.Should().Equal("{Title}");
    }
}
