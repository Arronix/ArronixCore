using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using FluentAssertions;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// The G04 assignment rule once its answers are written down: what a restart continues, and what a merge
/// now moves.
/// </summary>
[TestFixture]
internal sealed class DurableCatalogIdentityTests
{
    private static readonly MediaKindId Kind = MediaKindId.FromString("works");
    private static readonly MediaLevelId Level = MediaLevelId.FromString("work");
    private static readonly DateTimeOffset Added = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private DurableStoreFixture _store = null!;

    [SetUp]
    public void SetUp() => _store = new DurableStoreFixture();

    [TearDown]
    public void TearDown() => _store.Dispose();

    /// <summary>The same identifier is the same item, before and after the process that first saw it.</summary>
    [Test]
    public void AnIdentifierKeepsItsIdentityAcrossARestart()
    {
        var first = _store.Identity().Identify(Kind, Level, [ExternalId.Of("alpha", "1")]);

        _store.Reopen();

        _store.Identity().Identify(Kind, Level, [ExternalId.Of("alpha", "1")]).Should().Be(first);
    }

    /// <summary>
    /// The allocator continues its sequence rather than reissuing numbers the library is keyed by.
    /// </summary>
    [Test]
    public void TheAllocatorDoesNotReissueAnIdentityAfterARestart()
    {
        var identity = _store.Identity();
        identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1")]);
        identity.Identify(Kind, Level, [ExternalId.Of("alpha", "2")]);

        _store.Reopen();

        _store.Identity()
            .Identify(Kind, Level, [ExternalId.Of("alpha", "3")]).Id
            .Should().Be(MediaItemId.FromInt64(3));
    }

