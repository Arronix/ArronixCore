using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Storage;
using FluentAssertions;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// What the catalog record store promises about adding, refreshing and finding a record.
/// </summary>
[TestFixture]
internal sealed class DurableCatalogRecordStoreTests
{
    private static readonly MediaKindId Kind = MediaKindId.FromString("works");
    private static readonly MediaLevelId Level = MediaLevelId.FromString("work");
    private static readonly DateTimeOffset Added = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private DurableStoreFixture _store = null!;

    [SetUp]
    public void SetUp() => _store = new DurableStoreFixture();

    [TearDown]
    public void TearDown() => _store.Dispose();

    /// <summary>The first of the four addressing cases: the same item under the same identifier.</summary>
    [Test]
    public async Task AddingTheSamePairTwiceWritesOneRecord()
    {
        var records = _store.Records();
        var record = DurableStoreFixture.Record(DurableStoreFixture.Item(1), "alpha", "1");

        var first = await records.MaterializeAsync(record, Added);
        var second = await records.MaterializeAsync(record, Added.AddHours(1));

        using var context = _store.Read();

        Assert.Multiple(() =>
        {
            first.Created.Should().BeTrue();
            second.Created.Should().BeFalse("the same pair is the same add, retried");
            second.Record.Reference.Should().Be(first.Record.Reference);
            context.CatalogRecords.Count().Should().Be(1);
        });
    }

