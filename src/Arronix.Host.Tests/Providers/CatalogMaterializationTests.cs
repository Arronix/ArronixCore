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

        var found = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        found.Should().NotBeNull();
        found!.Item.Title.Should().Be("Arrival");
        found.CatalogId.Should().Be(ExternalId.Of("alpha", "1"));
        found.Reference.Kind.Should().Be(context.Kind.Kind);
        found.Reference.Id.Value.Should().BeGreaterThan(0, "the host assigned it, and the cataloger never saw it");
    }

    [Test]
    public async Task RepeatedFetchesOfOneRecordResolveToOneReference()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));

        var first = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var second = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        second!.Reference.Should().Be(first!.Reference);
    }

    [Test]
    public async Task ARedirectBindsTheIdentifierAskedForAndTheOneAnsweredWithToOneReference()
    {
        // The catalog followed its own alias: asked for 'old', it answered with the record it calls 'new'.
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "new", "Arrival")));

        var viaAlias = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "old"));
        var direct = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "new"));

        using var assertions = new AssertionScope();
        viaAlias!.CatalogId.Should().Be(
            ExternalId.Of("alpha", "new"),
            "the canonical identity is the one the item states in its cataloger's own scheme");
        direct!.Reference.Should().Be(viaAlias.Reference, "an alias and the record it redirects to are one item");
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

        var viaBeta = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("beta", "tt2"));
        var viaAlpha = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));
        var again = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("beta", "tt2"));

        using var assertions = new AssertionScope();
        viaAlpha!.Reference.Should().Be(viaBeta!.Reference, "they name one item");
        again!.Reference.Should().Be(viaBeta.Reference, "and the identifier bound first keeps resolving");
    }

    /// <summary>
    /// What a merge does to a local identity a caller is already holding: it resolves, and nothing else.
    /// </summary>
    /// <remarks>
    /// Two identities assigned separately merge onto the lower one, and the superseded reference resolves
    /// through <see cref="CatalogIdentity.Canonical"/>. Library rows already written under the superseded
    /// reference are not moved — that is a store operation this milestone does not have — so a caller has to
    /// canonicalize before reading, and the assertion below is the boundary rather than a feature.
    /// </remarks>
    [Test]
    public void ASupersededLocalIdentityResolvesToTheSurvivingOneAndNothingElseMoves()
    {
        var identity = new CatalogIdentity();
        var kind = Works.Id;
        var level = MediaLevelId.FromString("work");

        var first = identity.Identify(kind, level, [ExternalId.Of("alpha", "1")]);
        var second = identity.Identify(kind, level, [ExternalId.Of("beta", "tt2")]);
        var merged = identity.Identify(kind, level, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "tt2")]);

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
        var kind = Works.Id;
        var same = ExternalId.Of("alpha", "1");

        var item = identity.Identify(kind, MediaLevelId.FromString("work"), [same]);
        var group = identity.Identify(kind, MediaLevelId.FromString("collection"), [same]);

        using var assertions = new AssertionScope();
        group.Should().NotBe(item);
        group.Id.Should().NotBe(item.Id, "an item's and a group's identifiers are different key spaces");
        identity.TryIdentify(kind, MediaLevelId.FromString("work"), same, out var round).Should().BeTrue();
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
        var level = MediaLevelId.FromString("work");
        var other = MediaKindId.FromString("other");

        var mine = identity.Identify(Works.Id, level, [ExternalId.Of("alpha", "1")]);
        var supersededHere = identity.Identify(Works.Id, level, [ExternalId.Of("beta", "2")]);
        identity.Identify(other, level, [ExternalId.Of("gamma", "9")]);
        var theirs = identity.Identify(other, level, [ExternalId.Of("delta", "8")]);

        identity.Identify(Works.Id, level, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]);

        using var assertions = new AssertionScope();
        theirs.Id.Should().Be(supersededHere.Id, "each kind numbers its own entities, so the collision is real");
        theirs.Kind.Should().NotBe(supersededHere.Kind);
        identity.Canonical(supersededHere).Should().Be(mine, "the merge happened where it was asked for");
        identity.Canonical(theirs).Should().Be(theirs, "and the same number in another kind was not part of it");
        identity.TryIdentify(other, level, ExternalId.Of("delta", "8"), out var round).Should().BeTrue();
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
        var work = MediaLevelId.FromString("work");
        var collection = MediaLevelId.FromString("collection");
        var one = MediaItemId.FromInt64(1);
        var two = MediaItemId.FromInt64(2);

        var item = identity.Assign(Works.Id, work, ExternalId.Of("alpha", "1"), one);
        var group = identity.Assign(Works.Id, collection, ExternalId.Of("alpha", "1"), one);
        var supersededItem = identity.Assign(Works.Id, work, ExternalId.Of("beta", "2"), two);
        var groupToo = identity.Assign(Works.Id, collection, ExternalId.Of("beta", "2"), two);

        identity.Identify(Works.Id, work, [ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "2")]);

        using var assertions = new AssertionScope();
        group.Id.Should().Be(item.Id, "the numbers collide across levels, which is what this rule is about");
        identity.Canonical(supersededItem).Should().Be(item, "the merge happened at the item level");
        identity.Canonical(groupToo).Should().Be(groupToo, "and not at the group level");
        identity.TryIdentify(Works.Id, collection, ExternalId.Of("beta", "2"), out var round).Should().BeTrue();
        round.Should().Be(groupToo, "the group's assignment was not moved with the item's");
    }

    /// <summary>Identity is host state, so reloading the extension that declares a kind does not reissue it.</summary>
    [Test]
    public async Task ARebuiltKindRuntimeKeepsTheIdentityTheHostAlreadyAssigned()
    {
        var context = CatalogContext.WithCatalogers(new StubCataloger("alpha", Work("alpha", "1", "Arrival")));
        var before = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        // What a reload does: the kind's runtime is derived again from the same declaration. Host state,
        // including everything already in the library, is not.
        var reloaded = context with
        {
            Kind = MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>(),
        };
        var after = await reloaded.Dispatcher.FetchAsync<Work>(reloaded.Kind, ExternalId.Of("alpha", "1"));

        using var assertions = new AssertionScope();
        reloaded.Kind.Should().NotBeSameAs(context.Kind, "the runtime really was rebuilt");
        after!.Reference.Should().Be(before!.Reference);
    }

    [Test]
    public async Task RoutingSelectsTheCatalogerThatOwnsTheSchemeRatherThanTheFirstOneRegistered()
    {
        var alpha = new StubCataloger("alpha", Work("alpha", "1", "Alpha answer"));
        var beta = new StubCataloger("beta", Work("beta", "1", "Beta answer"));
        var context = CatalogContext.WithCatalogers(alpha, beta);

        var found = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("beta", "1"));

        using var assertions = new AssertionScope();
        found!.Item.Title.Should().Be("Beta answer");
        alpha.Fetched.Should().BeEmpty("the scheme, not the registration order, decides who is asked");
        beta.Fetched.Should().Equal(ExternalId.Of("beta", "1"));
    }

    [Test]
    public async Task RoutingAndValidationUseTheSchemeCapturedAtRegistration()
    {
        var cataloger = new StubCataloger("alpha", Work("alpha", "1", "Arrival"));
        var context = CatalogContext.WithCatalogers(cataloger);

        cataloger.CatalogScheme = "changed-after-registration";
        var found = await context.Dispatcher.FetchAsync<Work>(context.Kind, ExternalId.Of("alpha", "1"));

        found!.CatalogId.Should().Be(ExternalId.Of("alpha", "1"));
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
    public async Task ACuratedListIsMaterializedThroughTheCatalogersThatOwnItsReferences()
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

        var found = await context.Dispatcher.MaterializeAsync(context.Kind, list);

        using var assertions = new AssertionScope();
        found.Select(entry => entry.Item.Title).Should().Equal("First", "Second");
        found.Select(entry => entry.CatalogId)
            .Should().Equal(ExternalId.Of("alpha", "1"), ExternalId.Of("beta", "9"));
        found.Select(entry => entry.CuratedEntryId)
            .Should().Equal(CuratedEntryId.Of("row-1"), null);
        found.Select(entry => entry.Reference).Distinct().Should().HaveCount(2);
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

        var act = () => context.Dispatcher.MaterializeAsync(context.Kind, list);

        act.Should().ThrowAsync<ArronixException>()
            .Where(error => error.ErrorCode == CoreErrorCode.CatalogSchemeUnowned);
        alpha.Fetched.Should().BeEmpty("ownership is settled for the whole list before any of it is fetched");
    }

    private static Work Work(string scheme, string value, string title, params ExternalId[] also) => new()
    {
        Title = title,
        ExternalIds = ExternalIdSet.From([ExternalId.Of(scheme, value), .. also]),
    };

    /// <summary>A registry, definitions and host identity state wired the way the host wires them.</summary>
    private sealed record CatalogContext(IMediaTypeRuntime Kind, CatalogDispatcher Dispatcher)
    {
        internal static CatalogContext WithCatalogers(params StubCataloger[] catalogers)
        {
            var providers = new ProviderRegistry();
            var definitions = new ProviderDefinitionStore(providers, new UnusedBus(), TimeProvider.System);
            var status = new ProviderStatusStore(TimeProvider.System);
            var sessions = new ProviderSessionStore(TimeProvider.System);
            var tests = new ProviderTestService(providers, definitions, sessions, status);
            var kind = MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>();

            var local = 0;

            foreach (var cataloger in catalogers)
            {
                var name = $"catalog-{++local}";
                var provider = providers.Register(
                    PluginId.FromString($"example.{cataloger.CatalogScheme}"),
                    ProviderFamily.Cataloger,
                    new ProviderDescriptor { LocalId = name, Name = name, Settings = [] },
                    cataloger,
                    kind.Kind);

                definitions.AddAsync(new ProviderDefinition
                {
                    Id = 0,
                    Provider = provider,
                    Family = ProviderFamily.Cataloger,
                    Name = name,
                    Settings = new Dictionary<string, string>(StringComparer.Ordinal),
                }).GetAwaiter().GetResult();
            }

            return new CatalogContext(
                kind,
                new CatalogDispatcher(providers, definitions, status, tests, new CatalogIdentity()));
        }
    }

    /// <summary>A cataloger that answers every request with one record and remembers what it was asked.</summary>
    private sealed class StubCataloger(string scheme, Work answer) : ICataloger<Work>
    {
        private readonly List<ExternalId> _fetched = [];
        private readonly List<CatalogQuery> _searched = [];

        internal IReadOnlyList<ExternalId> Fetched => _fetched;

        internal IReadOnlyList<CatalogQuery> Searched => _searched;

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
            return Task.FromResult<IReadOnlyList<Work>>([answer]);
        }

        public Task<Work?> GetAsync(
            ProviderInvocation invocation,
            ExternalId id,
            CancellationToken cancellationToken = default)
        {
            _fetched.Add(id);
            return Task.FromResult<Work?>(answer);
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
