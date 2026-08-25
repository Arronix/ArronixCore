using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;


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
/// The test is where the event type came from, not what it is called. A type the contract assembly defines
/// is a platform event and is always publishable; so is one declared by an assembly this package owns. An
/// assembly it can merely see — a dependency's published contract, or a private assembly that happens to
/// share its load context — belongs to somebody else, so publishing its events is a forgery, refused as an
/// isolation violation rather than as a missing privilege: no capability would make it acceptable.
/// </para>
/// </remarks>
public sealed class FilteredEventPublisher : IEventPublisher
{
    private static readonly System.Reflection.Assembly ContractAssembly = typeof(IDomainEvent).Assembly;

    private readonly IEventPublisher _inner;
    private readonly PackageOwnership? _ownership;
    private readonly PluginInvocationLifetime? _invocation;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilteredEventPublisher"/> class.
    /// </summary>
    /// <param name="inner">The platform's publisher.</param>
    /// <param name="plugin">The extension publishing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A publisher built this way imposes only the platform rule. That is the in-process case — a module the
    /// host constructed itself, or a test — where there is no package to own anything.
    /// </remarks>
    public FilteredEventPublisher(IEventPublisher inner, PluginId plugin)
        : this(inner, plugin, ownership: null, invocation: null)
    {
    }

    internal FilteredEventPublisher(
        IEventPublisher inner,
        PluginId plugin,
        PackageOwnership? ownership,
        PluginInvocationLifetime? invocation)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        Plugin = plugin;
        _ownership = ownership;
        _invocation = invocation;
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

        return _ownership is null || _ownership.Owns(eventType);
    }

    /// <inheritdoc />
    /// <exception cref="PluginIsolationException">
    /// The event type belongs neither to the platform nor to this extension.
    /// </exception>
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();

        if (!MayPublish(eventType))
        {
            throw new PluginIsolationException(eventType.Assembly.GetName().Name ?? eventType.FullName!, Plugin.ToString());
        }

        if (_invocation is null)
        {
            await _inner.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The event carries this extension's own type. Held for the whole fan-out, so teardown cannot
        // unload the assembly that defines it while a handler is still reading it.
        if (!_invocation.TryEnter(out var lease))
        {
            throw new InvalidOperationException(
                $"Extension '{Plugin}' published an event after its runtime was withdrawn.");
        }

        using (lease)
        {
            await _inner.PublishAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
