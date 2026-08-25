using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Events;
using Arronix.Common.Contributions;
using Arronix.Common.Lifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arronix.Common.Events;

/// <summary>
/// The platform's event bus: fan-out to the host's own handlers and to the ones extensions contributed.
/// </summary>
/// <remarks>
/// <para>
/// Host handlers run first, most specific contract first and in registration order within a contract; then
/// extension handlers, by extension identifier and registration order. One instance runs once however many
/// contracts it is registered under.
/// </para>
/// <para>
/// Nothing that reaches a collectible assembly is cached here or asked of the container, because both this
/// type's caches and the container's accessor cache outlive every extension. An extension's handler for its
/// own event comes from the leased ledger instead.
/// </para>
/// <para>
/// A handler that throws does not stop the ones after it; the failure is logged and never republished as an
/// event, which would turn one broken handler into a loop.
/// </para>
/// </remarks>
internal sealed partial class HostEventPublisher : IEventPublisher
{
    /// <summary>How much of an extension-supplied string reaches the log.</summary>
    private const int MaxLoggedLength = 512;

    /// <summary>The contract chain of one event type, cached only when nothing in it is collectible.</summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<Type>> Chains = new();

    /// <summary>How to call a handler of one contract. Keyed only by contracts that are never collectible.</summary>
    private static readonly ConcurrentDictionary<Type, Invoker> Invokers = new();

    private static readonly MethodInfo InvokerFactory = typeof(HostEventPublisher)
        .GetMethod(nameof(MakeInvoker), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IServiceProvider _services;
    private readonly IPluginContributionSource? _contributions;
    private readonly ILogger<HostEventPublisher> _log;

    /// <summary>Initializes a new instance of the <see cref="HostEventPublisher"/> class.</summary>
    /// <param name="services">Where the host's own handlers are resolved from.</param>
    /// <param name="log">The log a failing handler is reported to.</param>
    /// <param name="contributions">
    /// The live extension runtime, when there is one. A host composed without it publishes to its own
    /// handlers only.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HostEventPublisher(
        IServiceProvider services,
        ILogger<HostEventPublisher> log,
        IPluginContributionSource? contributions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(log);

        _services = services;
        _log = log;
        _contributions = contributions;
    }

