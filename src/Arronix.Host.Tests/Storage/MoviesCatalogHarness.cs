using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;
using Arronix.Host.Configuration;
using Arronix.Host.Engines.Items;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Media.Typed;
using Arronix.Host.Providers;
using Arronix.Host.Storage;
using Arronix.Host.Storage.Durable;
using Arronix.Media.Movies;
using Arronix.Plugin.Movies;
using Arronix.Plugin.Movies.Definition;
using Arronix.Host.Tests.Support;
using FluentAssertions;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// The catalog vertical over the real movies contract, on a real database.
/// </summary>
/// <remarks>
/// <para>
/// The media kind is the shipped <see cref="Movies"/> declaration, so the item is the shipped
/// <see cref="Movie"/> and the storage bridge is the one its own build generated. That is the point: this
/// is the only place a durable record can be written and read back through a real generated codec.
/// </para>
/// <para>
/// The kind is bound in process rather than loaded from a package, because a packaged kind's item type is
/// a different CLR type in its own load context and a cataloger compiled here could not close over it —
/// which is the isolation the loader exists to enforce, and is proved separately by the packaged admission
/// tests. What is under test here is the service above that seam.
/// </para>
/// </remarks>
internal sealed class MoviesCatalogHarness : IDisposable
{
    internal static readonly MediaKindId Kind = MediaKindId.FromString("movies");

    private readonly DurableStoreFixture _store;
    private readonly bool _ownsStore;

    private MoviesCatalogHarness(DurableStoreFixture store, bool ownsStore, params StubCataloger[] catalogers)
    {
        _store = store;
        _ownsStore = ownsStore;
        Catalogers = catalogers;

        // One ledger across every cataloger, so what is recorded is the order the calls happened in. Two
        // per-provider queues concatenated would report the same order whatever the calls did.
        foreach (var cataloger in catalogers)
        {
            cataloger.RecordInto(Requests);
        }

        var library = new LibraryOptions();
        library.RootFolders.Add("/library");

        Kinds = new MediaKindRegistry(TestOptions.Of(library));
        Providers = new ProviderRegistry();
        Identity = store.Identity();
        Records = store.Records();
        Store = new DurableLibraryStore(store.Schema, Kinds);
        Definitions = new ProviderDefinitionStore(Providers, new SilentBus(), TimeProvider.System, store.Definitions());

        // The item store is the seam under test; the two required engines are filled with values that
        // refuse, because nothing here matches a release or plans a query.
        var engines = new DefinitionEngineCatalog
        {
            ItemStore = definition => new HostItemSource(definition, Records, Identity),
            Matcher = definition => new UnusedMatcher(definition.Kind),
            QueryPlanner = definition => new UnusedPlanner(definition.Kind),
        };

        var binder = new MediaTypeBinder(Kinds, engines, Providers);
        var runtime = MediaTypeModelFactory
            .Build<Movie, ReleaseTarget<Movie>, Release<Video>, MovieReleaseParser, Movies>();

        binder.TryRegister(
            PluginId.FromString("movies"),
            "0.1.0",
            CapabilitySet.Of(Capability.MediaKind, Capability.Parsing),
            runtime,
            out _,
            out var defects).Should().BeTrue(string.Join("; ", defects.Select(defect => defect.Message)));

        var status = new ProviderStatusStore(TimeProvider.System);
        var sessions = new ProviderSessionStore(TimeProvider.System);
        var tests = new ProviderTestService(Providers, Definitions, sessions, status);

        foreach (var cataloger in catalogers)
        {
            var local = $"catalog-{cataloger.CatalogScheme}";
            var provider = Providers.Register(
                PluginId.FromString($"example.{cataloger.CatalogScheme}"),
                ProviderFamily.Cataloger,
                new ProviderDescriptor { LocalId = local, Name = local, Settings = [] },
                cataloger,
                Kind);

            Definitions.AddAsync(new ProviderDefinition
            {
                Id = 0,
                Provider = provider,
                Family = ProviderFamily.Cataloger,
                Name = local,
                Priority = cataloger.Priority,
                Settings = new Dictionary<string, string>(StringComparer.Ordinal),
            }).GetAwaiter().GetResult();
        }

        Dispatcher = new CatalogDispatcher(Providers, Definitions, status, tests, Identity);
        Catalog = new CatalogLibrary(Kinds, Dispatcher, Records, Store, Identity, TimeProvider.System);
        Items = new MediaItemBroker(Kinds);
    }

    internal MediaKindRegistry Kinds { get; }

