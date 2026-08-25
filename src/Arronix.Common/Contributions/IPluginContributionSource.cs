using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;

namespace Arronix.Common.Contributions;

/// <summary>One contributed value, the extension that registered it, and where in its order it came.</summary>
/// <typeparam name="TValue">What was contributed.</typeparam>
/// <param name="Owner">The extension that registered it, for ordering and owner-qualified diagnostics.</param>
/// <param name="Ordinal">Its position in that extension's registration order.</param>
/// <param name="Value">The contribution.</param>
internal readonly record struct PluginContribution<TValue>(PluginId Owner, int Ordinal, TValue Value);

/// <summary>
/// Contributions selected from the live extension runtime, safe to invoke until disposed.
/// </summary>
/// <typeparam name="TValue">What was selected.</typeparam>
/// <remarks>
/// Disposal must not be skipped: teardown waits for every outstanding lease before it disposes an
/// extension's objects or unloads its code.
/// </remarks>
internal interface IContributionLease<TValue> : IDisposable
{
    /// <summary>Gets the selection, ordered by owning extension and then by registration order.</summary>
    IReadOnlyList<PluginContribution<TValue>> Contributions { get; }
}

/// <summary>One extension-contributed event handler and the delegate that invokes it.</summary>
/// <param name="Handler">The handler instance, for identity comparison against a host registration.</param>
/// <param name="EventType">The type it subscribed to.</param>
/// <param name="Invoke">Invokes it, typed at the registration point rather than rediscovered here.</param>
internal sealed record EventHandlerContribution(
    object Handler,
    Type EventType,
    Func<IDomainEvent, CancellationToken, Task> Invoke);

/// <summary>
/// What the extension runtime is currently contributing, and the leases that keep it alive while used.
/// </summary>
/// <remarks>
/// The platform assembly cannot reference the extension runtime, so the runtime implements this and the
/// platform's event and telemetry pipelines consume it. Every method selects under the publication gate and
/// returns with the gate released, so nothing here is held across an <c>await</c> of extension code.
/// </remarks>
internal interface IPluginContributionSource
{
    /// <summary>Selects everything active extensions registered under one contract.</summary>
    /// <typeparam name="TContract">The registration contract.</typeparam>
    /// <returns>The lease. Never <see langword="null"/>; an empty lease is an ordinary result.</returns>
    IContributionLease<TContract> Acquire<TContract>()
        where TContract : class;

    /// <summary>
    /// Selects the active handlers eligible for one event type, applying the subscription boundary.
    /// </summary>
    /// <param name="eventType">The runtime type of the event being published.</param>
    /// <returns>The lease.</returns>
    IContributionLease<EventHandlerContribution> AcquireEventHandlers(Type eventType);

    /// <summary>
    /// Leases the extension that defines a type, so an object of it can be held past the current call.
    /// </summary>
    /// <param name="type">The type whose owner is wanted.</param>
    /// <param name="owner">The extension that owns it, when one does.</param>
    /// <param name="lease">The lease, when an active extension owns the type.</param>
    /// <returns><see langword="false"/> when no active extension owns it.</returns>
    /// <remarks>
    /// The telemetry queue uses this: a queued event can carry an exception whose type came from an
    /// extension, and holding it past teardown would pin a context the host has declared gone.
    /// </remarks>
    bool TryAcquireOwner(Type type, out PluginId owner, out IDisposable? lease);
}
