using Arronix.Abstractions.Errors;
using Arronix.Host.Storage;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Storage;

/// <summary>
/// The at-most-one-chosen-manifestation rule, enforced once for every media kind.
/// </summary>
/// <remarks>
/// Two of the surveyed applications hand-roll this check inside a repository, each for its own entity. Here
/// the shape declares that a level is a variant axis and the store enforces the consequence, so a fifth
/// media kind gets the invariant without writing it.
/// </remarks>
[TestFixture]
internal sealed class VariantSelectionInvariantTests
{
    private static IMediaStore Layered()
        => new InMemoryMediaStore(
            TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Layered())));

    [Test]
    public async Task AChosenManifestationIsStoredOnTheLevelAboveIt()
    {
        var store = Layered();
        var work = ShapeFixtures.Item(ShapeFixtures.Work, 1);
        var chosen = ShapeFixtures.Item(ShapeFixtures.Variant, 10);

        await store.UpsertLibraryAsync(new LibraryFacet { Ref = work, SelectedVariant = chosen });

        (await store.FindLibraryAsync(work))!.SelectedVariant.Should().Be(chosen);
    }

    [Test]
    public async Task AChosenManifestationAtTheWrongLevelIsRefused()
    {
        var store = Layered();

        var act = async () => await store.UpsertLibraryAsync(new LibraryFacet
        {
            Ref = ShapeFixtures.Item(ShapeFixtures.Work, 1),
            SelectedVariant = ShapeFixtures.Item(ShapeFixtures.Part, 10),
        });

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("is at level");
    }

    [Test]
    public async Task RecordingAChoiceOnTheWrongParentLevelIsRefused()
    {
        var store = Layered();

        var act = async () => await store.UpsertLibraryAsync(new LibraryFacet
        {
            Ref = ShapeFixtures.Item(ShapeFixtures.Catalog, 1),
            SelectedVariant = ShapeFixtures.Item(ShapeFixtures.Variant, 10),
        });

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("recorded on level");
    }

    [Test]
    public async Task OneManifestationCannotBeChosenByTwoParents()
    {
        var store = Layered();
        var chosen = ShapeFixtures.Item(ShapeFixtures.Variant, 10);

        await store.UpsertLibraryAsync(new LibraryFacet
        {
            Ref = ShapeFixtures.Item(ShapeFixtures.Work, 1),
            SelectedVariant = chosen,
        });

        var act = async () => await store.UpsertLibraryAsync(new LibraryFacet
        {
            Ref = ShapeFixtures.Item(ShapeFixtures.Work, 2),
            SelectedVariant = chosen,
        });

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("already the chosen");
    }

    [Test]
    public async Task AShapeWithNoVariantLevelRefusesAChoiceAltogether()
    {
        var store = new InMemoryMediaStore(
            TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Fused())));

        var act = async () => await store.UpsertLibraryAsync(new LibraryFacet
        {
            Ref = ShapeFixtures.Item(ShapeFixtures.Catalog, 1),
            SelectedVariant = ShapeFixtures.Item(ShapeFixtures.Catalog, 2),
        });

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("no variant level");
    }

    [Test]
    public async Task AMonitoringAnswerOnAnUndeclaredAxisIsRefused()
    {
        var store = Layered();

        var act = async () => await store.UpsertLibraryAsync(new LibraryFacet
        {
            Ref = ShapeFixtures.Item(ShapeFixtures.Work, 1),
            Monitor = new Dictionary<string, string>(StringComparer.Ordinal) { ["invented"] = "yes" },
        });

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("declares no monitoring axis");
    }
}
