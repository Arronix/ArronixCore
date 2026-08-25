using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Storage;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Storage;

/// <summary>
/// The join's declared rules, enforced on the write that would break them.
/// </summary>
/// <remarks>
/// These are the rules the eventual relational schema will express as uniqueness constraints. Asserting them
/// against the in-memory store now is what stops the first four media kinds being written against rules that
/// do not yet exist.
/// </remarks>
[TestFixture]
internal sealed class UnitFileLinkInvariantTests
{
    private static (IMediaStore Store, MediaKindRegistry Kinds, FakeItemSource Items) Fused()
    {
        var items = new FakeItemSource(ShapeFixtures.Kind);
        var kinds = TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Fused(), items));
        return (TestOptions.StoreWith(kinds), kinds, items);
    }

    private static (IMediaStore Store, FakeItemSource Items) Layered()
    {
        var items = new FakeItemSource(ShapeFixtures.Kind);
        var kinds = TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Layered(), items));
        return (TestOptions.StoreWith(kinds), items);
    }

    [Test]
    public async Task ASecondFileIsRefusedForAUnitDeclaredToHaveAtMostOne()
    {
        var (store, _, _) = Fused();
        var unit = ShapeFixtures.Item(ShapeFixtures.Catalog, 1);

        await store.LinkAsync(new UnitFileLink(unit, MediaFileId.FromInt64(1), null));

        var act = async () => await store.LinkAsync(new UnitFileLink(unit, MediaFileId.FromInt64(2), null));

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("at most one");
    }

    [Test]
    public async Task ASecondUnitIsRefusedForAFileDeclaredToSatisfyAtMostOne()
    {
        var (store, _, _) = Fused();
        var file = MediaFileId.FromInt64(1);

        await store.LinkAsync(new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Catalog, 1), file, null));

        var act = async () => await store.LinkAsync(
            new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Catalog, 2), file, null));

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("at most one");
    }

    [Test]
    public async Task AnOrdinalIsRefusedWhereTheShapeGivesItNoMeaning()
    {
        var (store, _, _) = Fused();

        var act = async () => await store.LinkAsync(
            new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Catalog, 1), MediaFileId.FromInt64(1), 2));

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("no meaning");
    }

    [Test]
    public async Task AUnitAtTheWrongLevelIsRefused()
    {
        var (store, _) = Layered();

        var act = async () => await store.LinkAsync(
            new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Work, 1), MediaFileId.FromInt64(1), null));

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("satisfies units at level");
    }

    [Test]
    public async Task OneFileMaySatisfySeveralUnitsWhereTheShapeAllowsIt()
    {
        var (store, items) = Layered();
        var file = MediaFileId.FromInt64(1);
        var first = ShapeFixtures.Item(ShapeFixtures.Part, 1);
        var second = ShapeFixtures.Item(ShapeFixtures.Part, 2);

        items.With(first, ShapeFixtures.At(1, 1)).With(second, ShapeFixtures.At(1, 2));

        await store.LinkAsync(new UnitFileLink(first, file, null));
        await store.LinkAsync(new UnitFileLink(second, file, null));

        (await store.LinksForFileAsync(file)).Should().HaveCount(2);
    }

    [Test]
    public async Task OneFileSpanningAConstrainedComponentIsRefused()
    {
        var (store, items) = Layered();
        var file = MediaFileId.FromInt64(1);
        var first = ShapeFixtures.Item(ShapeFixtures.Part, 1);
        var acrossTheBoundary = ShapeFixtures.Item(ShapeFixtures.Part, 2);

        // The two units sit in different groups of the constrained component, which is exactly what the
        // declared span rule forbids one file from doing.
        items.With(first, ShapeFixtures.At(1, 12)).With(acrossTheBoundary, ShapeFixtures.At(2, 1));

        await store.LinkAsync(new UnitFileLink(first, file, null));

        var act = async () => await store.LinkAsync(new UnitFileLink(acrossTheBoundary, file, null));

        (await act.Should().ThrowAsync<ArronixException>()).And.Message.Should().Contain("spanning component");
    }

    [Test]
    public async Task LinkingTheSamePairTwiceIsAcceptedAndRecordedOnce()
    {
        var (store, _, _) = Fused();
        var link = new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Catalog, 1), MediaFileId.FromInt64(1), null);

        await store.LinkAsync(link);
        await store.LinkAsync(link);

        (await store.LinksForUnitAsync(link.Unit)).Should().HaveCount(1);
    }

    [Test]
    public async Task UnlinkingRemovesTheJoinFromBothDirections()
    {
        var (store, _, _) = Fused();
        var link = new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Catalog, 1), MediaFileId.FromInt64(1), null);

        await store.LinkAsync(link);
        await store.UnlinkAsync(link);

        (await store.LinksForUnitAsync(link.Unit)).Should().BeEmpty();
        (await store.LinksForFileAsync(link.File)).Should().BeEmpty();
    }
}
