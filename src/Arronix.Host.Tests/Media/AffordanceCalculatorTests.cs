using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Host.Media;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Media;

/// <summary>
/// Affordances derived from the shape, never declared.
/// </summary>
/// <remarks>
/// A declaration that can be derived is a declaration that can disagree, so what is asserted here is that
/// each affordance follows from a fact the shape already states — and that the two which depend on the
/// deployment rather than the shape follow from the deployment.
/// </remarks>
[TestFixture]
internal sealed class AffordanceCalculatorTests
{
    private static ValidatedShape Layered()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out _).Should().BeTrue();
        return shape!;
    }

    private static IReadOnlyList<Affordance> For(
        ValidatedShape shape,
        Abstractions.Shape.MediaLevelId level,
        CapabilitySet? capabilities = null,
        bool rootFolder = true,
        bool releaseSource = true)
        => AffordanceCalculator.ForLevel(
            shape,
            shape.LevelOf(level),
            capabilities ?? CapabilitySet.Of(
                Capability.MediaKind,
                Capability.Indexing,
                Capability.Metadata,
                Capability.Renaming).WithImplied(),
            rootFolder,
            releaseSource);

    [Test]
    public void ALevelWithAMonitoringAxisIsMonitorable()
        => For(Layered(), ShapeFixtures.Work).Should().Contain(Affordance.Monitorable);

    [Test]
    public void ALevelWithNoMonitoringAxisIsNot()
        => For(Layered(), ShapeFixtures.Part).Should().NotContain(Affordance.Monitorable);

    [Test]
    public void ALevelThatIsASearchTargetIsSearchable()
        => For(Layered(), ShapeFixtures.Work).Should().Contain(Affordance.Searchable);

    [Test]
    public void WithoutTheIndexingPrivilegeNothingIsSearchableOrDownloadable()
    {
        var affordances = For(Layered(), ShapeFixtures.Work, CapabilitySet.Of(Capability.MediaKind));

        affordances.Should().NotContain(Affordance.Searchable);
        affordances.Should().NotContain(Affordance.Downloadable);
    }

    [Test]
    public void NothingIsDownloadableUntilAReleaseSourceIsConfigured()
        => For(Layered(), ShapeFixtures.Work, releaseSource: false)
            .Should().NotContain(Affordance.Downloadable);

    [Test]
    public void NothingIsRelocatableUntilARootFolderIsConfigured()
        => For(Layered(), ShapeFixtures.Catalog, rootFolder: false)
            .Should().NotContain(Affordance.Relocatable);

    [Test]
    public void OnlyWhatAUserAddsIsTaggable()
    {
        For(Layered(), ShapeFixtures.Catalog).Should().Contain(Affordance.Taggable);
        For(Layered(), ShapeFixtures.Part).Should().NotContain(Affordance.Taggable);
    }

    [Test]
    public void ALevelWithAChildIsBrowsableAndALeafIsNot()
    {
        For(Layered(), ShapeFixtures.Catalog).Should().Contain(Affordance.Browsable);
        For(Layered(), ShapeFixtures.Part).Should().NotContain(Affordance.Browsable);
    }

    [Test]
    public void TheChoiceAmongManifestationsIsOfferedOnTheLevelAboveThem()
    {
        // Attributing it to the variant level itself would put the control on each of the things being
        // chosen between, and the chosen one is recorded on the parent.
        For(Layered(), ShapeFixtures.Work).Should().Contain(Affordance.Selectable);
        For(Layered(), ShapeFixtures.Variant).Should().NotContain(Affordance.Selectable);
    }

    [Test]
    public void OnlyALevelWithACatalogRecordAndAnIdentifierIsRefreshable()
    {
        var shape = Layered();

        For(shape, ShapeFixtures.Catalog).Should().NotContain(Affordance.Refreshable);

        ValidatedShape.TryValidate(
            ShapeFixtures.Fused(),
            out var fused,
            out _).Should().BeTrue();

        For(fused!, ShapeFixtures.Catalog).Should().Contain(Affordance.Refreshable);
    }

    [Test]
    public void OnlyAFileBearingLevelIsRenamable()
    {
        For(Layered(), ShapeFixtures.Work).Should().Contain(Affordance.Renamable);
        For(Layered(), ShapeFixtures.Variant).Should().NotContain(Affordance.Renamable);
    }

    [Test]
    public void OnlyALevelWithLibraryStateIsRemovable()
    {
        For(Layered(), ShapeFixtures.Work).Should().Contain(Affordance.Removable);
        For(Layered(), ShapeFixtures.Variant).Should().NotContain(Affordance.Removable);
    }

    [Test]
    public void TheResultIsStableAcrossCalls()
    {
        var shape = Layered();

        For(shape, ShapeFixtures.Work).Should().Equal(For(shape, ShapeFixtures.Work));
    }
}
