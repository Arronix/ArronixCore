using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Contributions;
using Arronix.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arronix.Common.Tests.Events;

/// <summary>
/// Who receives a published event, in what order, and what happens when one of them fails.
/// </summary>
[TestFixture]
public class HostEventPublisherTests
{
    private static readonly PluginId Movies = PluginId.FromString("a.movies");

    private static readonly PluginId Television = PluginId.FromString("b.television");

    [Test]
    public async Task EveryEligibleHostHandlerRunsInContractThenRegistrationOrderAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();

        services.AddSingleton<IEventHandler<IDomainEvent>>(new Recording<IDomainEvent>("base-first", order));
        services.AddSingleton<IEventHandler<IDomainEvent>>(new Recording<IDomainEvent>("base-second", order));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("exact-first", order));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("exact-second", order));

        await Publisher(services).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"exact-first", "exact-second", "base-first", "base-second"}));
    }

    [Test]
    public async Task AHandlerRegisteredUnderTwoContractsRunsOnceAsync()
    {
        var order = new List<string>();
        var both = new Both("broadcaster", order);
        var services = new ServiceCollection();

        services.AddSingleton<IEventHandler<IDomainEvent>>(both);
        services.AddSingleton<IEventHandler<Renamed>>(both);

        await Publisher(services).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"broadcaster"}), "one instance is one subscriber however it was registered");
    }

    [Test]
    public async Task PublishingThroughABaseReferenceReachesTheConcreteHandlersAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("exact", order));

        IDomainEvent published = new Renamed();
        await Publisher(services).PublishAsync(published).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"exact"}), "the runtime type selects the handlers, not the caller's variable");
    }

    [Test]
    public async Task AThrowingHandlerDoesNotStopTheOnesAfterItAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();

        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("first", order));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("throws", order, throws: true));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("third", order));

        await Publisher(services).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"first", "throws", "third"}));
    }

    [Test]
    public void ACanceledPublicationIsTheCallersAndPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            () => Publisher(new ServiceCollection()).PublishAsync(new Renamed(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ExtensionHandlersRunAfterTheHostsAndInTheOrderTheSourceGaveThemAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("host", order));

        var contributions = new StubContributions(
        [
            Contribution(Television, 0, new Recording<Renamed>("television", order)),
            Contribution(Movies, 1, new Recording<Renamed>("movies-second", order)),
            Contribution(Movies, 0, new Recording<Renamed>("movies-first", order)),
        ]);

        await Publisher(services, contributions).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(
            order,
            Is.EqualTo(new[] {"host", "television", "movies-second", "movies-first"}),
            "the publisher preserves the source's order rather than imposing one; that this list is not the installation order is the point");
    }

    [Test]
    public async Task AThrowingExtensionHandlerDoesNotStopTheOnesAfterItAsync()
    {
        var order = new List<string>();
        var contributions = new StubContributions(
        [
            Contribution(Movies, 0, new Recording<Renamed>("throws", order, throws: true)),
            Contribution(Movies, 1, new Recording<Renamed>("after", order)),
        ]);

        await Publisher(new ServiceCollection(), contributions).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"throws", "after"}));
    }

    [Test]
    public async Task EveryExtensionLeaseIsReleasedWhateverTheHandlersDidAsync()
    {
        var order = new List<string>();
        var contributions = new StubContributions(
        [
            Contribution(Movies, 0, new Recording<Renamed>("throws", order, throws: true)),
        ]);

        await Publisher(new ServiceCollection(), contributions).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(contributions.Released, Is.EqualTo(1), "a leaked lease is an extension that can never be torn down");
    }

    [Test]
    public async Task AHostThatComposedNoExtensionRuntimePublishesToItsOwnHandlersAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("host", order));

        await Publisher(services).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"host"}));
    }


    [Test]
    public async Task ACallerWhoGivesUpPartWayThroughStopsTheRestAsync()
    {
        var order = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var services = new ServiceCollection();

        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("first", order));
        services.AddSingleton<IEventHandler<Renamed>>(new Canceling(cancellation, order));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("third", order));

        try
        {
            await Publisher(services).PublishAsync(new Renamed(), cancellation.Token).ConfigureAwait(false);
            Assert.Fail("the caller's cancellation is the caller's and propagates");
        }
        catch (OperationCanceledException)
        {
            // The point of the test.
        }

        Assert.That(order, Is.EqualTo(new[] {"first", "cancels"}), "the token is read before each handler, not only at entry");
    }

    [Test]
    public async Task AHandlersOwnCancellationIsAnOrdinaryFailureAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();

        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("first", order, throws: new OperationCanceledException("its own timeout")));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("second", order));

        await Publisher(services).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"first", "second"}), "nobody asked the publication to stop");
    }

    [Test]
    public async Task AHandlerOfABaseTypeDoesNotReceiveADerivedExtensionEventAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("host-handler-of-the-base", order));

        var contributions = new StubContributions(
        [
            Contribution(Movies, 0, new Recording<Renamed>("extension-subscribed-to-the-base", order)),
        ]);

        await Publisher(services, contributions).PublishAsync(new RenamedTwice()).ConfigureAwait(false);

        Assert.That(
            order,
            Is.EqualTo(new[] {"host-handler-of-the-base"}),
            "the host's own handlers are resolved through the whole contract chain; a contributed subscription is delivered by exact type only");
    }

    [Test]
    public async Task TheRuntimeTypeChoosesTheHandlersEvenWhenItIsOnlyKnownAtRunTimeAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<RenamedTwice>>(new Recording<RenamedTwice>("derived", order));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("base", order));

        var published = (IDomainEvent)Activator.CreateInstance(typeof(RenamedTwice))!;
        await Publisher(services).PublishAsync(published).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"derived", "base"}), "host handlers run most specific contract first");
    }

    [Test]
    public async Task ACanceledFanOutStillReleasesTheExtensionLeasesAsync()
    {
        var order = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var contributions = new StubContributions(
        [
            Contribution(Movies, 0, new Canceling(cancellation, order)),
            Contribution(Movies, 1, new Recording<Renamed>("after", order)),
        ]);

        try
        {
            await Publisher(new ServiceCollection(), contributions)
                .PublishAsync(new Renamed(), cancellation.Token)
                .ConfigureAwait(false);
            Assert.Fail("the caller's cancellation propagates");
        }
        catch (OperationCanceledException)
        {
            // The point of the test.
        }

        Assert.That(order, Is.EqualTo(new[] {"cancels"}), "the token is read before each contributed handler too");
        Assert.That(contributions.Released, Is.EqualTo(1), "cancellation leaves through the same release as success");
    }

    [Test]
    public async Task AFailureWhoseMessageThrowsIsStillContainedAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();

        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("hostile", order, new Unreadable()));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("after", order));

        await Publisher(services).PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"hostile", "after"}), "rendering the failure must not become the failure");
    }

    [Test]
    public async Task AFailingLogIsNotAReasonToStopCallingHandlersAsync()
    {
        var order = new List<string>();
        var services = new ServiceCollection();

        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("throws", order, throws: true));
        services.AddSingleton<IEventHandler<Renamed>>(new Recording<Renamed>("after", order));

        var publisher = new HostEventPublisher(services.BuildServiceProvider(), new BrokenLog(), null);
        await publisher.PublishAsync(new Renamed()).ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(new[] {"throws", "after"}));
    }

    [Test]
    public void AProcessFatalFailureIsNotContained()
    {
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IEventHandler<Renamed>>(
            new Recording<Renamed>("exhausted", order, new OutOfMemoryException()));

        Assert.That(
            () => Publisher(services).PublishAsync(new Renamed()),
            Throws.InstanceOf<OutOfMemoryException>(),
            "containment is for extension defects, not for a process that is no longer sound");
    }

    [Test]
    public void AnExtensionsOwnEventTypeIsNotRetainedOnceItsAssemblyGoesAway()
    {
        var (published, requested) = PublishFromACollectibleAssembly(wrapped: false);

        Assert.That(Reaching(requested), Is.Empty, "nothing naming the extension was asked of the container");
        Assert.That(Reaching(CachedContracts()), Is.Empty, "and nothing naming it was kept here");
        Assert.That(Collected(published), Is.True, "publishing an extension's event must not pin the assembly that defines it");
    }

    [Test]
    public void APlatformGenericClosedOverAnExtensionTypeIsNotRetainedEither()
    {
        // The generic definition is the host's, so the constructed type reports a permanent assembly while
        // naming a collectible one. Keeping it pins the extension exactly as its own event type would.
        var (published, requested) = PublishFromACollectibleAssembly(wrapped: true);

        Assert.That(Reaching(requested), Is.Empty, "IEventHandler<Envelope<Extension>> would sit in the container's accessor cache");
        Assert.That(Reaching(CachedContracts()), Is.Empty, "and Envelope<Extension> would sit in this publisher's own caches");
        Assert.That(Collected(published), Is.True, "a constructed generic is only as permanent as its type arguments");
    }

    /// <summary>Every type either publisher cache is keyed by, read straight off the static fields.</summary>
    private static IReadOnlyList<Type> CachedContracts()
        => [.. new[] {"Chains", "Invokers"}
            .Select(field => typeof(HostEventPublisher).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!)
            .SelectMany(field => ((IEnumerable<KeyValuePair<Type, object>>)Keys(field.GetValue(null)!)).Select(pair => pair.Key))];

    private static IEnumerable<KeyValuePair<Type, object>> Keys(object dictionary)
        => ((System.Collections.IEnumerable)dictionary)
            .Cast<object>()
            .Select(entry => new KeyValuePair<Type, object>(
                (Type)entry.GetType().GetProperty("Key")!.GetValue(entry)!,
                entry));

    /// <summary>The types in a set that name collectible code, which is what nothing permanent may hold.</summary>
    private static IReadOnlyList<Type> Reaching(IEnumerable<Type> types)
        => [.. types.Where(Collectible)];

    private static bool Collectible(Type type)
        => type.Assembly.IsCollectible
            || (type.HasElementType && Collectible(type.GetElementType()!))
            || type.GenericTypeArguments.Any(Collectible);

    /// <summary>Publishes an event that reaches an assembly of its own, and reports on that assembly.</summary>
    /// <param name="wrapped">
    /// <see langword="false"/> to publish the emitted type itself; <see langword="true"/> to publish a
    /// permanent generic event closed over it.
    /// </param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Published, IReadOnlyList<Type> Requested) PublishFromACollectibleAssembly(bool wrapped)
    {
        var name = "Emitted" + Guid.CreateVersion7().ToString("N");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect);
        var emitted = assembly
            .DefineDynamicModule(name)
            .DefineType(name + ".Fact", TypeAttributes.Public | TypeAttributes.Class, wrapped ? typeof(object) : typeof(Emitted))
            .CreateType();

        var domainEvent = (IDomainEvent)Activator.CreateInstance(
            wrapped ? typeof(Envelope<>).MakeGenericType(emitted) : emitted)!;

        var asked = new Asked(new ServiceCollection().BuildServiceProvider());
        new HostEventPublisher(asked, NullLogger<HostEventPublisher>.Instance)
            .PublishAsync(domainEvent)
            .GetAwaiter()
            .GetResult();

        Assert.That(emitted.Assembly.IsCollectible, Is.True, "the fixture proves nothing if the assembly is permanent");
        return (new WeakReference(assembly), asked.Requested);
    }

    /// <summary>Whether the collector could take it, which is the only real proof of release.</summary>
    private static bool Collected(WeakReference published)
    {
        for (var attempt = 0; attempt < 12 && published.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return !published.IsAlive;
    }

    private static HostEventPublisher Publisher(
        ServiceCollection services,
        IPluginContributionSource? contributions = null)
        => new(services.BuildServiceProvider(), NullLogger<HostEventPublisher>.Instance, contributions);

    private static PluginContribution<EventHandlerContribution> Contribution<TEvent>(
        PluginId owner,
        int ordinal,
        IEventHandler<TEvent> handler)
        where TEvent : IDomainEvent
        => new(
            owner,
            ordinal,
            new EventHandlerContribution(
                handler,
                typeof(TEvent),
                (domainEvent, token) => handler.HandleAsync((TEvent)domainEvent, token)));

    private record Renamed : IDomainEvent
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }

    private sealed record RenamedTwice : Renamed;

    private sealed class Recording<TEvent> : IEventHandler<TEvent>
        where TEvent : IDomainEvent
    {
        private readonly string _name;
        private readonly List<string> _order;
        private readonly Exception? _failure;

        public Recording(string name, List<string> order, bool throws = false)
            : this(name, order, throws ? new InvalidOperationException($"{name} objected") : null)
        {
        }

        public Recording(string name, List<string> order, Exception? throws)
        {
            _name = name;
            _order = order;
            _failure = throws;
        }

        public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
        {
            lock (_order)
            {
                _order.Add(_name);
            }

            return _failure is null ? Task.CompletedTask : Task.FromException(_failure);
        }
    }

    /// <summary>A subscriber that cancels the publication it is part of.</summary>
    private sealed class Canceling(CancellationTokenSource cancellation, List<string> order)
        : IEventHandler<Renamed>
    {
        public Task HandleAsync(Renamed domainEvent, CancellationToken cancellationToken = default)
        {
            order.Add("cancels");
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    /// <summary>A container that remembers what it was asked for, because its accessor cache is permanent.</summary>
    private sealed class Asked(IServiceProvider inner) : IServiceProvider
    {
        private readonly List<Type> _requested = [];

        internal IReadOnlyList<Type> Requested => _requested;

        public object? GetService(Type serviceType)
        {
            _requested.Add(serviceType);
            return inner.GetService(serviceType);
        }
    }

    /// <summary>An exception that will not say what went wrong.</summary>
    private sealed class Unreadable : Exception
    {
        public override string Message => throw new InvalidOperationException("not telling");
    }

    /// <summary>A log that fails on its own account, the way an overwhelmed sink does.</summary>
    private sealed class BrokenLog : ILogger<HostEventPublisher>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("the log is full");
    }

    /// <summary>A base an emitted event derives from, so the emitted type needs no members of its own.</summary>
    public record Emitted : IDomainEvent
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }

    /// <summary>A permanent event type that names another type. Its payload is never read here.</summary>
    public sealed record Envelope<TPayload> : IDomainEvent
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }

    private sealed class Both(string name, List<string> order)
        : IEventHandler<IDomainEvent>, IEventHandler<Renamed>
    {
        public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            order.Add(name);
            return Task.CompletedTask;
        }

        public Task HandleAsync(Renamed domainEvent, CancellationToken cancellationToken = default)
        {
            order.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class StubContributions(IReadOnlyList<PluginContribution<EventHandlerContribution>> handlers)
        : IPluginContributionSource
    {
        internal int Released { get; private set; }

        public IContributionLease<TContract> Acquire<TContract>()
            where TContract : class => throw new NotSupportedException();

        public IContributionLease<TContract> AcquireOwned<TContract>(PluginId owner)
            where TContract : class => throw new NotSupportedException();

        public IContributionLease<EventHandlerContribution> AcquireEventHandlers(Type eventType)
            => new Lease(
                [.. handlers.Where(handler => handler.Value.EventType == eventType)],
                () => Released++);

        public IReadOnlyList<PluginId> ContributorsOf<TContract>()
            where TContract : class => [];

        private sealed class Lease(
            IReadOnlyList<PluginContribution<EventHandlerContribution>> contributions,
            Action onRelease) : IContributionLease<EventHandlerContribution>
        {
            public IReadOnlyList<PluginContribution<EventHandlerContribution>> Contributions => contributions;

            public void Dispose() => onRelease();
        }
    }
}