    internal ProviderRegistry Providers { get; }

    internal ProviderDefinitionStore Definitions { get; }

    internal CatalogIdentity Identity { get; }

    internal ICatalogRecordStore Records { get; }

    internal IMediaStore Store { get; }

    internal CatalogDispatcher Dispatcher { get; }

    internal CatalogLibrary Catalog { get; }

    internal MediaItemBroker Items { get; }

    internal IReadOnlyList<StubCataloger> Catalogers { get; }

    /// <summary>Every catalog call any cataloger received, in the order it received them.</summary>
    internal ConcurrentQueue<string> Requests { get; } = new();

    internal ArronixStoreContext Read() => _store.Read();

    /// <summary>
    /// Starts again over the same database, as a new process would.
    /// </summary>
    /// <remarks>
    /// The database outlives both harnesses and belongs to the one that made it, so the restarted harness
    /// disposes only what it created. Disposing it must not delete the file the caller is still holding, and
    /// disposing both must not delete it twice.
    /// </remarks>
    internal MoviesCatalogHarness Restart(params StubCataloger[] catalogers)
    {
        _store.Reopen();
        return new MoviesCatalogHarness(_store, ownsStore: false, catalogers);
    }

    internal static MoviesCatalogHarness With(params StubCataloger[] catalogers)
        => new(new DurableStoreFixture(), ownsStore: true, catalogers);

    public void Dispose()
    {
        Definitions.Dispose();

        if (_ownsStore)
        {
            _store.Dispose();
        }
    }

    /// <summary>One movie, shaped the way a cataloger shapes one.</summary>
    internal static Movie Film(
        string title,
        IEnumerable<ExternalId> ids,
        CatalogRecordState state = CatalogRecordState.Active,
        int? year = 1999)
        => new()
        {
            Title = title,
            Year = year,
            CatalogState = state,
            ExternalIds = ExternalIdSet.From(ids),
            Lifecycle = new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(1999, 3, 31),
                EvaluatedOn = new DateOnly(2026, 8, 27),
            },
        };

    /// <summary>A cataloger that answers from a script and counts what it was asked.</summary>
    internal sealed class StubCataloger(string scheme, int priority = 25) : ICataloger<Movie>
    {
        private readonly ConcurrentDictionary<string, Movie> _byValue = new(StringComparer.Ordinal);
        private ConcurrentQueue<string>? _ledger;

        public string CatalogScheme { get; } = scheme;

        internal int Priority { get; } = priority;

        /// <summary>Records this cataloger's calls into the ledger every cataloger shares.</summary>
        internal void RecordInto(ConcurrentQueue<string> ledger) => _ledger = ledger;

        public ProviderDescriptor Descriptor =>
            new() { LocalId = CatalogScheme, Name = CatalogScheme, Settings = [] };

        public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

        /// <summary>Scripts what this catalog answers for one of its own identifiers.</summary>
        internal StubCataloger Holding(string value, Movie answer)
        {
            _byValue[value] = answer;
            return this;
        }

        public Task<IReadOnlyList<Movie>> SearchAsync(
            ProviderInvocation invocation,
            CatalogQuery query,
            CancellationToken cancellationToken = default)
        {
            _ledger?.Enqueue($"search:{CatalogScheme}");
            return Task.FromResult<IReadOnlyList<Movie>>([.. _byValue.Values.OrderBy(film => film.Title, StringComparer.Ordinal)]);
        }

        public Task<Movie?> GetAsync(
            ProviderInvocation invocation,
            ExternalId id,
            CancellationToken cancellationToken = default)
        {
            _ledger?.Enqueue($"{id.Scheme}:{id.Value}");
            return Task.FromResult(_byValue.GetValueOrDefault(id.Value));
        }

        public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
            ProviderInvocation invocation,
            DateTimeOffset since,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExternalId>>([]);

        public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => [];

        public Task<ValidationOutcome> TestAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
            ProviderInvocation invocation,
            string sourceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedMatcher(MediaKindId kind) : IReleaseMatcher
    {
        public MediaKindId MediaKind => kind;

        public Task<MatchOutcome> MatchAsync(
            MatchRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The catalog vertical never matches a release.");
    }

    private sealed class UnusedPlanner(MediaKindId kind) : IReleaseQueryPlanner
    {
        public MediaKindId MediaKind => kind;

        public Task<ReleaseQueryPlan> PlanAsync(
            AcquisitionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The catalog vertical never plans a query.");
    }

    private sealed class SilentBus : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
    }
}
