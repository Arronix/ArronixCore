using Arronix.Abstractions.Events;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Announces that a provider definition was added, changed, removed or quarantined.
/// </summary>
/// <param name="EventId">Identifier of this occurrence.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="CorrelationId">The wider operation it belongs to, when there was one.</param>
/// <param name="Family">The kind of external service the definition configures.</param>
/// <param name="DefinitionId">The definition that changed.</param>
/// <param name="Change">What happened to it.</param>
/// <remarks>
/// Publishing the change as an event is what lets cross-cutting concerns — health, caches, schedules —
/// react without the provider knowing they exist. <paramref name="Family"/> is a value rather than a
/// generic type argument, and that is what makes the event able to cross an extension boundary at all: a
/// generic event cannot be handled by code that does not reference the type argument.
/// </remarks>
public sealed record ProviderDefinitionChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    ProviderFamily Family,
    int DefinitionId,
    ProviderChangeKind Change) : IDomainEvent;

/// <summary>
/// What happened to a provider definition.
/// </summary>
public enum ProviderChangeKind
{
    /// <summary>The definition was created.</summary>
    Added = 0,

    /// <summary>The definition's settings changed.</summary>
    Updated = 1,

    /// <summary>The definition was deleted.</summary>
    Removed = 2,

    /// <summary>The definition's health changed without its settings changing.</summary>
    StatusChanged = 3,

    /// <summary>The definition's implementation went away, and the definition was retained.</summary>
    Orphaned = 4
}
