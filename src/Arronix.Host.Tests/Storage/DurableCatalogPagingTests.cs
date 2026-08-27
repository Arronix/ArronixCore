using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Storage;
using FluentAssertions;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// How a page of catalog records is taken: what the store orders by, what it filters on, and what it
/// refuses to answer at all.
/// </summary>
[TestFixture]
internal sealed class DurableCatalogPagingTests
{
    private static readonly MediaKindId Kind = MediaKindId.FromString("works");
    private static readonly MediaLevelId Level = MediaLevelId.FromString("work");
    private static readonly DateTimeOffset Added = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private DurableStoreFixture _store = null!;

    [SetUp]
    public void SetUp() => _store = new DurableStoreFixture();

    [TearDown]
    public void TearDown() => _store.Dispose();

    /// <summary>
    /// The characters a stored pattern would otherwise treat as wildcards are searched for literally.
    /// </summary>
    /// <remarks>
    /// The interesting rows are chosen so an unescaped pattern passes: <c>%100%%</c> matches every title
    /// beginning "100", and <c>%1_0%</c> matches "1x0" as well as "1_0". Both cases fail loudly if the
    /// escaping is removed.
    /// </remarks>
    [TestCase("100%", "100% Wolf")]
    [TestCase("1_0", "1_0 Remastered")]
    public async Task LiteralWildcardsInASearchAreNotWildcards(string search, string expected)
    {
        await SeedAsync("100% Wolf", "1000 Ships", "1_0 Remastered", "1x0 Alternate");

        var page = await _store.Records().PageAsync(
            Kind,
            Level,
            new CatalogRecordQuery(search, ByTitle: true, Descending: false, Page: 1, PageSize: 10));

        page.Records.Select(record => record.Title).Should().Equal(expected);
    }

    /// <summary>The escape character itself is a character somebody can have in a title.</summary>
    [Test]
    public async Task TheEscapeCharacterIsSearchedForLiterally()
    {
        await SeedAsync(@"AC\DC Live", "ACDC Live");

        var page = await _store.Records().PageAsync(
            Kind,
            Level,
            new CatalogRecordQuery(@"AC\DC", ByTitle: true, Descending: false, Page: 1, PageSize: 10));

        page.Records.Select(record => record.Title).Should().Equal(@"AC\DC Live");
    }

    /// <summary>Bounds a store cannot serve are refused, not quietly turned into ones it can.</summary>
    [TestCase(0, 10)]
    [TestCase(-1, 10)]
    [TestCase(1, 0)]
    [TestCase(1, -5)]
    public async Task NonPositiveBoundsAreRefused(int page, int pageSize)
    {
        var act = async () => await _store.Records().PageAsync(
            Kind,
            Level,
            new CatalogRecordQuery(null, false, false, page, pageSize));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// A page number large enough to overflow a 32-bit offset answers with no rows rather than negative
    /// ones, and still says how many there were.
    /// </summary>
    [Test]
    public async Task APageBeyondAnInt32OffsetIsEmptyAndStillReportsTheTotal()
    {
        await SeedAsync("One", "Two");

        var page = await _store.Records().PageAsync(
            Kind,
            Level,
            new CatalogRecordQuery(null, false, false, Page: int.MaxValue, PageSize: 1000));

        Assert.Multiple(() =>
        {
            page.Records.Should().BeEmpty();
            page.TotalCount.Should().Be(2);
        });
    }

    /// <summary>Records sharing a title keep a stable order, so paging cannot show one twice.</summary>
    [Test]
    public async Task RecordsSharingATitleAreOrderedStablyByIdentity()
    {
        await SeedAsync("Same", "Same", "Same");
        var records = _store.Records();
        var query = new CatalogRecordQuery(null, ByTitle: true, Descending: false, Page: 1, PageSize: 2);

        var first = await records.PageAsync(Kind, Level, query);
        var second = await records.PageAsync(Kind, Level, query with { Page = 2 });

        Assert.Multiple(() =>
        {
            first.Records.Select(record => record.Reference.Id.Value).Should().Equal(1L, 2L);
            second.Records.Select(record => record.Reference.Id.Value).Should().Equal(3L);
            first.TotalCount.Should().Be(3);
        });
    }

    private async Task SeedAsync(params string[] titles)
    {
        var records = _store.Records();

        for (var index = 0; index < titles.Length; index++)
        {
            await records.MaterializeAsync(
                DurableStoreFixture.Record(
                    DurableStoreFixture.Item(index + 1),
                    "alpha",
                    (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    titles[index]),
                Added);
        }
    }
}