    /// <summary>The second case: one item cannot state two identifiers in its own catalog's scheme.</summary>
    [Test]
    public async Task ASecondIdentifierInTheOwningSchemeIsRefused()
    {
        var records = _store.Records();
        await records.MaterializeAsync(DurableStoreFixture.Record(DurableStoreFixture.Item(1), "alpha", "1"), Added);

        var act = async () => await records.MaterializeAsync(
            DurableStoreFixture.Record(DurableStoreFixture.Item(1), "alpha", "9"),
            Added);

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.CatalogIdentityInvalid);
    }

    /// <summary>The third case: one catalog identifier materializes one item and no other.</summary>
    [Test]
    public async Task AnIdentifierAlreadyHeldUnderAnotherItemIsRefused()
    {
        var records = _store.Records();
        await records.MaterializeAsync(DurableStoreFixture.Record(DurableStoreFixture.Item(1), "alpha", "1"), Added);

        var act = async () => await records.MaterializeAsync(
            DurableStoreFixture.Record(DurableStoreFixture.Item(2), "alpha", "1"),
            Added);

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.CatalogIdentityInvalid);
    }

    /// <summary>
    /// The fourth case: two catalogs that converged on one work, added through each in turn.
    /// </summary>
    /// <remarks>
    /// Run both ways round, because which catalog was asked first is the whole question: the record keeps
    /// the authority that materialized it, and being asked second does not transfer ownership.
    /// </remarks>
    [TestCase("alpha", "1", "beta", "2")]
    [TestCase("beta", "2", "alpha", "1")]
    public async Task ConvergedIdentifiersFromTwoCatalogsAreOneRecordOwnedByTheFirst(
        string firstScheme,
        string firstValue,
        string secondScheme,
        string secondValue)
    {
        var records = _store.Records();
        var item = DurableStoreFixture.Item(1);

        var first = await records.MaterializeAsync(
            DurableStoreFixture.Record(item, firstScheme, firstValue, title: "First"),
            Added);

        var second = await records.MaterializeAsync(
            DurableStoreFixture.Record(item, secondScheme, secondValue, title: "Second"),
            Added.AddHours(1));

        using var context = _store.Read();

        Assert.Multiple(() =>
        {
            first.Created.Should().BeTrue();
            second.Created.Should().BeFalse("the identity says the two catalogs named one work");
            second.Record.CatalogId.Should().Be(
                ExternalId.Of(firstScheme, firstValue),
                "the record keeps the authority that materialized it");
            second.Record.Title.Should().Be("First", "and the stored facts are the ones that were stored");
            context.CatalogRecords.Count().Should().Be(1);
            context.LibraryEntries.Count().Should().Be(1);
        });
    }

    /// <summary>The record and the user's presence are one act, so one add writes both.</summary>
    [Test]
    public async Task AddingWritesTheRecordAndThePresenceTogether()
    {
        await _store.Records().MaterializeAsync(
            DurableStoreFixture.Record(DurableStoreFixture.Item(1), "alpha", "1"),
            Added);

        using var context = _store.Read();
        var entry = context.LibraryEntries.Single();

        Assert.Multiple(() =>
        {
            context.CatalogRecords.Should().ContainSingle();
            entry.Identity.Should().Be(1);
            entry.AddedAt.Should().Be(Added);
        });
    }

    /// <summary>A retry is not a second decision, so it does not restamp when the user added the item.</summary>
    [Test]
    public async Task ARetriedAddKeepsTheDateTheUserAddedIt()
    {
        var records = _store.Records();
        var record = DurableStoreFixture.Record(DurableStoreFixture.Item(1), "alpha", "1");

        await records.MaterializeAsync(record, Added);
        await records.MaterializeAsync(record, Added.AddYears(1));

        using var context = _store.Read();
        context.LibraryEntries.Single().AddedAt.Should().Be(Added);
    }

    /// <summary>A refresh replaces what the catalog says and cannot reach what the user decided.</summary>
    [Test]
    public async Task ARefreshReplacesCatalogFactsAndLeavesPresenceAlone()
    {
        var records = _store.Records();
        var item = DurableStoreFixture.Item(1);
        await records.MaterializeAsync(
            DurableStoreFixture.Record(item, "alpha", "1", title: "Before"),
            Added);

        var refreshed = await records.RefreshAsync(DurableStoreFixture.Record(
            item,
            "alpha",
            "1",
            title: "After",
            state: CatalogRecordState.Withdrawn));

        using var context = _store.Read();

        Assert.Multiple(() =>
        {
            refreshed.Title.Should().Be("After");
            refreshed.State.Should().Be(CatalogRecordState.Withdrawn);
            refreshed.Revision.Should().Be(2, "a catalog write is countable");
            context.LibraryEntries.Single().AddedAt.Should().Be(Added, "the user's half is untouched");
        });
    }

    /// <summary>A catalog refreshes the records it is the authority for, and no others.</summary>
    [Test]
    public async Task ARefreshFromAnotherCatalogIsRefused()
    {
        var records = _store.Records();
        var item = DurableStoreFixture.Item(1);
        await records.MaterializeAsync(DurableStoreFixture.Record(item, "alpha", "1"), Added);

        var act = async () => await records.RefreshAsync(DurableStoreFixture.Record(item, "beta", "2"));

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.CatalogIdentityInvalid);
    }

    /// <summary>A withdrawn record is still a record: it is found, and it says what it is.</summary>
    [Test]
    public async Task AWithdrawnRecordStaysAddressable()
    {
        var records = _store.Records();
        var item = DurableStoreFixture.Item(1);
        await records.MaterializeAsync(DurableStoreFixture.Record(item, "alpha", "1"), Added);
        await records.RefreshAsync(DurableStoreFixture.Record(
            item,
            "alpha",
            "1",
            state: CatalogRecordState.Withdrawn));

        var found = await records.FindAsync(item);
        var page = await records.PageAsync(Kind, Level, new CatalogRecordQuery(null, false, false, 1, 10));

        Assert.Multiple(() =>
        {
            found.Should().NotBeNull();
            found!.State.Should().Be(CatalogRecordState.Withdrawn);
            page.Records.Should().ContainSingle("a withdrawn record is still browsed, saying what it is");
        });
    }

    /// <summary>What was written is there after the process that wrote it has gone.</summary>
    [Test]
    public async Task ARecordAndItsPresenceSurviveARestart()
    {
        await _store.Records().MaterializeAsync(
            DurableStoreFixture.Record(DurableStoreFixture.Item(1), "alpha", "1", title: "Kept"),
            Added);

        _store.Reopen();

        var found = await _store.Records().FindAsync(DurableStoreFixture.Item(1));

        using var context = _store.Read();

        Assert.Multiple(() =>
        {
            found.Should().NotBeNull();
            found!.Title.Should().Be("Kept");
            found.Payload.Should().Equal([1, 2, 3]);
            context.LibraryEntries.Single().AddedAt.Should().Be(Added);
        });
    }
}
