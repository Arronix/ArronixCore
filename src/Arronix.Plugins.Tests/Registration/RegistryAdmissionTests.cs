using System.Linq;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Tests.Support;


namespace Arronix.Plugins.Tests.Registration;

/// <summary>
/// What the registry records, in what order, and when it stops accepting anything.
/// </summary>
[TestFixture]
public sealed class RegistryAdmissionTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.admission");

    private static (PluginRegistry Registry, PluginRegistrationLedger Ledger) Create(params Capability[] granted)
    {
        var ledger = new PluginRegistrationLedger(Plugin);
        return (new PluginRegistry(Plugin, CapabilitySet.Of(granted), ledger), ledger);
    }

    [Test]
    public void RegistrationOrderIsPreservedBecauseItIsTheOrderTheExtensionMeant()
    {
        var (registry, ledger) = Create(Capability.Indexing);

        registry
            .AddIndexer<FakeIndexer>(Declarations.Indexer("first"))
            .AddIndexer<FakeIndexer>(Declarations.Indexer("second"))
            .AddIndexer<FakeIndexer>(Declarations.Indexer("third"));

        ledger.Registered<Abstractions.Providers.ProviderTypeRegistration>()
            .Select(registration => registration.Descriptor.LocalId)
            .Should().Equal("first", "second", "third");
        ledger.Entries.Select(entry => entry.Ordinal).Should().Equal(0, 1, 2);
    }

    [Test]
    public void ACatalogerRegistrationRetainsTheMediaOwnedItemTypeAndImplementationType()
    {
        var (registry, ledger) = Create(Capability.Metadata);

        registry.AddCataloger<FakeCatalogItem, FakeCataloger>(Declarations.Cataloger("typed"));

        var registration = ledger.Registered<Abstractions.Providers.ProviderTypeRegistration>().Single();
        registration.ContractType.Should().Be(typeof(Abstractions.Providers.ICataloger<FakeCatalogItem>));
        registration.ImplementationType.Should().Be(typeof(FakeCataloger));
        registration.MediaItemType.Should().Be(typeof(FakeCatalogItem));
    }

    [Test]
    public void EveryAddMethodReturnsTheRegistrySoRegistrationReadsAsOneStatement()
    {
        var (registry, _) = Create(Capability.Parsing);

        registry.AddReleaseParser(new FakeReleaseParser()).Should().BeSameAs(registry);
    }

    [Test]
    public void ANullInstanceIsRefused()
    {
        var (registry, _) = Create(Capability.Parsing);

        var register = () => registry.AddReleaseParser(null!);

        register.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ANullInstanceIsRefusedBeforeTheCapabilityIsEvenConsulted()
    {
        var (registry, _) = Create();

        var register = () => registry.AddReleaseParser(null!);

        register.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AScheduledJobNeedsASchedule()
    {
        var (registry, _) = Create();

        var register = () => registry.AddScheduledJob(new FakeScheduledJob(), "   ");

        register.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AScheduledJobIsRecordedWithItsScheduleUnparsed()
    {
        var (registry, ledger) = Create();

        registry.AddScheduledJob(new FakeScheduledJob(), "every 00:05:00");

        ledger.ScheduledJobs.Should().ContainSingle();
        ledger.ScheduledJobs[0].Schedule.Should().Be(
            "every 00:05:00",
            "the schedule grammar belongs to the scheduler, not to the registry");
    }

    [Test]
    public void AnEventSubscriptionRecordsTheEventTypeItSubscribedTo()
    {
        var (registry, ledger) = Create();

        registry.AddEventHandler(new FakeEventHandler());

        ledger.EventHandlers.Should().ContainSingle();
        ledger.EventHandlers[0].EventType.Should().Be(typeof(FakeEvent));
    }

    [Test]
    public void NothingIsAcceptedAfterConfigurationReturns()
    {
        var (registry, _) = Create(Capability.Parsing);
        registry.Seal();

        var register = () => registry.AddReleaseParser(new FakeReleaseParser());

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*after its configuration returned*");
    }

    [Test]
    public void SealingAlsoStopsUngatedRegistrations()
    {
        var (registry, _) = Create();
        registry.Seal();

        var register = () => registry.AddHealthContributor(new FakeHealthContributor());

        register.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void AskingForASingleRegistrationWhereThereAreSeveralIsAProgrammingError()
    {
        var (registry, ledger) = Create(Capability.Parsing);
        registry.AddReleaseParser(new FakeReleaseParser()).AddReleaseParser(new FakeReleaseParser());

        var single = () => ledger.Single<Abstractions.Parsing.IReleaseParser>();

        single.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void AskingForASingleRegistrationThatWasNeverMadeYieldsNothing()
    {
        var (_, ledger) = Create();

        ledger.Single<Abstractions.Parsing.IReleaseParser>().Should().BeNull();
    }

    [Test]
    public void ARefusedRegistryConstructionNamesTheMissingLedger()
    {
        var construct = () => new PluginRegistry(Plugin, CapabilitySet.None, null!);

        construct.Should().Throw<ArgumentNullException>();
    }

    private sealed record FakeEvent : IDomainEvent
    {
        public Guid EventId => Guid.Empty;

        public DateTimeOffset OccurredAt => DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }

    private sealed class FakeEventHandler : IEventHandler<FakeEvent>
    {
        public Task HandleAsync(FakeEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeScheduledJob : IScheduledJob
    {
        public string JobId => "test";

        public string Name => "Test";

        public string Description => "Test";

        public int Priority => 0;

        public int MaxConcurrency => 1;

        public TimeSpan ShutdownDeadline => TimeSpan.FromSeconds(5);

        public Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new JobExecutionResult(true));
    }
}
