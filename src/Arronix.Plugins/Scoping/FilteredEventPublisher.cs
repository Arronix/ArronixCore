using System.Runtime.Loader;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;


namespace Arronix.Plugins.Scoping;

/// <summary>
/// Confines what an extension may publish to its own namespace and the platform's.
/// </summary>
/// <remarks>
/// <para>
/// The event contract documents that an extension sees the platform's events and its own, never another
/// extension's. The subscription half of that is enforced by the host when it dispatches. This is the
/// publication half, and it is the sharper of the two: an extension that could publish an event type
/// belonging to another extension could make the platform act on a fact that extension never asserted.
/// </para>
/// <para>
/// The test is where the event type came from, not what it is called. A type defined in the contract
/// assembly is a platform event and is always publishable; a type that came out of this extension's own
/// load context is its own; anything else is a forgery, and is refused as an isolation violation rather
/// than as a missing privilege, because no capability would make it acceptable.
/// </para>
/// <para>
/// A publisher constructed without a load context imposes only the first rule. That is the in-process case
/// — a module the host constructed itself, or a test — where there is no isolation boundary to enforce and
/// pretending otherwise would be theater.
/// </para>
/// </remarks>
public sealed class FilteredEventPublisher : IEventPublisher
{
    private static readonly System.Reflection.Assembly ContractAssembly = typeof(IDomainEvent).Assembly;

    private readonly IEventPublisher _inner;
    private readonly AssemblyLoadContext? _owningContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilteredEventPublisher"/> class.
    /// </summary>
    /// <param name="inner">The platform's publisher.</param>
    /// <param name="plugin">The extension publishing.</param>
    /// <param name="owningContext">
    /// The extension's load context, or <see langword="null"/> when it was not loaded in isolation.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public FilteredEventPublisher(IEventPublisher inner, PluginId plugin, AssemblyLoadContext? owningContext = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        Plugin = plugin;
        _owningContext = owningContext;
    }

    /// <summary>
    /// Gets the extension publishing.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Determines whether the extension may publish an event type.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns><see langword="true"/> when publication is permitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventType"/> is <see langword="null"/>.</exception>
    public bool MayPublish(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (eventType.Assembly == ContractAssembly)
        {
            return true;
        }

        return _owningContext is null || AssemblyLoadContext.GetLoadContext(eventType.Assembly) == _owningContext;
    }

    /// <inheritdoc />
    /// <exception cref="PluginIsolationException">
    /// The event type belongs neither to the platform nor to this extension.
    /// </exception>
    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();

        if (!MayPublish(eventType))
        {
            throw new PluginIsolationException(eventType.Assembly.GetName().Name ?? eventType.FullName!, Plugin.ToString());
        }

        return _inner.PublishAsync(domainEvent, cancellationToken);
    }
}
