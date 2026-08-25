using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Tests.Support;

namespace Arronix.Plugins.Tests.Registration;

/// <summary>
/// Which events an extension may subscribe to, and how each refusal is reported.
/// </summary>
[TestFixture]
public sealed class EventSubscriptionAdmissionTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.subscriber");

    private static readonly Assembly Own = typeof(EventSubscriptionAdmissionTests).Assembly;

    private static readonly Assembly Another = typeof(PluginRegistry).Assembly;

    private static readonly PackageOwnership OwnsThisAssembly = new(Own, []);

    [Test]
    public void TheWholeBusCannotBeSubscribedTo()
    {
        var (registry, ledger) = Create(OwnsThisAssembly);

        var subscribe = () => registry.AddEventHandler<IDomainEvent>(new Everything());

        subscribe.Should().Throw<PluginCapabilityException>()
            .Which.ErrorCode.Should().Be(CoreErrorCode.PluginCapabilityMissing);
        ledger.EventHandlers.Should().BeEmpty();
    }

    [Test]
    public void AnEventInterfaceCannotBeSubscribedTo()
    {
        var (registry, _) = Create(OwnsThisAssembly);

        var subscribe = () => registry.AddEventHandler<IFinished>(new Interested<IFinished>());

        subscribe.Should().Throw<PluginCapabilityException>()
            .WithMessage("*not a concrete event*");
    }

    [Test]
    public void AnAbstractEventCannotBeSubscribedTo()
    {
        var (registry, _) = Create(OwnsThisAssembly);

        var subscribe = () => registry.AddEventHandler<Occurrence>(new Interested<Occurrence>());

        subscribe.Should().Throw<PluginCapabilityException>()
            .WithMessage("*not a concrete event*");
    }

    [Test]
    public void AConcreteBaseTheExtensionOwnsIsAdmittedAsTheOneTypeItNames()
    {
        var (registry, ledger) = Create(OwnsThisAssembly);

        registry.AddEventHandler(new Interested<Reported>());

        ledger.EventHandlers.Should().ContainSingle()
            .Which.EventType.Should().Be<Reported>(
                "a subscription is recorded as the exact type it named, and dispatch delivers that type only");
    }

    [Test]
    public void SealingDecidesBeforeAnythingElseDoes()
    {
        var (registry, _) = Create(new PackageOwnership(Another, []));
        registry.Seal();

        var subscribe = () => registry.AddEventHandler(new Interested<Concluded>());

        subscribe.Should().Throw<InvalidOperationException>()
            .WithMessage("*after its configuration returned*")
            .And.Should().NotBeOfType<PluginIsolationException>(
                "a registry that stopped accepting anything says so first, as it does for every other registration");
    }

    [Test]
    public void AnEventDefinedByAnotherAssemblyIsRefusedAsAnIsolationViolation()
    {
        var (registry, ledger) = Create(new PackageOwnership(Another, []));

        var subscribe = () => registry.AddEventHandler(new Interested<Concluded>());

        subscribe.Should().Throw<PluginIsolationException>()
            .WithMessage($"*{Own.GetName().Name}*")
            .Which.ErrorCode.Should().Be(CoreErrorCode.PluginIsolationViolation);
        ledger.EventHandlers.Should().BeEmpty("a refused subscription is not half-recorded");
    }

    [Test]
    public void AnEventInAContractAssemblyThePackagePublishesIsItsOwn()
    {
        var published = Loaded("Owned.Published.Contract");
        var (registry, ledger) = Create(new PackageOwnership(Own, [published.Assembly]));

        Subscribe(registry, published.EventType);

        ledger.EventHandlers.Should().ContainSingle()
            .Which.EventType.Should().Be(published.EventType, "a package owns the contracts it publishes");
    }

    [Test]
    public void AnEventInADependencysContractAssemblyIsNotThePackagesToSubscribeTo()
    {
        var dependency = Loaded("Visible.Dependency.Contract");
        var (registry, _) = Create(new PackageOwnership(Own, []));

        var subscribe = () => Subscribe(registry, dependency.EventType);

        subscribe.Should().Throw<PluginIsolationException>()
            .WithMessage("*merely see*", "a visible contract belongs to whoever published it");
    }

    [Test]
    public void APrivateAssemblySharingTheExtensionsLoadContextIsNotOwnedEither()
    {
        // Both files are loaded into one context, and only one of them is the package's entry assembly.
        var context = new AssemblyLoadContext("ownership-fixture", isCollectible: true);
        var entry = context.LoadFromAssemblyPath(Emit("Owned.Entry.Assembly"));
        var shipped = context.LoadFromAssemblyPath(Emit("Private.Shipped.Assembly"));

        var (registry, _) = Create(new PackageOwnership(entry, []));

        var subscribe = () => Subscribe(registry, shipped.GetType(EmittedEvent.TypeName)!);

        subscribe.Should().Throw<PluginIsolationException>(
            "sharing a load context is not owning; ownership is the entry assembly and the published contracts");
    }

    [Test]
    public void AnEventTheHostCannotProveOwnershipOfIsRefusedRatherThanAssumed()
    {
        var (registry, _) = Create(ownership: null);

        var subscribe = () => registry.AddEventHandler(new Interested<Concluded>());

        subscribe.Should().Throw<PluginIsolationException>()
            .WithMessage("*no record of what this package owns*");
    }

    [Test]
    public void AnEventTheExtensionsOwnEntryAssemblyDeclaresIsAdmitted()
    {
        var (registry, ledger) = Create(OwnsThisAssembly);

        registry.AddEventHandler(new Interested<Concluded>());

        ledger.EventHandlers.Should().ContainSingle()
            .Which.EventType.Should().Be<Concluded>();
    }

    [Test]
    public void AnAdmittedPlatformEventNeedsNoOwnershipAtAll()
    {
        var (registry, ledger) = Create(ownership: null);

        registry.AddEventHandler(new Interested<ProviderDefinitionChanged>());

        ledger.EventHandlers.Should().ContainSingle()
            .Which.EventType.Should().Be<ProviderDefinitionChanged>();
    }

    [Test]
    public void EveryEventTheContractAssemblyDeclaresIsEitherOnTheListOrRefused()
    {
        // The list is a subset of the contract assembly's events by design. Adding an event to Abstractions
        // therefore lands in the second group and is refused until someone reads its payload and lists it.
        var (registry, ledger) = Create(OwnsThisAssembly);

        foreach (var declared in ContractEvents())
        {
            var subscribe = () => Subscribe(registry, declared);

            if (PlatformEvents.All.Contains(declared))
            {
                subscribe.Should().NotThrow($"'{declared.Name}' is on the list");
            }
            else
            {
                subscribe.Should().Throw<PluginIsolationException>(
                        $"'{declared.Name}' is a platform event that was never admitted")
                    .WithMessage("*not on the list*");
            }
        }

        ledger.EventHandlers.Select(handler => handler.EventType)
            .Should().BeEquivalentTo(PlatformEvents.All);
    }

    [Test]
    public void TheListIsExactlyWhatHasBeenReviewed()
    {
        // Editing this list is the review. It is asserted by name so that widening it is a visible change
        // here as well as there.
        PlatformEvents.All.Should().BeEquivalentTo(new[] { typeof(ProviderDefinitionChanged) });
        PlatformEvents.All.Should().BeSubsetOf(ContractEvents(), "an event the platform does not raise cannot be admitted");
    }

    [Test]
    public void APlatformEventIsDecidedByTheListEvenWhenTheEntryAssemblyWouldSayOtherwise()
    {
        var (registry, ledger) = Create(new PackageOwnership(typeof(IDomainEvent).Assembly, []));

        registry.AddEventHandler(new Interested<ProviderDefinitionChanged>());

        ledger.EventHandlers.Should().ContainSingle(
            "the contract assembly is checked against the list, never against ownership");
    }

    private static (PluginRegistry Registry, PluginRegistrationLedger Ledger) Create(PackageOwnership? ownership)
    {
        var ledger = new PluginRegistrationLedger(Plugin);
        return (new PluginRegistry(Plugin, CapabilitySet.None, ledger, null, ownership), ledger);
    }

    /// <summary>Writes an assembly declaring one event, and loads it as its own.</summary>
    private static (Assembly Assembly, Type EventType) Loaded(string assemblyName)
    {
        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(Emit(assemblyName));
        return (assembly, assembly.GetType(EmittedEvent.TypeName)!);
    }

    private static string Emit(string assemblyName)
        => EmittedEvent.Write(Path.Combine(Path.GetTempPath(), "arronix-ownership", assemblyName), assemblyName);

    /// <summary>Every concrete event the contract assembly declares.</summary>
    private static IReadOnlyList<Type> ContractEvents()
        => [.. typeof(IDomainEvent).Assembly.GetExportedTypes()
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false })];

    /// <summary>Subscribes to an event named only at run time, the way a refusal test has to.</summary>
    private static void Subscribe(PluginRegistry registry, Type eventType)
    {
        try
        {
            typeof(EventSubscriptionAdmissionTests)
                .GetMethod(nameof(SubscribeTo), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(eventType)
                .Invoke(null, [registry]);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
        {
            throw wrapped.InnerException;
        }
    }

    private static void SubscribeTo<TEvent>(PluginRegistry registry)
        where TEvent : IDomainEvent
        => registry.AddEventHandler(new Interested<TEvent>());

    private interface IFinished : IDomainEvent;

    private abstract record Occurrence : IDomainEvent
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }

    private sealed record Concluded : Occurrence;

    private record Reported : IDomainEvent
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }


    private sealed class Interested<TEvent> : IEventHandler<TEvent>
        where TEvent : IDomainEvent
    {
        public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class Everything : IEventHandler<IDomainEvent>
    {
        public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