    /// <summary>Calls one handler of a known contract.</summary>
    private delegate Task Invoker(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The runtime type selects the handlers, not <typeparamref name="TEvent"/>. Cancellation is read
    /// before each handler, not only at entry, so a caller that gives up part-way through stops the rest;
    /// the leases are still released on the way out.
    /// </remarks>
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        var eventType = domainEvent.GetType();
        var delivered = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var contract in Contracts(eventType))
        {
            var invoke = Invokers.GetOrAdd(contract, static known => (Invoker)InvokerFactory
                .MakeGenericMethod(known)
                .Invoke(null, null)!);

            foreach (var handler in _services.GetServices(typeof(IEventHandler<>).MakeGenericType(contract)))
            {
                if (handler is not null && delivered.Add(handler))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await InvokeAsync(
                            handler,
                            () => invoke(handler, domainEvent, cancellationToken),
                            eventType,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        if (_contributions is null)
        {
            return;
        }

        // Selected and leased under the publication gate, then invoked with the gate released. The leases
        // are held for the whole fan-out, so teardown waits for a handler rather than disposing it.
        using var contributed = _contributions.AcquireEventHandlers(eventType);

        foreach (var contribution in contributed.Contributions)
        {
            var handler = contribution.Value;

            if (delivered.Add(handler.Handler))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await InvokeAsync(
                        handler.Handler,
                        () => handler.Invoke(domainEvent, cancellationToken),
                        eventType,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The contracts the host's own handlers may be resolved through for one event, most specific first:
    /// the concrete type, its bases nearest first, then its event interfaces ordered by name so the order
    /// cannot vary between runs.
    /// </summary>
    /// <remarks>
    /// Contracts that reach a collectible assembly are left out and their chains are not cached: resolving
    /// one would put a constructed generic naming an extension's type into the container's accessor cache,
    /// which never goes away.
    /// </remarks>
    private static IReadOnlyList<Type> Contracts(Type eventType)
        => ReachesCollectibleCode(eventType)
            ? Chain(eventType)
            : Chains.GetOrAdd(eventType, static known => Chain(known));

    private static IReadOnlyList<Type> Chain(Type eventType)
    {
        var contracts = new List<Type>();

        for (var current = eventType;
             current is not null && typeof(IDomainEvent).IsAssignableFrom(current);
             current = current.BaseType)
        {
            contracts.Add(current);
        }

        contracts.AddRange(
            eventType.GetInterfaces()
                .Where(typeof(IDomainEvent).IsAssignableFrom)
                .OrderBy(contract => contract.FullName ?? contract.Name, StringComparer.Ordinal));

        if (!contracts.Contains(typeof(IDomainEvent)))
        {
            contracts.Add(typeof(IDomainEvent));
        }

        return [.. contracts.Distinct().Where(static contract => !ReachesCollectibleCode(contract))];
    }

    /// <summary>Whether a type names collectible code anywhere in it.</summary>
    /// <remarks>
    /// A generic definition in a permanent assembly closed over an extension's type is itself permanent, so
    /// <see cref="Type.Assembly"/> alone would call <c>Envelope&lt;PluginThing&gt;</c> safe to keep; holding
    /// it pins the extension's load context for the life of the process.
    /// </remarks>
    private static bool ReachesCollectibleCode(Type type)
    {
        if (type.Assembly.IsCollectible)
        {
            return true;
        }

        if (type.HasElementType)
        {
            return ReachesCollectibleCode(type.GetElementType()!);
        }

        foreach (var argument in type.GenericTypeArguments)
        {
            if (ReachesCollectibleCode(argument))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the typed call once per contract, so dispatch is a delegate rather than reflection.</summary>
    private static Invoker MakeInvoker<TEvent>()
        where TEvent : IDomainEvent
        => static (handler, domainEvent, token) => ((IEventHandler<TEvent>)handler).HandleAsync((TEvent)domainEvent, token);

    /// <summary>
    /// Runs one handler, containing anything it does short of ending the process.
    /// </summary>
    /// <remarks>
    /// Only the caller's own cancellation propagates: a handler's own timeout is a handler failure, and
    /// treating it as the caller giving up would drop every handler after it. The failure is logged as
    /// rendered text rather than as the exception object, which an extension defined and an asynchronous
    /// log could hold past teardown.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A handler is third-party code and one broken subscriber must not stop the others; the failure is recorded against that handler and process-fatal conditions still propagate.")]
    private async Task InvokeAsync(
        object handler,
        Func<Task> invoke,
        Type eventType,
        CancellationToken cancellationToken)
    {
        try
        {
            await invoke().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            Report(handler, eventType, failure);
        }
    }

    /// <summary>
    /// Writes one handler failure to the log, as text the host owns.
    /// </summary>
    /// <remarks>
    /// Reporting is contained in its turn: an extension may override <see cref="Exception.Message"/> to
    /// throw, and a sink behind the log may fail on its own account. Neither is a reason to stop calling
    /// the handlers that had nothing to do with it.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Reporting a contained failure must not itself become the failure that stops the fan-out; process-fatal conditions still propagate.")]
    private void Report(object handler, Type eventType, Exception failure)
    {
        try
        {
            HandlerFailed(
                _log,
                Rendered(handler.GetType()),
                Rendered(eventType),
                Rendered(failure.GetType()),
                MessageOf(failure));
        }
        catch (Exception unreportable) when (!ProcessFailure.IsFatal(unreportable))
        {
            // Nowhere left to say it: the log is what says things.
        }
    }

    /// <summary>The name of a type as text the host owns, so no log holds the type itself.</summary>
    private static string Rendered(Type type) => Bounded(type.FullName ?? type.Name);

    /// <summary>
    /// The failure's message, which an extension may have overridden to throw, or to answer with nothing.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Rendering a failure for the log must not itself become the failure that stops the fan-out; process-fatal conditions still propagate.")]
    private static string MessageOf(Exception failure)
    {
        try
        {
            // A getter that answers null is as much the extension's prerogative as one that throws, and the
            // report is made either way: it is what says a handler failed and that the others still ran.
            return failure.Message is { } message ? Bounded(message) : "<the failure stated no message>";
        }
        catch (Exception unreadable) when (!ProcessFailure.IsFatal(unreadable))
        {
            return $"<the failure's own message threw {unreadable.GetType().Name}>";
        }
    }

    /// <summary>Caps text an extension supplied. A message is a log line, not a payload.</summary>
    private static string Bounded(string text)
        => text.Length <= MaxLoggedLength ? text : string.Concat(text.AsSpan(0, MaxLoggedLength), "…");

    [LoggerMessage(
        EventId = 9300,
        Level = LogLevel.Error,
        Message = "Event handler '{Handler}' failed while handling '{EventType}': {FailureType}: {FailureMessage}. The remaining handlers still ran.")]
    private static partial void HandlerFailed(
        ILogger logger,
        string handler,
        string eventType,
        string failureType,
        string failureMessage);
}
