using System.Collections;
using System.Linq;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Media.Typed;
using Arronix.Host.Providers;
using Arronix.Host.Tests.TypedMedia;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Time.Testing;


namespace Arronix.Host.Tests.Providers;

/// <summary>
/// Materializing a catalog record: a cataloger says what an item is, the host says what it is called here.
/// </summary>
[TestFixture]
internal sealed class CatalogMaterializationTests
{
    [Test]
    public async Task ACatalogerReturnsTheExactItemCarryingItsOwnCatalogIdentity()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        fetch.Outcome.Should().Be(CatalogFetchOutcome.Found);
        fetch.Candidate.Should().NotBeNull();
        fetch.Candidate!.Item.Title.Should().Be("Arrival");
        fetch.Candidate.CatalogId.Should().Be(ExternalId.Of("alpha", "1"));
        fetch.Candidate.Held.Should().BeNull("nothing has been taken in, so the platform holds no reference");
    }

    /// <summary>
    /// O-40's decisive half: a catalog search is a read. It answers with the catalog's identity and the
    /// exact item, and the identity space is the same size afterwards as it was before.
    /// </summary>
    /// <remarks>
    /// Asserted through the ordinary lookup a caller would make, and controlled twice over: the same lookup
    /// does find the record once it is materialized, and the identity that materialization receives is the
    /// first this kind ever issues — which it could not be if the search had spent one.
    /// </remarks>
    [Test]
    public async Task ASearchLeavesTheIdentityStateExactlyAsItFoundIt()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var found = await context.Dispatcher.SearchAsync<Work>(context.Kind, "alpha", new CatalogQuery("Arrival"));
        var afterSearching = context.Holds(ExternalId.Of("alpha", "1"));
        var taken = context.Dispatcher.Materialize(context.Kind, found.Candidates[0]);

        using var assertions = new AssertionScope();
        found.Candidates.Should().ContainSingle().Which.CatalogId.Should().Be(ExternalId.Of("alpha", "1"));
        found.IsPartialResult.Should().BeFalse();
        found.Candidates[0].Held.Should().BeNull();
        afterSearching.Should().BeFalse("searching a catalog is a read and names nothing locally");
        context.Holds(ExternalId.Of("alpha", "1")).Should().BeTrue(
            "the control: the same lookup does find the record once it is materialized");
        taken.Reference.Id.Value.Should().Be(
            1,
            "and it receives the first identity this kind issues, so the search spent none");
    }

    /// <summary>Fetching is a read too, and repeating one cannot grow the identity space either.</summary>
    [Test]
    public async Task RepeatedFetchesAllocateNothingAndReportTheReferenceOnlyOnceItIsHeld()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var before = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var again = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var heldWhileReading = context.Holds(ExternalId.Of("alpha", "1"));

        var taken = context.Dispatcher.Materialize(context.Kind, again.Candidate!);
        var after = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        heldWhileReading.Should().BeFalse("two fetches of an unheld record name nothing locally");
        before.Candidate!.Held.Should().BeNull();
        again.Candidate!.Held.Should().BeNull();
        taken.Reference.Id.Value.Should().Be(1, "so the first identity issued goes to the first take-in");
        after.Candidate!.Held.Should().Be(taken.Reference, "once held, a fetch reports the reference it is held under");
        context.Holds(ExternalId.Of("alpha", "1")).Should().BeTrue("the control: the lookup does find it");
    }

    /// <remarks>
    /// The control is the unrelated record taken in afterwards: it receives the very next identity, so the
    /// repeat between them consumed none. A repeat that minted would push it one further along.
    /// </remarks>
    [Test]
    public async Task RepeatedTakeInOfOneRecordAllocatesOnce()
    {
        var context = CatalogContext.WithCatalogers(
            new StubCataloger("alpha", Work("alpha", "1", "Arrival")),
            new StubCataloger("beta", Work("beta", "9", "Another work")));

        var first = await context.TakeInAsync(ExternalId.Of("alpha", "1"));
        var second = await context.TakeInAsync(ExternalId.Of("alpha", "1"));
        var unrelated = await context.TakeInAsync(ExternalId.Of("beta", "9"));

        using var assertions = new AssertionScope();
        second.Reference.Should().Be(first.Reference);
        first.Reference.Id.Value.Should().Be(1, "the host assigned it, and the cataloger never saw it");
        unrelated.Reference.Id.Value.Should().Be(
            2,
            "the control: the next distinct record takes the next identity, so the repeat minted nothing");
    }

    /// <summary>
    /// A redirect keeps the identifier that was asked for, and binds it alongside the one answered with.
    /// </summary>
    /// <remarks>
    /// The catalog resolved its own alias, so the two name one record. Dropping the alias would leave a
    /// caller still holding it — a bookmark, a stored reference, an operator typing it again — unable to
    /// resolve locally, which is exactly the idempotence O-40 requires over aliases and redirects.
    /// </remarks>
    [Test]
    public async Task ARedirectRetainsTheIdentifierAskedForAndBindsItWithTheOneAnsweredWith()
    {
        // The catalog followed its own alias: asked for 'old', it answered with the record it calls 'new'.
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "new", "Arrival")));

        var redirected = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "old"));
        var viaAlias = context.Dispatcher.Materialize(context.Kind, redirected.Candidate!);
        var direct = await context.TakeInAsync(ExternalId.Of("alpha", "new"));

        using var assertions = new AssertionScope();
        redirected.Candidate!.CatalogId.Should().Be(
            ExternalId.Of("alpha", "new"),
            "the canonical identity is the one the item states in its cataloger's own scheme");
        redirected.Candidate.RequestedId.Should().Be(
            ExternalId.Of("alpha", "old"),
            "and the candidate carries the identifier that was actually asked for");
        direct.Reference.Should().Be(viaAlias.Reference, "an alias and the record it redirects to are one item");
        viaAlias.Reference.Id.Value.Should().Be(1, "one record, one identity, reached by either identifier");
        context.Identity.TryFind(context.Kind.Kind, Level, ExternalId.Of("alpha", "old"), out var alias)
            .Should().BeTrue("the alias was bound when the record it names was taken in");
        alias.Should().Be(viaAlias.Reference);
    }

    /// <summary>A candidate whose identifier equals the one asked for carries no redundant alias.</summary>
    [Test]
    public async Task ACandidateThatWasNotRedirectedCarriesNoRequestedAlias()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        fetch.Candidate!.RequestedId.Should().BeNull("the identity answered with is the one asked for");
    }

    /// <summary>
    /// The reference a candidate is reported held under does not depend on the order its item lists its
    /// identifiers in.
    /// </summary>
    /// <remarks>
    /// Two identifiers were assigned separately and later turn out to name one record. Assignment settles on
    /// the lower of them; reading has to settle the same way, or a candidate would be reported under one
    /// reference and materialize to another purely because a cataloger listed its identifiers differently.
    /// The witness is the same state read twice, with only the order reversed.
    /// </remarks>
    [TestCase(false, TestName = "HeldIsTheSurvivingAssignmentInDeclaredOrder")]
    [TestCase(true, TestName = "HeldIsTheSurvivingAssignmentInReversedOrder")]
    public async Task AHeldReferenceIsTheSurvivingAssignmentWhateverOrderTheIdentifiersArrive(bool reversed)
    {
        var first = ExternalId.Of("beta", "tt2");
        var second = ExternalId.Of("gamma", "g9");
        var describing = reversed
            ? Work("alpha", "1", "Arrival", second, first)
            : Work("alpha", "1", "Arrival", first, second);
        var context = CatalogContext.WithCatalogers(
            new StubCataloger("beta", Work("beta", "tt2", "Arrival")),
            new StubCataloger("gamma", Work("gamma", "g9", "Arrival")),
            new StubCataloger("alpha", describing));

        // Two separate assignments, the lower one made first, which is the survivor a merge settles on.
        var lower = await context.TakeInAsync(first);
        var higher = await context.TakeInAsync(second);
        var converging = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var taken = context.Dispatcher.Materialize(context.Kind, converging.Candidate!);

        using var assertions = new AssertionScope();
        higher.Reference.Id.Value.Should().BeGreaterThan(lower.Reference.Id.Value, "the fixture really is two");
        converging.Candidate!.Held.Should().Be(
            lower.Reference,
            "the lowest surviving assignment wins, which is the one assignment itself would settle on");
        taken.Reference.Should().Be(
            converging.Candidate.Held,
            "so what a candidate is reported under is what materializing it produces");
        context.Identity.Canonical(higher.Reference).Should().Be(lower.Reference, "and the merge happened");
    }

    /// <summary>
    /// Two catalogs, each authoritative in its own scheme, describing one work. The second answer names an
    /// identifier the first already bound, so the two assignments have to become one.
    /// </summary>
    [Test]
    public async Task IdentifiersConvergingOnOneItemResolveToOneReference()
    {
        var context = CatalogContext.WithCatalogers(
            new StubCataloger("beta", Work("beta", "tt2", "Arrival")),
            new StubCataloger("alpha", Work("alpha", "1", "Arrival", ExternalId.Of("beta", "tt2"))));

        var viaBeta = await context.TakeInAsync(ExternalId.Of("beta", "tt2"));
        var viaAlpha = await context.TakeInAsync(ExternalId.Of("alpha", "1"));
        var again = await context.TakeInAsync(ExternalId.Of("beta", "tt2"));

        using var assertions = new AssertionScope();
        viaAlpha.Reference.Should().Be(viaBeta.Reference, "they name one item");
        again.Reference.Should().Be(viaBeta.Reference, "and the identifier bound first keeps resolving");
    }

    /// <summary>
    /// A record already held converges without allocating, whichever of its identifiers is asked for.
    /// </summary>
    [Test]
    public async Task ACandidateReportedAsHeldMaterializesToTheReferenceItWasReportedUnder()
    {
        var context = CatalogContext.WithCatalogers(
            new StubCataloger("beta", Work("beta", "tt2", "Arrival")),
            new StubCataloger("alpha", Work("alpha", "1", "Arrival", ExternalId.Of("beta", "tt2"))));

        var held = await context.TakeInAsync(ExternalId.Of("beta", "tt2"));
        var viaAnotherCatalog = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var namedWhileReading = context.Holds(ExternalId.Of("alpha", "1"));
        var taken = context.Dispatcher.Materialize(context.Kind, viaAnotherCatalog.Candidate!);

        using var assertions = new AssertionScope();
        viaAnotherCatalog.Candidate!.Held.Should().Be(
            held.Reference,
            "the record is held under an identifier this item also states");
        namedWhileReading.Should().BeFalse(
            "reporting that is a read: the second catalog's identifier was not bound by looking at it");
        taken.Reference.Should().Be(held.Reference, "so taking it in converges rather than naming it twice");
        context.Holds(ExternalId.Of("alpha", "1")).Should().BeTrue("the control: materializing does bind it");
    }

    /// <summary>
    /// What a merge does to a local identity a caller is already holding: it resolves, and nothing else.
    /// </summary>
    /// <remarks>
    /// Two identities assigned separately merge onto the lower one, and the superseded reference resolves
    /// through <see cref="ICatalogIdentityReader.Canonical"/>. Library rows already written under the
    /// superseded reference are not moved — that is a store operation this milestone does not have — so the
    /// assertion below is the boundary rather than a feature.
    /// </remarks>
    [Test]
    public void ASupersededLocalIdentityResolvesToTheSurvivingOneAndNothingElseMoves()
    {
        var identity = new CatalogIdentity();
        var assign = (ICatalogIdentityAssignment)identity;
        var kind = Works.Id;
        var level = MediaLevelId.FromString("work");

        var first = assign.Identify(kind, level, [ExternalId.Of("alpha", "1")]);
        var second = assign.Identify(kind, level, [ExternalId.Of("beta", "tt2")]);
        var merged = assign.Identify(kind, level, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "tt2")]);

        using var assertions = new AssertionScope();
        second.Should().NotBe(first, "they were separate items until something said otherwise");
        merged.Should().Be(first, "the merge settles on the lower assignment, whichever order it is seen in");
        identity.Canonical(second).Should().Be(first, "so a caller holding the superseded reference resolves");
        identity.Canonical(first).Should().Be(first, "and one that was never superseded is returned unchanged");
        second.Should().NotBe(merged, "the superseded reference is still a different key, so anything stored "
            + "under it stays there until a store migration moves it");
    }

    /// <summary>
    /// An item and a group naming the same scheme and value are two different things, and the host must not
    /// answer with one reference for both.
    /// </summary>
    [Test]
    public void AnItemAndAGroupCarryingTheSameIdentifierDoNotCollide()
    {
        var identity = new CatalogIdentity();
        var assign = (ICatalogIdentityAssignment)identity;
        var kind = Works.Id;
        var same = ExternalId.Of("alpha", "1");

        var item = assign.Identify(kind, MediaLevelId.FromString("work"), [same]);
        var group = assign.Identify(kind, MediaLevelId.FromString("collection"), [same]);

        using var assertions = new AssertionScope();
        group.Should().NotBe(item);
        group.Id.Should().NotBe(item.Id, "an item's and a group's identifiers are different key spaces");
        identity.TryFind(kind, MediaLevelId.FromString("work"), same, out var round).Should().BeTrue();
        round.Should().Be(item);
    }

    /// <summary>
    /// A local identity is unique within its kind and no further, so two kinds hold the same number for
    /// different entities. A merge in one must leave the other alone.
    /// </summary>
    [Test]
    public void AMergeInOneKindLeavesTheSameNumberInAnotherKindAlone()
    {
        var identity = new CatalogIdentity();
        var assign = (ICatalogIdentityAssignment)identity;
        var level = MediaLevelId.FromString("work");
        var other = MediaKindId.FromString("other");

        var mine = assign.Identify(Works.Id, level, [ExternalId.Of("alpha", "1")]);
        var supersededHere = assign.Identify(Works.Id, level, [ExternalId.Of("beta", "2")]);
        assign.Identify(other, level, [ExternalId.Of("gamma", "9")]);
        var theirs = assign.Identify(other, level, [ExternalId.Of("delta", "8")]);

        assign.Identify(Works.Id, level, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]);

        using var assertions = new AssertionScope();
        theirs.Id.Should().Be(supersededHere.Id, "each kind numbers its own entities, so the collision is real");
        theirs.Kind.Should().NotBe(supersededHere.Kind);
        identity.Canonical(supersededHere).Should().Be(mine, "the merge happened where it was asked for");
        identity.Canonical(theirs).Should().Be(theirs, "and the same number in another kind was not part of it");
        identity.TryFind(other, level, ExternalId.Of("delta", "8"), out var round).Should().BeTrue();
        round.Should().Be(theirs, "so that kind's own assignment was not moved with the superseded one");
    }

    /// <summary>
    /// The same statement one level down: an equal number at another level of the same kind is a different
    /// entity. The allocator numbers per kind, so the collision is placed directly.
    /// </summary>
    [Test]
    public void AMergeInOneLevelLeavesTheSameNumberInAnotherLevelAlone()
    {
        var identity = new CatalogIdentity();
        var assign = (ICatalogIdentityAssignment)identity;
        var work = MediaLevelId.FromString("work");
        var collection = MediaLevelId.FromString("collection");
        var one = MediaItemId.FromInt64(1);
        var two = MediaItemId.FromInt64(2);

        var item = identity.Assign(Works.Id, work, ExternalId.Of("alpha", "1"), one);
        var group = identity.Assign(Works.Id, collection, ExternalId.Of("alpha", "1"), one);
        var supersededItem = identity.Assign(Works.Id, work, ExternalId.Of("beta", "2"), two);
        var groupToo = identity.Assign(Works.Id, collection, ExternalId.Of("beta", "2"), two);

        assign.Identify(Works.Id, work, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]);

        using var assertions = new AssertionScope();
        group.Id.Should().Be(item.Id, "the numbers collide across levels, which is what this rule is about");
        identity.Canonical(supersededItem).Should().Be(item, "the merge happened at the item level");
        identity.Canonical(groupToo).Should().Be(groupToo, "and not at the group level");
        identity.TryFind(Works.Id, collection, ExternalId.Of("beta", "2"), out var round).Should().BeTrue();
        round.Should().Be(groupToo, "the group's assignment was not moved with the item's");
    }

    /// <summary>Identity is host state, so reloading the extension that declares a kind does not reissue it.</summary>
    [Test]
    public async Task ARebuiltKindRuntimeKeepsTheIdentityTheHostAlreadyAssigned()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));
        var before = await context.TakeInAsync(ExternalId.Of("alpha", "1"));

        // What a reload does: the kind's runtime is derived again from the same declaration. Host state,
        // including everything already in the library, is not.
        var reloaded = context with
        {
            Kind = MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>(),
        };
        var after = await reloaded.TakeInAsync(ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        reloaded.Kind.Should().NotBeSameAs(context.Kind, "the runtime really was rebuilt");
        after.Reference.Should().Be(before.Reference);
    }

    [Test]
    public async Task RoutingSelectsTheCatalogerThatOwnsTheSchemeRatherThanTheFirstOneRegistered()
    {
        var alpha = new StubCataloger("alpha", Work("alpha", "1", "Alpha answer"));
        var beta = new StubCataloger("beta", Work("beta", "1", "Beta answer"));
        var context = CatalogContext.WithCatalogers(alpha, beta);

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("beta", "1"));

        using var assertions = new AssertionScope();
        fetch.Candidate!.Item.Title.Should().Be("Beta answer");
        alpha.Fetched.Should().BeEmpty("the scheme, not the registration order, decides who is asked");
        beta.Fetched.Should().Equal(ExternalId.Of("beta", "1"));
    }

    [Test]
    public async Task RoutingAndValidationUseTheSchemeCapturedAtRegistration()
    {
        var cataloger = new StubCataloger("alpha", Work("alpha", "1", "Arrival"));
        var context = CatalogContext.WithCatalogers(cataloger);

        cataloger.CatalogScheme = "changed-after-registration";
        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        fetch.Candidate!.CatalogId.Should().Be(ExternalId.Of("alpha", "1"));
    }

    [Test]
    public void ACallClosedOverAnotherItemTypeIsRefusedInsteadOfLookingLikeAMissingRecord()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var act = () => context.Dispatcher.FetchAsync<IMediaItem>(
            context.Kind,
            ExternalId.Of("alpha", "1"));

        act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("kind");
    }

    [Test]
    public void SearchingANonCanonicalSchemeIsRefusedBeforeRouting()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var act = () => context.Dispatcher.SearchAsync<Work>(
            context.Kind,
            "Alpha",
            new CatalogQuery("Arrival"));

        act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("scheme");
    }

    [Test]
    public async Task ACatalogSearchMayResolveAnIdentifierFromAnotherCatalog()
    {
        var cataloger = new StubCataloger("alpha", Work("alpha", "1", "Arrival"));
        var context = CatalogContext.WithCatalogers(cataloger);
        var query = new CatalogQuery(string.Empty, ExternalId.Of("beta", "9"));

        await context.Dispatcher.SearchAsync<Work>(context.Kind, "alpha", query);

        cataloger.Searched.Should().Equal(query);
    }

    [Test]
    public void AReferenceInASchemeNoInstalledCatalogerOwnsIsRefused()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var act = () => context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("gamma", "1"));

        act.Should().ThrowAsync<ArronixException>()
            .Where(error => error.ErrorCode == CoreErrorCode.CatalogSchemeUnowned)
            .WithMessage("*gamma*");
    }

    /// <summary>
    /// Routing compares schemes ordinally, so a reference whose scheme is not in canonical form is rejected
    /// rather than quietly matching nothing.
    /// </summary>
    [Test]
    public void AReferenceWhoseSchemeIsNotInCanonicalFormIsRejected()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var act = () => context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("Alpha", "1"));

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*canonical catalog scheme*");
    }

    [Test]
    public void AnItemStatingNoIdentifierInItsCatalogersOwnSchemeIsRefused()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("beta", "1", "Arrival")));

        var act = () => context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        act.Should().ThrowAsync<ArronixException>()
            .Where(error => error.ErrorCode == CoreErrorCode.CatalogIdentityInvalid);
    }

    [Test]
    public void AnItemStatingTwoIdentifiersInItsCatalogersOwnSchemeIsRefused()
    {
        var ambiguous = new Work
        {
            Title = "Arrival",
            ExternalIds = ExternalIdSet.Of(ExternalId.Of("alpha", "1"), ExternalId.Of("alpha", "2")),
        };
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", ambiguous));

        var act = () => context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        act.Should().ThrowAsync<ArronixException>()
            .Where(error => error.ErrorCode == CoreErrorCode.CatalogIdentityInvalid)
            .WithMessage("*exactly one*");
    }

    [Test]
    public async Task ACuratedListIsResolvedThroughTheCatalogersThatOwnItsReferences()
    {
        var alpha = new StubCataloger("alpha", Work("alpha", "1", "First"));
        var beta = new StubCataloger("beta", Work("beta", "9", "Second"));
        var context = CatalogContext.WithCatalogers(alpha, beta);
        var list = new CuratedListFetch<Work>(
            [
                new CuratedReference(ExternalId.Of("alpha", "1"), CuratedEntryId.Of("row-1")),
                new CuratedReference(ExternalId.Of("beta", "9")),
            ],
            AnyFailure: false,
            Warnings: []);

        var resolved = await context.Dispatcher.ResolveAsync(context.Kind, list);
        var namedWhileReading = context.Holds(ExternalId.Of("alpha", "1"));
        var taken = resolved.Candidates
            .Select(candidate => context.Dispatcher.Materialize(context.Kind, candidate))
            .ToArray();

        using var assertions = new AssertionScope();
        resolved.Candidates.Select(entry => entry.Item.Title).Should().Equal("First", "Second");
        resolved.Candidates.Select(entry => entry.CatalogId)
            .Should().Equal(ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "9"));
        resolved.Candidates.Select(entry => entry.CuratedEntryId)
            .Should().Equal(CuratedEntryId.Of("row-1"), null);
        alpha.Fetched.Should().Equal([ExternalId.Of("alpha", "1")], "each reference is fetched from its own owner");
        beta.Fetched.Should().Equal(ExternalId.Of("beta", "9"));
        namedWhileReading.Should().BeFalse("resolving a curated list is a read");
        context.Holds(ExternalId.Of("alpha", "1")).Should().BeTrue("the control: materializing does bind it");
        taken.Select(entry => entry.Reference).Distinct().Should().HaveCount(2);
        taken.Select(entry => entry.CuratedEntryId)
            .Should().Equal([CuratedEntryId.Of("row-1"), null], "the curator's entry identifier survives");
    }

    [Test]
    public void ACuratedListNamingAnUnownedSchemeIsRefusedBeforeAnythingIsFetched()
    {
        var alpha = new StubCataloger("alpha", Work("alpha", "1", "First"));
        var context = CatalogContext.WithCatalogers(alpha);
        var list = new CuratedListFetch<Work>(
            [
                new CuratedReference(ExternalId.Of("alpha", "1")),
                new CuratedReference(ExternalId.Of("gamma", "2")),
            ],
            AnyFailure: false,
            Warnings: []);

        var act = () => context.Dispatcher.ResolveAsync(context.Kind, list);

        act.Should().ThrowAsync<ArronixException>()
            .Where(error => error.ErrorCode == CoreErrorCode.CatalogSchemeUnowned);
        alpha.Fetched.Should().BeEmpty("ownership is settled for the whole list before any of it is fetched");
    }

    /// <summary>
    /// The catalog says it does not hold the record. That is a fact about the record, and it is not the
    /// same sentence as any of the three ways a call can fail to produce one.
    /// </summary>
    [Test]
    public async Task AnAuthorityAnsweringThatItDoesNotHoldTheRecordIsNotAFailureToAnswer()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", answer: null));

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        fetch.Outcome.Should().Be(CatalogFetchOutcome.NotHeld);
        fetch.Candidate.Should().BeNull();
        fetch.Reason.Should().Contain("alpha");
        context.Status.Find(context.Definitions[0].Id).Should().BeNull("the authority answered, so it succeeded");
    }

    /// <summary>
    /// An installed authority that is backed off is not a missing installation, and the record's absence
    /// from the answer says nothing about the record.
    /// </summary>
    [Test]
    public async Task ABackedOffAuthorityIsUnavailableRatherThanAnUnownedScheme()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var context = CatalogContext.WithCatalogers(clock, new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        // Past the startup window, then past the grace the ladder gives a first failure, so the second one
        // genuinely takes the provider out of service rather than only being noted.
        clock.Advance(TimeSpan.FromHours(1));
        context.Status.RecordFailure(context.Definitions[0].Id);
        clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));
        context.Status.RecordFailure(context.Definitions[0].Id);

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var searched = await context.Dispatcher.SearchAsync<Work>(context.Kind, "alpha", new CatalogQuery("Arrival"));

        using var assertions = new AssertionScope();
        context.Status.IsAvailable(context.Definitions[0].Id).Should().BeFalse("the fixture really did back it off");
        fetch.Outcome.Should().Be(CatalogFetchOutcome.AuthorityUnavailable);
        fetch.Reason.Should().Contain("out of service").And.Contain("back-off");
        searched.Candidates.Should().BeEmpty();
        searched.IsPartialResult.Should().BeTrue("an empty answer and an unanswered one are not the same page");
        searched.Warnings.Should().ContainSingle().Which.Should().Contain("out of service");
    }

    /// <summary>An authority that throws did not answer, and the failure climbs its own back-off ladder.</summary>
    [Test]
    public async Task AFailingAuthorityDoesNotAnswerAndIsRecordedAgainstItsOwnDefinition()
    {
        var cataloger = new StubCataloger("alpha", Work("alpha", "1", "Arrival")) { Throws = true };
        var context = CatalogContext.WithCatalogers(cataloger);

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var searched = await context.Dispatcher.SearchAsync<Work>(context.Kind, "alpha", new CatalogQuery("Arrival"));

        using var assertions = new AssertionScope();
        fetch.Outcome.Should().Be(
            CatalogFetchOutcome.NotAnswered,
            "a transport failure must never be readable as the catalog saying the record is gone");
        fetch.Reason.Should().Contain("catalog-1");
        searched.IsPartialResult.Should().BeTrue();
        context.Status.Find(context.Definitions[0].Id)!.Value.EscalationLevel.Should().Be(
            2,
            "both calls climbed the ladder the catalog path previously read and never fed");
    }

    /// <summary>
    /// Absence is fail-closed. One authority answering "not mine" while another never answered does not
    /// establish that the record is gone, so the aggregate is the unanswered one.
    /// </summary>
    /// <remarks>
    /// The witness is deterministic in both directions: the same two authorities, in the same order, with
    /// only the failing one's behaviour changed. Ordering is fixed by priority and definition identifier, so
    /// the answering authority is asked first in both cases and the outcome turns on the second alone.
    /// </remarks>
    [TestCase(true, CatalogFetchOutcome.NotAnswered, TestName = "OneAuthoritySilentLeavesAbsenceUnestablished")]
    [TestCase(false, CatalogFetchOutcome.NotHeld, TestName = "EveryAuthorityAnsweringEstablishesAbsence")]
    public async Task AbsenceIsClaimedOnlyWhenEveryAuthorityAnswered(bool secondFails, CatalogFetchOutcome expected)
    {
        var answering = new StubCataloger("alpha", answer: null);
        var second = new StubCataloger("alpha", answer: null) { Throws = secondFails };
        var context = CatalogContext.WithCatalogers(answering, second);

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        answering.Fetched.Should().Equal([ExternalId.Of("alpha", "1")], "the first authority was asked either way");
        second.Fetched.Should().Equal([ExternalId.Of("alpha", "1")], "and so was the second");
        fetch.Outcome.Should().Be(expected);
        fetch.Candidate.Should().BeNull();
    }

    /// <summary>
    /// A provider's own cancellation is a failed answer. Only the caller's token ends the dispatch.
    /// </summary>
    /// <remarks>
    /// A provider raising <see cref="OperationCanceledException"/> from its own timeout is indistinguishable
    /// by type from the caller withdrawing, and the two must not be treated alike: the first is one
    /// authority failing, the second is the whole call being abandoned. The token, not the exception type,
    /// is what says which.
    /// </remarks>
    [Test]
    public async Task AProviderRaisedCancellationIsAFailedAnswerRatherThanTheCallersOwn()
    {
        var context = CatalogContext.WithCatalogers(
            new StubCataloger("alpha", answer: null) { CancelsItself = true });

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        fetch.Outcome.Should().Be(CatalogFetchOutcome.NotAnswered);
        fetch.Reason.Should().Contain("catalog-1");
        context.Status.Find(context.Definitions[0].Id).Should().NotBeNull("a timeout is evidence about the provider");
    }

    /// <summary>The caller's own cancellation still ends the call, which is the control for the rule above.</summary>
    [Test]
    public async Task TheCallersOwnCancellationEndsTheDispatch()
    {
        var context = CatalogContext.WithCatalogers(
            new StubCataloger("alpha", answer: null) { CancelsItself = true });
        using var withdrawn = new CancellationTokenSource();
        await withdrawn.CancelAsync();

        var act = () => context.Dispatcher.FetchAsync<Work>(
            context.Kind,
            ExternalId.Of("alpha", "1"),
            withdrawn.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// The boundary contains a cataloger's own failure and nothing beyond it. A process that is no longer
    /// sound propagates, wrapped or not.
    /// </summary>
    /// <remarks>
    /// The pairing is the control: the same call, the same stub, one ordinary failure contained and one
    /// unsound-process failure not — so containment here is the deliberate rule rather than a catch-all
    /// that happens to have been written narrowly.
    /// </remarks>
    [Test]
    public async Task AFatalFailureIsNotContainedAtTheCatalogBoundary()
    {
        var ordinary = CatalogContext.WithCatalogers(
            new StubCataloger("alpha", answer: null) { Throws = true });
        var unsound = CatalogContext.WithCatalogers(
            new StubCataloger("alpha", answer: null)
            {
                // Wrapped, because an exhausted heap routinely arrives inside a type initializer.
                Fails = new TypeInitializationException("Catalog", new OutOfMemoryException()),
            });

        var contained = await ordinary.Dispatcher.FetchAsync<Work>(ordinary.Kind, ExternalId.Of("alpha", "1"));
        var act = () => unsound.Dispatcher.FetchAsync<Work>(unsound.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        contained.Outcome.Should().Be(CatalogFetchOutcome.NotAnswered, "an ordinary failure is one authority's");
        await act.Should().ThrowAsync<TypeInitializationException>()
            .WithInnerException<TypeInitializationException, OutOfMemoryException>();
    }

    /// <summary>
    /// An owner that is backed off is still one of the scheme's authorities, so absence cannot be settled
    /// while it is missing from the answer.
    /// </summary>
    /// <remarks>
    /// Exactly two authorities, both owning the scheme, both holding nothing. The only difference between
    /// the cases is whether the second is in service — so the outcome turns on that alone. Filtering the
    /// backed-off owner away, as the dispatcher used to, would report a confident absence assembled from
    /// half the authorities.
    /// </remarks>
    [TestCase(true, CatalogFetchOutcome.NotAnswered, TestName = "ABackedOffOwnerLeavesAbsenceUnestablished")]
    [TestCase(false, CatalogFetchOutcome.NotHeld, TestName = "EveryOwnerInServiceEstablishesAbsence")]
    public async Task AFetchSettlesAbsenceOnlyWhenEveryOwnerOfTheSchemeWasAsked(
        bool secondBackedOff,
        CatalogFetchOutcome expected)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var asked = new StubCataloger("alpha", answer: null);
        var context = CatalogContext.WithCatalogers(clock, asked, new StubCataloger("alpha", answer: null));

        if (secondBackedOff)
        {
            context.BackOff(clock, ordinal: 1);
        }

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        asked.Fetched.Should().Equal(
            [ExternalId.Of("alpha", "1")],
            "the authority in service is asked in both cases");
        fetch.Outcome.Should().Be(expected);
        fetch.Candidate.Should().BeNull();

        if (secondBackedOff)
        {
            fetch.Reason.Should().Contain("catalog-2").And.Contain("out of service");
        }
    }

    /// <summary>A record found by an authority in service is a record, whoever else was not asked.</summary>
    [Test]
    public async Task ABackedOffOwnerDoesNotStopAnAuthorityInServiceAnsweringWithTheRecord()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var context = CatalogContext.WithCatalogers(
            clock,
            new StubCataloger("alpha", Work("alpha", "1", "Arrival")),
            new StubCataloger("alpha", answer: null));
        context.BackOff(clock, ordinal: 1);

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        fetch.Outcome.Should().Be(CatalogFetchOutcome.Found);
        fetch.Candidate!.Item.Title.Should().Be("Arrival");
    }

    /// <summary>A search whose scheme has a backed-off owner is partial, and says which owner.</summary>
    [Test]
    public async Task ASearchNamesTheBackedOffOwnerThatWasNotAsked()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var answering = new StubCataloger("alpha", Work("alpha", "1", "Arrival"));
        var absent = new StubCataloger("alpha", Work("alpha", "2", "Story of Your Life"));
        var context = CatalogContext.WithCatalogers(clock, answering, absent);
        context.BackOff(clock, ordinal: 1);

        var searched = await context.Dispatcher.SearchAsync<Work>(context.Kind, "alpha", new CatalogQuery("Arrival"));

        using var assertions = new AssertionScope();
        searched.Candidates.Should().ContainSingle().Which.CatalogId.Should().Be(ExternalId.Of("alpha", "1"));
        absent.Searched.Should().BeEmpty("an owner out of service is not asked");
        searched.IsPartialResult.Should().BeTrue("so the page is assembled from fewer authorities than own it");
        searched.Warnings.Should().ContainSingle().Which.Should()
            .Contain("catalog-2").And.Contain("out of service");
    }

    /// <summary>A caller that has already withdrawn is not served, and no authority is asked.</summary>
    [Test]
    public async Task AnAlreadyWithdrawnCallerIsRefusedBeforeAnyAuthorityIsAsked()
    {
        var cataloger = new StubCataloger("alpha", Work("alpha", "1", "Arrival"));
        var context = CatalogContext.WithCatalogers(cataloger);
        using var withdrawn = new CancellationTokenSource();
        await withdrawn.CancelAsync();

        var fetching = () => context.Dispatcher.FetchAsync<Work>(
            context.Kind,
            ExternalId.Of("alpha", "1"),
            withdrawn.Token);
        var searching = () => context.Dispatcher.SearchAsync<Work>(
            context.Kind,
            "alpha",
            new CatalogQuery("Arrival"),
            withdrawn.Token);

        using var assertions = new AssertionScope();
        await fetching.Should().ThrowAsync<OperationCanceledException>();
        await searching.Should().ThrowAsync<OperationCanceledException>();
        cataloger.Fetched.Should().BeEmpty();
        cataloger.Searched.Should().BeEmpty();
    }

    /// <summary>
    /// A withdrawn caller is answered with the withdrawal, not with a diagnosis of the installation.
    /// </summary>
    /// <remarks>
    /// What the entry check exists for: the per-authority check never runs in either case, so without it a
    /// withdrawn caller receives an answer about the deployment to a question nobody is waiting on.
    /// </remarks>
    [Test]
    public async Task AWithdrawnCallerIsNotToldAboutTheInstallationInstead()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var context = CatalogContext.WithCatalogers(clock, new StubCataloger("alpha", answer: null));
        using var withdrawn = new CancellationTokenSource();
        await withdrawn.CancelAsync();

        var unowned = () => context.Dispatcher.FetchAsync<Work>(
            context.Kind,
            ExternalId.Of("gamma", "1"),
            withdrawn.Token);
        var curated = () => context.Dispatcher.ResolveAsync(
            context.Kind,
            new CuratedListFetch<Work>([new CuratedReference(ExternalId.Of("gamma", "1"))], false, []),
            withdrawn.Token);

        context.BackOff(clock, ordinal: 0);
        var allBackedOff = () => context.Dispatcher.FetchAsync<Work>(
            context.Kind,
            ExternalId.Of("alpha", "1"),
            withdrawn.Token);

        using var assertions = new AssertionScope();
        await unowned.Should().ThrowAsync<OperationCanceledException>();
        await curated.Should().ThrowAsync<OperationCanceledException>();
        await allBackedOff.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A cataloger that ignores the token it was handed cannot turn a withdrawn call into a record or into
    /// an absence.
    /// </summary>
    /// <remarks>
    /// Both halves matter: an ignored cancellation must become neither a record nor an absence, and the
    /// second is the more dangerous, because absence is what a refresh would later make durable.
    /// </remarks>
    [TestCase(true, TestName = "AnIgnoredCancellationDoesNotBecomeARecord")]
    [TestCase(false, TestName = "AnIgnoredCancellationDoesNotBecomeAnAbsence")]
    public async Task ACatalogerIgnoringCancellationYieldsNeitherSuccessNorAbsence(bool holdsTheRecord)
    {
        using var caller = new CancellationTokenSource();
        var cataloger = new StubCataloger("alpha", holdsTheRecord ? Work("alpha", "1", "Arrival") : null)
        {
            WithdrawsTheCaller = caller,
        };
        var context = CatalogContext.WithCatalogers(cataloger);

        var act = () => context.Dispatcher.FetchAsync<Work>(
            context.Kind,
            ExternalId.Of("alpha", "1"),
            caller.Token);

        using var assertions = new AssertionScope();
        await act.Should().ThrowAsync<OperationCanceledException>();
        cataloger.Fetched.Should().Equal(
            [ExternalId.Of("alpha", "1")],
            "the cataloger really was called, and really did answer past the withdrawal");
    }

    /// <summary>A caller withdrawing part-way through stops the authorities that have not been asked yet.</summary>
    [Test]
    public async Task ACallerWithdrawingBetweenAuthoritiesStopsTheOnesNotYetAsked()
    {
        using var caller = new CancellationTokenSource();
        var first = new StubCataloger("alpha", answer: null) { WithdrawsTheCaller = caller };
        var second = new StubCataloger("alpha", Work("alpha", "1", "Arrival"));
        var context = CatalogContext.WithCatalogers(first, second);

        var act = () => context.Dispatcher.FetchAsync<Work>(
            context.Kind,
            ExternalId.Of("alpha", "1"),
            caller.Token);

        using var assertions = new AssertionScope();
        await act.Should().ThrowAsync<OperationCanceledException>();
        first.Fetched.Should().HaveCount(1);
        second.Fetched.Should().BeEmpty("the caller withdrew before the second authority was reached");
    }

    /// <summary>
    /// An owner lands in exactly one of the two sets, whatever a concurrent status change does.
    /// </summary>
    /// <remarks>
    /// The clock moves on every reading, so a back-off that expires between two of them answers differently
    /// each time. Asking twice would then place this owner in neither set, and the answer would name no
    /// authority at all while claiming every one of them is out of service.
    /// </remarks>
    [Test]
    public async Task AnOwnerIsPartitionedOnOneReadingOfItsAvailability()
    {
        var clock = new SteppingClock(DateTimeOffset.UnixEpoch);
        var context = CatalogContext.WithCatalogers(clock, new StubCataloger("alpha", answer: null));

        clock.Advance(TimeSpan.FromHours(1));
        context.Status.RecordFailure(context.Definitions[0].Id);
        clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));
        var till = context.Status.RecordFailure(context.Definitions[0].Id).DisabledTill!.Value;

        // Parked one tick short of the back-off expiring, and moving past it on the very next reading.
        clock.MoveTo(till - TimeSpan.FromSeconds(1));
        clock.Step = TimeSpan.FromSeconds(2);

        var fetch = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        fetch.Outcome.Should().Be(CatalogFetchOutcome.AuthorityUnavailable);
        fetch.Reason.Should().Contain(
            "catalog-1",
            "the owner was read once and placed, rather than read twice and lost between the answers");
    }

    /// <summary>
    /// A search makes its own cancellation checks, and each is asserted where only that check can fire.
    /// </summary>
    /// <remarks>
    /// A search does not early-return when every owner is backed off — the loop simply does not run — so its
    /// entry check is the only thing between a withdrawn caller and a partial answer. The unowned scheme is
    /// refused before the loop for the same reason.
    /// </remarks>
    [Test]
    public async Task ASearchRefusesAWithdrawnCallerWhereNoOtherCheckWould()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var context = CatalogContext.WithCatalogers(clock, new StubCataloger("alpha", answer: null));
        using var withdrawn = new CancellationTokenSource();
        await withdrawn.CancelAsync();

        var unowned = () => context.Dispatcher.SearchAsync<Work>(
            context.Kind,
            "gamma",
            new CatalogQuery("Arrival"),
            withdrawn.Token);

        context.BackOff(clock, ordinal: 0);
        var allBackedOff = () => context.Dispatcher.SearchAsync<Work>(
            context.Kind,
            "alpha",
            new CatalogQuery("Arrival"),
            withdrawn.Token);

        using var assertions = new AssertionScope();
        await unowned.Should().ThrowAsync<OperationCanceledException>();
        await allBackedOff.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// An answer produced after the caller withdrew is not even read, let alone put on a page.
    /// </summary>
    /// <remarks>
    /// Copying a cataloger's collection is work the host does for the caller. Checking before the copy is
    /// what makes that work conditional on someone still waiting for it.
    /// </remarks>
    [Test]
    public async Task ASearchDoesNotEvenReadAnAnswerProducedAfterTheCallerWithdrew()
    {
        using var caller = new CancellationTokenSource();
        var cataloger = new StubCataloger("alpha", Work("alpha", "1", "Arrival")) { WithdrawsTheCaller = caller };
        var context = CatalogContext.WithCatalogers(cataloger);

        var act = () => context.Dispatcher.SearchAsync<Work>(
            context.Kind,
            "alpha",
            new CatalogQuery("Arrival"),
            caller.Token);

        using var assertions = new AssertionScope();
        await act.Should().ThrowAsync<OperationCanceledException>();
        cataloger.Searched.Should().HaveCount(1, "it really did answer past the withdrawal");
        cataloger.Results!.WasRead.Should().BeFalse("and what it answered with was never read");
        context.Status.Find(context.Definitions[0].Id).Should().BeNull(
            "nor was the discarded answer recorded as a contribution");
    }

    /// <summary>
    /// A withdrawal that lands while the answer is being copied still discards it.
    /// </summary>
    /// <remarks>
    /// The window the second check covers: the caller was waiting when the copy began and had stopped by the
    /// time it finished, which is a race a single check before the copy cannot close.
    /// </remarks>
    [Test]
    public async Task ASearchDiscardsAnAnswerTheCallerStoppedWaitingForWhileItWasCopied()
    {
        using var caller = new CancellationTokenSource();
        var cataloger = new StubCataloger("alpha", Work("alpha", "1", "Arrival"))
        {
            WithdrawsTheCallerWhileRead = caller,
        };
        var context = CatalogContext.WithCatalogers(cataloger);

        var act = () => context.Dispatcher.SearchAsync<Work>(
            context.Kind,
            "alpha",
            new CatalogQuery("Arrival"),
            caller.Token);

        using var assertions = new AssertionScope();
        await act.Should().ThrowAsync<OperationCanceledException>();
        cataloger.Results!.WasRead.Should().BeTrue("the copy began, which is what puts the race in reach");
        context.Status.Find(context.Definitions[0].Id).Should().BeNull(
            "and the answer contributed nothing, not even a recorded success");
    }

    /// <summary>
    /// A cataloger whose results throw as they are read is that authority failing, not the search failing.
    /// </summary>
    /// <remarks>
    /// Calling a cataloger and reading what it returned are both its own code, so both are contained the
    /// same way: the authority is recorded failed, named in a warning, and the authorities that did answer
    /// still make the page. The three cases are the same tear raised three ways — an ordinary failure, a
    /// cancellation the caller did not ask for, and a process that is no longer sound.
    /// </remarks>
    [Test]
    public async Task ASearchContainsACatalogerWhoseResultsTearWhileTheyAreRead()
    {
        var ordinary = Tearing(new InvalidOperationException("The page tore."));
        var providerCancellation = Tearing(new OperationCanceledException("The catalog page timed out."));
        var unsound = Tearing(new TypeInitializationException("Catalog", new OutOfMemoryException()));

        var searchingOrdinary = await ordinary.Dispatcher.SearchAsync<Work>(
            ordinary.Kind, "alpha", new CatalogQuery("Arrival"));
        var searchingProviderCancellation = await providerCancellation.Dispatcher.SearchAsync<Work>(
            providerCancellation.Kind, "alpha", new CatalogQuery("Arrival"));
        var searchingUnsound = () => unsound.Dispatcher.SearchAsync<Work>(
            unsound.Kind, "alpha", new CatalogQuery("Arrival"));

        using var assertions = new AssertionScope();

        foreach (var (searched, tear) in new[]
        {
            (searchingOrdinary, "page tore"),
            (searchingProviderCancellation, "timed out"),
        })
        {
            searched.Candidates.Should().ContainSingle(
                "the authority that answered still makes the page")
                .Which.CatalogId.Should().Be(ExternalId.Of("alpha", "2"));
            searched.IsPartialResult.Should().BeTrue();
            searched.Warnings.Should().ContainSingle().Which.Should().Contain("catalog-1").And.Contain(tear);
        }

        ordinary.Status.Find(ordinary.Definitions[0].Id).Should().NotBeNull("the tear is the authority's");
        ordinary.Status.Find(ordinary.Definitions[1].Id).Should().BeNull("and not the one that answered's");
        await searchingUnsound.Should().ThrowAsync<TypeInitializationException>(
            "a process that is no longer sound is not contained, wherever it is raised");
    }

    /// <summary>Two authorities: the first tears as its results are read, the second answers.</summary>
    private static CatalogContext Tearing(Exception tear) => CatalogContext.WithCatalogers(
        new StubCataloger("alpha", Work("alpha", "1", "Arrival")) { FailsWhileRead = tear },
        new StubCataloger("alpha", Work("alpha", "2", "Story of Your Life")));

    /// <summary>
    /// A caller withdrawing alongside a failure stops the next authority, which no other check would.
    /// </summary>
    /// <remarks>
    /// The authority that withdrew the caller also failed, so nothing it returned was checked. Only the
    /// check at the top of the next iteration stands between the withdrawal and the next authority.
    /// </remarks>
    [TestCase(true, TestName = "ASearchStopsAtTheNextAuthorityAfterAWithdrawalWithAFailure")]
    [TestCase(false, TestName = "AFetchStopsAtTheNextAuthorityAfterAWithdrawalWithAFailure")]
    public async Task ADispatchStopsAtTheNextAuthorityWhenAWithdrawalArrivesWithAFailure(bool searching)
    {
        using var caller = new CancellationTokenSource();
        var first = new StubCataloger("alpha", answer: null) { WithdrawsTheCaller = caller, Throws = true };
        var second = new StubCataloger("alpha", Work("alpha", "2", "Story of Your Life"));
        var context = CatalogContext.WithCatalogers(first, second);

        Func<Task> act = searching
            ? () => context.Dispatcher.SearchAsync<Work>(
                context.Kind,
                "alpha",
                new CatalogQuery("Arrival"),
                caller.Token)
            : () => context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"), caller.Token);

        using var assertions = new AssertionScope();
        await act.Should().ThrowAsync<OperationCanceledException>();
        second.Searched.Should().BeEmpty("the caller withdrew before the second authority was reached");
        second.Fetched.Should().BeEmpty();
    }

    /// <summary>A caller withdrawing part-way through a search stops the authorities not yet asked.</summary>
    [Test]
    public async Task ASearchStopsAtTheAuthorityWhereTheCallerWithdrew()
    {
        using var caller = new CancellationTokenSource();
        var first = new StubCataloger("alpha", Work("alpha", "1", "Arrival")) { WithdrawsTheCaller = caller };
        var second = new StubCataloger("alpha", Work("alpha", "2", "Story of Your Life"));
        var context = CatalogContext.WithCatalogers(first, second);

        var act = () => context.Dispatcher.SearchAsync<Work>(
            context.Kind,
            "alpha",
            new CatalogQuery("Arrival"),
            caller.Token);

        using var assertions = new AssertionScope();
        await act.Should().ThrowAsync<OperationCanceledException>();
        first.Searched.Should().HaveCount(1);
        second.Searched.Should().BeEmpty("the caller withdrew before the second authority was reached");
    }

    /// <summary>A catalog call that succeeds clears the back-off the same way every other family's does.</summary>
    [Test]
    public async Task AnAnsweringAuthorityClearsItsRecordedFailure()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));
        context.Status.RecordFailure(context.Definitions[0].Id);

        await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        context.Status.Find(context.Definitions[0].Id).Should().BeNull();
    }

    /// <summary>
    /// One authority failing and another answering is a partial result, not a silent one.
    /// </summary>
    [Test]
    public async Task ASearchNamesTheAuthorityThatDidNotContribute()
    {
        var failing = new StubCataloger("alpha", Work("alpha", "1", "Arrival")) { Throws = true };
        var context = CatalogContext.WithCatalogers(
            failing,
            new StubCataloger("alpha", Work("alpha", "2", "Story of Your Life")));

        var searched = await context.Dispatcher.SearchAsync<Work>(context.Kind, "alpha", new CatalogQuery("Arrival"));

        using var assertions = new AssertionScope();
        searched.Candidates.Should().ContainSingle().Which.CatalogId.Should().Be(ExternalId.Of("alpha", "2"));
        searched.IsPartialResult.Should().BeTrue();
        searched.Warnings.Should().ContainSingle().Which.Should().Contain("catalog-1");
        context.Status.Find(context.Definitions[1].Id).Should().BeNull("the authority that answered is unaffected");
    }

    /// <summary>The level the fixture kind's items sit at.</summary>
    private static MediaLevelId Level => MediaLevelId.FromString("work");

    private static Work Work(string scheme, string value, string title, params ExternalId[] also) => new()
    {
        Title = title,
        ExternalIds = ExternalIdSet.From([ExternalId.Of(scheme, value), .. also]),
    };

    /// <summary>A registry, definitions and host identity state wired the way the host wires them.</summary>
    private sealed record CatalogContext(
        IMediaTypeRuntime Kind,
        CatalogDispatcher Dispatcher,
        CatalogIdentity Identity,
        ProviderStatusStore Status,
        IReadOnlyList<ProviderDefinition> Definitions)
    {
        internal static CatalogContext WithCatalogers(params StubCataloger[] catalogers)
            => WithCatalogers(TimeProvider.System, catalogers);

        internal static CatalogContext WithCatalogers(TimeProvider clock, params StubCataloger[] catalogers)
        {
            var providers = new ProviderRegistry();
            var definitions = new ProviderDefinitionStore(providers, new UnusedBus(), TimeProvider.System);
            var status = new ProviderStatusStore(clock);
            var sessions = new ProviderSessionStore(TimeProvider.System);
            var tests = new ProviderTestService(providers, definitions, sessions, status);

            var local = 0;
            var configured = new List<ProviderDefinition>();

            foreach (var cataloger in catalogers)
            {
                var name = $"catalog-{++local}";
                var provider = providers.Register(
                    PluginId.FromString($"example.{cataloger.CatalogScheme}"),
                    ProviderFamily.Cataloger,
                    new ProviderDescriptor { LocalId = name, Name = name, Settings = [] },
                    cataloger,
                    typeof(Work));

                configured.Add(definitions.AddAsync(new ProviderDefinition
                {
                    Id = 0,
                    Provider = provider,
                    Family = ProviderFamily.Cataloger,
                    Name = name,
                    Settings = new Dictionary<string, string>(StringComparer.Ordinal),
                }).GetAwaiter().GetResult());
            }

            var identity = new CatalogIdentity();

            return new CatalogContext(
                MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>(),
                new CatalogDispatcher(providers, definitions, status, tests, identity),
                identity,
                status,
                configured);
        }

        /// <summary>Fetches one record and materializes it, which is what adding it to the library does.</summary>
        internal async Task<MaterializedItem<Work>> TakeInAsync(ExternalId catalogId)
        {
            var fetch = await Dispatcher.FetchAsync<Work>(Kind, catalogId);

            fetch.Outcome.Should().Be(CatalogFetchOutcome.Found, "the fixture's cataloger holds the record");
            return Dispatcher.Materialize(Kind, fetch.Candidate!);
        }

        /// <summary>
        /// Takes one configured authority out of service, past both grace windows so the ladder bites.
        /// </summary>
        /// <param name="clock">The fixture's clock, which must be the one the status store was built on.</param>
        /// <param name="ordinal">Which configured definition, in registration order.</param>
        internal void BackOff(FakeTimeProvider clock, int ordinal)
        {
            clock.Advance(TimeSpan.FromHours(1));
            Status.RecordFailure(Definitions[ordinal].Id);
            clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));
            Status.RecordFailure(Definitions[ordinal].Id);
            Status.IsAvailable(Definitions[ordinal].Id).Should().BeFalse(
                "the fixture must really have taken it out of service for the rule to mean anything");
        }

        /// <summary>Whether the platform holds anything under one catalog identifier.</summary>
        /// <remarks>
        /// The ordinary read a caller would make. Asserting no-allocation through it, rather than through a
        /// counter put on identity state for the purpose, keeps the production surface free of a member that
        /// exists only to be tested.
        /// </remarks>
        internal bool Holds(ExternalId catalogId) =>
            Identity.TryFind(Kind.Kind, Level, catalogId, out _);
    }

    /// <summary>
    /// A cataloger that answers every request with one record and remembers what it was asked. A null
    /// answer is the catalog saying it holds no such record; <see cref="Throws"/> is the catalog failing to
    /// answer at all, which is a different sentence.
    /// </summary>
    private sealed class StubCataloger(string scheme, Work? answer) : ICataloger<Work>
    {
        private readonly List<ExternalId> _fetched = [];
        private readonly List<CatalogQuery> _searched = [];

        internal IReadOnlyList<ExternalId> Fetched => _fetched;

        internal IReadOnlyList<CatalogQuery> Searched => _searched;

        internal bool Throws { get; init; }

        /// <summary>Raises cancellation the way a provider's own request timeout does.</summary>
        internal bool CancelsItself { get; init; }

        /// <summary>An exact failure to raise, when the test is about which failures are contained.</summary>
        internal Exception? Fails { get; init; }

        /// <summary>
        /// A caller to withdraw while answering, standing in for a cataloger that ignores its token.
        /// </summary>
        internal CancellationTokenSource? WithdrawsTheCaller { get; init; }

        /// <summary>A caller to withdraw as the answer is read, rather than as it is produced.</summary>
        internal CancellationTokenSource? WithdrawsTheCallerWhileRead { get; init; }

        /// <summary>A failure to raise as the answer is read, rather than as it is produced.</summary>
        internal Exception? FailsWhileRead { get; init; }

        /// <summary>The list this cataloger last answered a search with.</summary>
        internal WatchedResults? Results { get; private set; }

        public string CatalogScheme { get; internal set; } = scheme;

        public CatalogerCapabilities Capabilities =>
            CatalogerCapabilities.Search | CatalogerCapabilities.IdentifierRedirects;

        public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => [];

        public Task<IReadOnlyList<Work>> SearchAsync(
            ProviderInvocation invocation,
            CatalogQuery query,
            CancellationToken cancellationToken = default)
        {
            _searched.Add(query);
            Misbehave();
            Results = new WatchedResults(answer, WithdrawsTheCallerWhileRead, FailsWhileRead);

            return Task.FromResult<IReadOnlyList<Work>>(Results);
        }

        public Task<Work?> GetAsync(
            ProviderInvocation invocation,
            ExternalId id,
            CancellationToken cancellationToken = default)
        {
            _fetched.Add(id);
            Misbehave();

            return Task.FromResult(answer);
        }

        /// <summary>Fails the way this stub was configured to, before it answers anything.</summary>
        private void Misbehave()
        {
            // Withdrawal first: a cataloger can ignore its token and then fail, and which of the two the
            // dispatcher sees must not depend on the order this fixture raises them in.
            WithdrawsTheCaller?.Cancel();

            if (Fails is { } exact)
            {
                throw exact;
            }

            if (Throws)
            {
                throw new InvalidOperationException("The catalog is unreachable.");
            }

            if (CancelsItself)
            {
                // What a provider's own request timeout raises. The caller's token is untouched.
                throw new OperationCanceledException("The catalog request timed out.");
            }
        }

        public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
            ProviderInvocation invocation,
            DateTimeOffset since,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExternalId>>([]);

        public Task<ValidationOutcome> TestAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default) => Task.FromResult(ValidationOutcome.Success);

        public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
            ProviderInvocation invocation,
            string optionSourceId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FacetValue>>([]);
    }

    /// <summary>
    /// A search result that records being read, and can withdraw the caller as it is.
    /// </summary>
    /// <remarks>
    /// What a cataloger returns is its own collection, and copying it is work the host does on the caller's
    /// behalf. Recording the read is how "nothing the cataloger returned was even looked at" becomes an
    /// assertion rather than an assumption.
    /// </remarks>
    private sealed class WatchedResults(
        Work? answer,
        CancellationTokenSource? withdrawsWhileRead,
        Exception? failsWhileRead)
        : IReadOnlyList<Work>
    {
        private readonly Work[] _items = answer is null ? [] : [answer];

        internal bool WasRead { get; private set; }

        public int Count => _items.Length;

        public Work this[int index] => _items[index];

        public IEnumerator<Work> GetEnumerator()
        {
            WasRead = true;
            withdrawsWhileRead?.Cancel();

            return failsWhileRead is { } failure
                ? throw failure
                : ((IEnumerable<Work>)_items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A clock that moves on every reading, so two reads of one fact can disagree.</summary>
    private sealed class SteppingClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        /// <summary>How far each reading moves the clock. Zero leaves it fixed.</summary>
        internal TimeSpan Step { get; set; } = TimeSpan.Zero;

        internal void Advance(TimeSpan by) => _now += by;

        internal void MoveTo(DateTimeOffset when) => _now = when;

        public override DateTimeOffset GetUtcNow()
        {
            var reading = _now;
            _now += Step;
            return reading;
        }
    }

    /// <summary>A bus for a fixture that never changes a definition.</summary>
    private sealed class UnusedBus : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
    }
}
