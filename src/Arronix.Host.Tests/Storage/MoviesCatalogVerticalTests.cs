using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using FluentAssertions;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// The catalog vertical end to end over the real movies contract: what a search does, what an add does,
/// what a refresh may change, and what survives a restart.
/// </summary>
[TestFixture]
internal sealed class MoviesCatalogVerticalTests
{
    private static readonly MediaLevelId Level = MediaLevelId.FromString("movie");

    /// <summary>Searching resolves identity and materializes nothing.</summary>
    [Test]
    public async Task ASearchMintsIdentityAndWritesNoRecord()
    {
        using var harness = MoviesCatalogHarness.With(
            new MoviesCatalogHarness.StubCataloger("alpha")
                .Holding("1", MoviesCatalogHarness.Film("The Matrix", [ExternalId.Of("alpha", "1")])));

        var found = await harness.Catalog.SearchAsync(
            MoviesCatalogHarness.Kind,
            "alpha",
            new CatalogQuery("matrix"),
            page: 1,
            pageSize: 10);

        using var context = harness.Read();

        Assert.Multiple(() =>
        {
            found.Items.Should().ContainSingle();
            found.Items[0].Item.Title.Should().Be("The Matrix");
            found.Items[0].InLibrary.Should().BeFalse("nobody has added it");
            found.TotalCount.Should().Be(1);
            context.CatalogRecords.Should().BeEmpty("a search is a question, not a decision");
            context.LibraryEntries.Should().BeEmpty();
            context.CatalogIdentities.Should().NotBeEmpty("but the identity it would be held under is settled");
        });
    }