    /// <summary>A reference a merge superseded still resolves, and still does after a restart.</summary>
    [Test]
    public void ASupersededReferenceResolvesAfterARestart()
    {
        var identity = _store.Identity();
        var alpha = identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1")]);
        var beta = identity.Identify(Kind, Level, [ExternalId.Of("beta", "2")]);

        // Seen together, so they are one entity: the later assignment merges onto the earlier.
        var merged = identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]);

        _store.Reopen();

        Assert.Multiple(() =>
        {
            merged.Should().Be(alpha, "the survivor is the lower assignment");
            _store.Identity().Canonical(beta).Should().Be(alpha);
        });
    }

    /// <summary>
    /// The limitation G04 recorded is closed: a merge moves the rows keyed by the identity it supersedes.
    /// </summary>
    [Test]
    public async Task AMergeMovesTheRecordAndTheLibraryEntryOntoTheSurvivor()
    {
        var identity = _store.Identity();
        var alpha = identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1")]);
        var beta = identity.Identify(Kind, Level, [ExternalId.Of("beta", "2")]);

        // Only the superseded reference has been added, so the merge has something to carry.
        await _store.Records().MaterializeAsync(
            DurableStoreFixture.Record(beta, "beta", "2", title: "Carried"),
            Added);

        identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]);

        var moved = await _store.Records().FindAsync(alpha);
        var left = await _store.Records().FindAsync(beta);

        using var context = _store.Read();

        Assert.Multiple(() =>
        {
            moved.Should().NotBeNull("the record follows the identity it belongs to");
            moved!.Title.Should().Be("Carried");
            left.Should().BeNull("and is not left under the identity nothing resolves to");
            context.CatalogRecords.Should().ContainSingle();
            context.LibraryEntries.Single().Identity.Should().Be(alpha.Id.Value);
        });
    }

    /// <summary>
    /// When both identities were added, the survivor's record stands and the user loses nothing.
    /// </summary>
    [Test]
    public async Task AMergeOfTwoAddedItemsKeepsTheSurvivorAndTheEarlierAdditionDate()
    {
        var identity = _store.Identity();
        var alpha = identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1")]);
        var beta = identity.Identify(Kind, Level, [ExternalId.Of("beta", "2")]);
        var records = _store.Records();

        await records.MaterializeAsync(
            DurableStoreFixture.Record(alpha, "alpha", "1", title: "Survivor"),
            Added.AddDays(10));

        await records.MaterializeAsync(
            DurableStoreFixture.Record(beta, "beta", "2", title: "Superseded"),
            Added);

        identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]);

        using var context = _store.Read();

        Assert.Multiple(() =>
        {
            context.CatalogRecords.Single().Title.Should().Be("Survivor");
            context.LibraryEntries.Should().ContainSingle();
            context.LibraryEntries.Single().AddedAt.Should().Be(
                Added,
                "the user added this work on the earlier of the two dates");
        });
    }

    /// <summary>
    /// Three identifiers found to name one work merge in one transaction, and commit as one item.
    /// </summary>
    /// <remarks>
    /// One commit carries two supersessions. The second move has to see what the first one did but has not
    /// yet saved, or it writes a second record under the survivor's key and a second answer on an axis that
    /// admits one — both of which only fail when the transaction commits.
    /// </remarks>
    [Test]
    public async Task ThreeIdentifiersMergingAtOnceCommitAsOneItemAndSurviveARestart()
    {
        var identity = _store.Identity();
        var alpha = identity.Identify(Kind, Level, [ExternalId.Of("alpha", "1")]);
        var beta = identity.Identify(Kind, Level, [ExternalId.Of("beta", "2")]);
        var gamma = identity.Identify(Kind, Level, [ExternalId.Of("gamma", "3")]);
        var records = _store.Records();

        // The survivor has nothing; both superseded identities carry a record and a library entry.
        await records.MaterializeAsync(
            DurableStoreFixture.Record(beta, "beta", "2", title: "From beta"),
            Added.AddDays(5));

        await records.MaterializeAsync(
            DurableStoreFixture.Record(gamma, "gamma", "3", title: "From gamma"),
            Added);

        await MonitorAsync(beta, "wanted", "true");
        await MonitorAsync(gamma, "wanted", "false");
        await MonitorAsync(gamma, "upgrade", "always");

        // All three seen together: two supersessions in one commit.
        identity.Identify(
            Kind,
            Level,
            [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2"), ExternalId.Of("gamma", "3")]);

        _store.Reopen();
        var restarted = _store.Identity();
        using var context = _store.Read();
        var entry = context.LibraryEntries.Single();

        Assert.Multiple(() =>
        {
            context.CatalogRecords.Should().ContainSingle("one work is one record");
            context.CatalogRecords.Single().Identity.Should().Be(alpha.Id.Value);
            context.LibraryEntries.Should().ContainSingle();
            entry.Identity.Should().Be(alpha.Id.Value);
            entry.AddedAt.Should().Be(Added, "the earliest of the dates the user added it");

            var answers = context.LibraryMonitors
                .Where(monitor => monitor.EntryId == entry.Id)
                .ToList()
                .ToDictionary(monitor => monitor.Dimension, monitor => monitor.Choice, StringComparer.Ordinal);

            answers.Keys.Order(StringComparer.Ordinal)
                .Should().Equal(["upgrade", "wanted"], "each axis is answered once");

            // The merges run in ascending superseded order, so beta is carried onto the survivor first and
            // its answer is the one in place when gamma's conflicting answer arrives. The survivor's
            // existing answer stands; only an axis it does not state is filled in.
            answers["wanted"].Should().Be("true", "beta's answer was the survivor's by the time gamma merged");
            answers["upgrade"].Should().Be("always", "and gamma answered an axis nothing else had");

            restarted.Canonical(beta).Should().Be(alpha);
            restarted.Canonical(gamma).Should().Be(alpha);
        });
    }

    /// <summary>Attaches one monitoring answer directly, which is what a user's facet holds.</summary>
    private async Task MonitorAsync(MediaItemRef reference, string dimension, string choice)
    {
        await using var context = _store.Read();

        var entry = context.LibraryEntries.Single(candidate =>
            candidate.Kind == reference.Kind.Value
            && candidate.Level == reference.Level.Value
            && candidate.Identity == reference.Id.Value);

        context.LibraryMonitors.Add(new Arronix.Host.Storage.Durable.LibraryMonitorRow
        {
            EntryId = entry.Id,
            Dimension = dimension,
            Choice = choice,
        });

        await context.SaveChangesAsync();
    }
}
