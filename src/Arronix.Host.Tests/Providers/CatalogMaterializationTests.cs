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
    /// The pairing below is the instrument's own control. The identical assertion is made either side of a
    /// search, where it must not move, and either side of a materialization, where it must — so a search
    /// that quietly started allocating could not pass, and neither could a broken counter.
    /// </remarks>
    [Test]
    public async Task ASearchLeavesTheIdentityStateExactlyAsItFoundIt()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));
        var before = context.Issued;

        var found = await context.Dispatcher.SearchAsync<Work>(context.Kind, "alpha", new CatalogQuery("Arrival"));
        var afterSearching = context.Issued;

        context.Dispatcher.Materialize(context.Kind, found.Candidates[0]);

        using var assertions = new AssertionScope();
        found.Candidates.Should().ContainSingle().Which.CatalogId.Should().Be(ExternalId.Of("alpha", "1"));
        found.IsPartialResult.Should().BeFalse();
        afterSearching.Should().Be(before, "searching a catalog is a read and allocates no durable identity");
        context.Issued.Should().Be(before + 1, "the control: taking one in does allocate, so the count moves");
    }

    /// <summary>Fetching is a read too, and repeating one cannot grow the identity space either.</summary>
    [Test]
    public async Task RepeatedFetchesAllocateNothingAndReportTheReferenceOnlyOnceItIsHeld()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var before = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var again = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var issuedWhileReading = context.Issued;

        var taken = context.Dispatcher.Materialize(context.Kind, again.Candidate!);
        var after = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        issuedWhileReading.Should().Be(0, "two fetches of an unheld record name nothing locally");
        before.Candidate!.Held.Should().BeNull();
        again.Candidate!.Held.Should().BeNull();
        after.Candidate!.Held.Should().Be(taken.Reference, "once held, a fetch reports the reference it is held under");
        context.Issued.Should().Be(1, "and reporting it is still a read");
    }

    [Test]
    public async Task RepeatedTakeInOfOneRecordAllocatesOnce()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var first = await context.TakeInAsync(ExternalId.Of("alpha", "1"));
        var second = await context.TakeInAsync(ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        second.Reference.Should().Be(first.Reference);
        first.Reference.Id.Value.Should().BeGreaterThan(0, "the host assigned it, and the cataloger never saw it");
        context.Issued.Should().Be(1, "the second take-in converged on the first assignment rather than minting");
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
        context.Issued.Should().Be(1);
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
        var issuedWhileReading = context.Issued;
        var taken = context.Dispatcher.Materialize(context.Kind, viaAnotherCatalog.Candidate!);

        using var assertions = new AssertionScope();
        viaAnotherCatalog.Candidate!.Held.Should().Be(
            held.Reference,
            "the record is held under an identifier this item also states");
        issuedWhileReading.Should().Be(1, "and reading that is still a read");
        taken.Reference.Should().Be(held.Reference, "so taking it in converges rather than naming it twice");
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
        var issuedWhileReading = context.Issued;
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
        issuedWhileReading.Should().Be(0, "resolving a curated list is a read");
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

        /// <summary>How many local identities this kind has issued.</summary>
        internal long Issued => Identity.Issued(Kind.Kind);
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

            return Task.FromResult<IReadOnlyList<Work>>(answer is null ? [] : [answer]);
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

    /// <summary>A bus for a fixture that never changes a definition.</summary>
    private sealed class UnusedBus : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
    }
}