    /// <summary>An add writes the record and the user's presence together, and is safe to repeat.</summary>
    [Test]
    public async Task AnAddIsAtomicAndIdempotent()
    {
        using var harness = MoviesCatalogHarness.With(
            new MoviesCatalogHarness.StubCataloger("alpha")
                .Holding("1", MoviesCatalogHarness.Film("The Matrix", [ExternalId.Of("alpha", "1")])));

        var first = await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("alpha", "1"));
        var again = await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("alpha", "1"));

        using var context = harness.Read();

        Assert.Multiple(() =>
        {
            first!.Value.Created.Should().BeTrue();
            again!.Value.Created.Should().BeFalse();
            again!.Value.View.Item.Ref.Should().Be(first!.Value.View.Item.Ref, "the same item keeps the same identity");
            first!.Value.View.InLibrary.Should().BeTrue();
            context.CatalogRecords.Should().ContainSingle();
            context.LibraryEntries.Should().ContainSingle("and one add is one library entry");
        });
    }

    /// <summary>What was added is what browse reads, after the process that added it has gone.</summary>
    [Test]
    public async Task AnAddedMovieIsBrowsedBackAfterARestart()
    {
        var cataloger = new MoviesCatalogHarness.StubCataloger("alpha")
            .Holding("1", MoviesCatalogHarness.Film("The Matrix", [ExternalId.Of("alpha", "1")]));

        using var harness = MoviesCatalogHarness.With(cataloger);
        var added = await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("alpha", "1"));

        using var restarted = harness.Restart(
            new MoviesCatalogHarness.StubCataloger("alpha")
                .Holding("1", MoviesCatalogHarness.Film("The Matrix", [ExternalId.Of("alpha", "1")])));

        var page = await restarted.Items.QueryAsync(
            MoviesCatalogHarness.Kind,
            new ItemQuery { Kind = MoviesCatalogHarness.Kind, Level = Level });

        var one = await restarted.Items.GetAsync(MoviesCatalogHarness.Kind, added!.Value.View.Item.Ref);

        Assert.Multiple(() =>
        {
            page.Should().NotBeNull();
            page!.Items.Should().ContainSingle();
            page.Items[0].Title.Should().Be("The Matrix");
            page.Items[0].Fields["year"].Number.Should().Be(1999, "the typed facts came back, not just a title");
            page.Items[0].Fields.Should().ContainKey("lifecycle");
            one.Should().NotBeNull();
            one!.Ref.Should().Be(added!.Value.View.Item.Ref);
        });
    }

    /// <summary>A refresh restates the catalog's half and cannot reach the user's.</summary>
    [Test]
    public async Task ARefreshUpdatesCatalogFactsAndSurfacesAWithdrawnRecord()
    {
        var cataloger = new MoviesCatalogHarness.StubCataloger("alpha")
            .Holding("1", MoviesCatalogHarness.Film("The Matrix", [ExternalId.Of("alpha", "1")]));

        using var harness = MoviesCatalogHarness.With(cataloger);
        var added = await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("alpha", "1"));
        var addedAt = harness.Read().LibraryEntries.Single().AddedAt;

        cataloger.Holding("1", MoviesCatalogHarness.Film(
            "The Matrix Resurrections",
            [ExternalId.Of("alpha", "1")],
            CatalogRecordState.Withdrawn));

        var refreshed = await harness.Catalog.RefreshAsync(MoviesCatalogHarness.Kind, added!.Value.View.Item.Ref);

        var browsed = await harness.Items.GetAsync(MoviesCatalogHarness.Kind, added!.Value.View.Item.Ref);
        using var context = harness.Read();

        Assert.Multiple(() =>
        {
            refreshed.Should().NotBeNull();
            refreshed!.Item.Title.Should().Be("The Matrix Resurrections");
            refreshed.InLibrary.Should().BeTrue("the user's presence is untouched by a catalog write");
            context.LibraryEntries.Single().AddedAt.Should().Be(addedAt);
            context.CatalogRecords.Single().CatalogState.Should().Be((int)CatalogRecordState.Withdrawn);
            browsed.Should().NotBeNull("a withdrawn record stays addressable");
            browsed!.Fields["catalogState"].Text.Should().Be("withdrawn", "and browse says what it is");
        });
    }

    /// <summary>
    /// A refresh that discovers a cross-reference follows the merge onto the surviving record, and
    /// continues through that record's own catalog rather than overwriting it.
    /// </summary>
    [Test]
    public async Task ARefreshFollowsAMergeOntoAnotherCatalogsRecordAndKeepsThatOwner()
    {
        var beta = new MoviesCatalogHarness.StubCataloger("beta")
            .Holding("2", MoviesCatalogHarness.Film("Owned by beta", [ExternalId.Of("beta", "2")]));

        var alpha = new MoviesCatalogHarness.StubCataloger("alpha")
            .Holding("1", MoviesCatalogHarness.Film("Owned by alpha", [ExternalId.Of("alpha", "1")]));

        using var harness = MoviesCatalogHarness.With(beta, alpha);

        // Beta is added first, so it holds the lower identity and is the one a merge keeps.
        await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("beta", "2"));
        var viaAlpha = await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("alpha", "1"));

        // Alpha now discovers that its record is the same work beta already owns, and beta has newer facts.
        alpha.Holding("1", MoviesCatalogHarness.Film(
            "Owned by alpha",
            [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]));

        beta.Holding("2", MoviesCatalogHarness.Film("Refreshed by beta", [ExternalId.Of("beta", "2")]));

        // Cleared here, so what the ledger holds afterwards is the refresh and nothing that set it up.
        harness.Requests.Clear();

        var refreshed = await harness.Catalog.RefreshAsync(MoviesCatalogHarness.Kind, viaAlpha!.Value.View.Item.Ref);
        var order = harness.Requests.ToArray();

        using var restarted = harness.Restart(
            new MoviesCatalogHarness.StubCataloger("beta"),
            new MoviesCatalogHarness.StubCataloger("alpha"));

        using var context = restarted.Read();
        var record = context.CatalogRecords.Single();

        Assert.Multiple(() =>
        {
            refreshed.Should().NotBeNull();
            refreshed!.CatalogId.Should().Be(
                ExternalId.Of("beta", "2"),
                "the merge kept beta's record, so beta is still the authority for it");
            refreshed.Item.Title.Should().Be(
                "Refreshed by beta",
                "and the facts published are the ones the surviving owner answered with");

            order.Should().Equal(
                ["alpha:1", "beta:2"],
                "the refresh starts at the record it was given, continues once through the surviving "
                + "record's own catalog, and stops");

            record.CatalogScheme.Should().Be("beta");
            record.Title.Should().Be("Refreshed by beta");
            context.CatalogRecords.Should().ContainSingle("one work is one record after the merge");
            context.LibraryEntries.Should().ContainSingle("and one library entry");
        });
    }

    /// <summary>Ownership cannot be handed over indefinitely: a second handover is refused, not chased.</summary>
    [Test]
    public async Task ARefreshThatKeepsChangingOwnerIsRefusedRatherThanLooping()
    {
        var gamma = new MoviesCatalogHarness.StubCataloger("gamma")
            .Holding("3", MoviesCatalogHarness.Film("Owned by gamma", [ExternalId.Of("gamma", "3")]));

        var beta = new MoviesCatalogHarness.StubCataloger("beta")
            .Holding("2", MoviesCatalogHarness.Film("Owned by beta", [ExternalId.Of("beta", "2")]));

        var alpha = new MoviesCatalogHarness.StubCataloger("alpha")
            .Holding("1", MoviesCatalogHarness.Film("Owned by alpha", [ExternalId.Of("alpha", "1")]));

        using var harness = MoviesCatalogHarness.With(gamma, beta, alpha);

        // Added lowest identity first, so each handover moves to a record owned by someone else again.
        await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("gamma", "3"));
        await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("beta", "2"));
        var viaAlpha = await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("alpha", "1"));

        alpha.Holding("1", MoviesCatalogHarness.Film(
            "Owned by alpha",
            [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]));

        beta.Holding("2", MoviesCatalogHarness.Film(
            "Owned by beta",
            [ExternalId.Of("beta", "2"), ExternalId.Of("gamma", "3")]));

        var act = async () => await harness.Catalog.RefreshAsync(
            MoviesCatalogHarness.Kind,
            viaAlpha!.Value.View.Item.Ref);

        (await act.Should().ThrowAsync<ArronixException>())
            .Which.ErrorCode.Should().Be(CoreErrorCode.CatalogIdentityInvalid);
    }

    /// <summary>
    /// The database belongs to the harness that made it, so a restart neither takes it away nor deletes it
    /// twice.
    /// </summary>
    /// <remarks>
    /// A restart is a second harness over one file. If the second owned it too, disposing it would delete
    /// the database the first is still reading, and disposing both would try to delete it twice — either of
    /// which would make a restart proof pass or fail for a reason that is not about durability.
    /// </remarks>
    [Test]
    public async Task ARestartDoesNotTakeTheDatabaseFromTheHarnessThatMadeIt()
    {
        using var harness = MoviesCatalogHarness.With(
            new MoviesCatalogHarness.StubCataloger("alpha")
                .Holding("1", MoviesCatalogHarness.Film("The Matrix", [ExternalId.Of("alpha", "1")])));

        await harness.Catalog.AddAsync(MoviesCatalogHarness.Kind, ExternalId.Of("alpha", "1"));

        var restarted = harness.Restart(new MoviesCatalogHarness.StubCataloger("alpha"));
        restarted.Dispose();

        // Disposed twice on purpose: a harness that does not own the file must be safe to dispose again.
        var disposingAgain = restarted.Dispose;

        using var stillThere = harness.Read();

        Assert.Multiple(() =>
        {
            disposingAgain.Should().NotThrow();
            stillThere.CatalogRecords.Should().ContainSingle(
                "the file outlived the harness that restarted over it");
        });
    }
}
