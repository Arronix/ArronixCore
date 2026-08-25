using System.Collections.Frozen;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Providers;

namespace Arronix.Plugins.Registration;

/// <summary>
/// The closed set of platform events an extension may subscribe to.
/// </summary>
/// <remarks>
/// Membership is listed, not derived from the contract assembly: adding an event to Abstractions must not
/// widen what extensions can read until its payload has been reviewed and it is added here.
/// </remarks>
internal static class PlatformEvents
{
    private static readonly FrozenSet<Type> Admitted = new[]
    {
        typeof(ProviderDefinitionChanged),
    }.ToFrozenSet();

    /// <summary>The admitted events, for the tests that hold this list to its own rules.</summary>
    internal static IReadOnlyCollection<Type> All => Admitted;

    /// <summary>Whether an extension may subscribe to an event because the platform declares it.</summary>
    /// <param name="eventType">The exact event type a subscription named.</param>
    /// <returns><see langword="true"/> when the type is on the list.</returns>
    internal static bool Admits(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return Admitted.Contains(eventType);
    }

    /// <summary>Whether the contract assembly declares the type. Used to word a refusal exactly.</summary>
    internal static bool IsContractEvent(Type eventType)
        => eventType.Assembly == typeof(IDomainEvent).Assembly;
}
