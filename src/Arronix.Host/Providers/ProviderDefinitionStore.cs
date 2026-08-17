using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;

// Provider, event and error contracts are experimental; the store holds definitions and announces changes.
#pragma warning disable ARX0003
#pragma warning disable ARX0004
#pragma warning disable ARX0015

namespace Arronix.Host.Providers;

/// <summary>
/// The operator's configured provider instances.
/// </summary>
/// <remarks>
/// <para>
/// A definition whose implementation has gone missing is marked orphaned and kept. That is the single most
/// important behavior in this file, and it is the deliberate inversion of a surveyed one: an application
/// that deletes stored definitions whose implementation vanished is safe only while implementations cannot
/// come and go. Under an extension model they can, and deletion means that uninstalling an extension for ten
/// minutes destroys every credential the operator entered into it.
/// </para>
/// <para>
/// Changes are announced as domain events so that cross-cutting concerns — the health aggregator's cache,
/// the affordance rebuild, a consumer's live view — attach without the store knowing about any of them. The
/// family travels as a value rather than as a generic type argument, which is what lets the announcement
/// cross an extension boundary at all.
/// </para>
/// </remarks>
public sealed class ProviderDefinitionStore
{
    private readonly ConcurrentDictionary<int, ProviderDefinition> _definitions = new();
    private readonly ProviderRegistry _providers;
    private readonly IEventPublisher? _events;
    private readonly TimeProvider _clock;
    private int _nextId;

    /// <summary>
    /// Creates a store.
    /// </summary>
    /// <param name="providers">The registry orphan detection compares against.</param>
    /// <param name="events">The publisher changes are announced on, when the host has one.</param>
    /// <param name="clock">The clock announcements are stamped with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> is <see langword="null"/>.</exception>
    public ProviderDefinitionStore(
        ProviderRegistry providers,
        IEnumerable<IEventPublisher> events,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);

        _providers = providers;
        _events = events.FirstOrDefault();
        _clock = clock;
    }

    /// <summary>
    /// Gets every definition, ordered by identifier.
    /// </summary>
    public IReadOnlyList<ProviderDefinition> All
        => [.. _definitions.Values.OrderBy(definition => definition.Id)];

    /// <summary>
    /// Lists definitions, narrowed.
    /// </summary>
    /// <param name="family">The family wanted, or <see langword="null"/> for all of them.</param>
    /// <param name="mediaKind">
    /// The media kind wanted. A definition that names no media kind serves every one of them.
    /// </param>
    /// <param name="enabledOnly">Whether to omit disabled and orphaned definitions.</param>
    /// <returns>The definitions.</returns>
    public IReadOnlyList<ProviderDefinition> Query(
        ProviderFamily? family = null,
        MediaKindId? mediaKind = null,
        bool enabledOnly = false)
        =>
        [
            .. All
                .Where(definition => family is null || definition.Family == family)
                .Where(definition => mediaKind is null
                    || definition.MediaKinds.Count == 0
                    || definition.MediaKinds.Contains(mediaKind.Value))
                .Where(definition => !enabledOnly
                    || (definition.Enabled && definition.State == DefinitionState.Active)),
        ];

    /// <summary>
    /// Looks up a definition.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The definition, or <see langword="null"/> when there is none.</returns>
    public ProviderDefinition? Find(int id) => _definitions.GetValueOrDefault(id);

    /// <summary>
    /// Gets a definition.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArronixException">There is no such definition.</exception>
    public ProviderDefinition Require(int id)
        => Find(id) ?? throw new ArronixException(
            CoreErrorCode.InvalidConfiguration,
            $"No provider definition {id} exists.");

    /// <summary>
    /// Adds a definition, assigning it an identifier.
    /// </summary>
    /// <param name="definition">The definition. Its identifier is replaced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public async Task<ProviderDefinition> AddAsync(
        ProviderDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var stored = definition with { Id = Interlocked.Increment(ref _nextId) };
        _definitions[stored.Id] = stored;

        await AnnounceAsync(stored, ProviderChangeKind.Added, cancellationToken).ConfigureAwait(false);
        return stored;
    }

    /// <summary>
    /// Replaces a definition.
    /// </summary>
    /// <param name="definition">The definition, carrying the identifier of the one it replaces.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArronixException">There is no definition with that identifier.</exception>
    public async Task<ProviderDefinition> UpdateAsync(
        ProviderDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _ = Require(definition.Id);

        _definitions[definition.Id] = definition;
        await AnnounceAsync(definition, ProviderChangeKind.Updated, cancellationToken).ConfigureAwait(false);
        return definition;
    }

    /// <summary>
    /// Removes a definition.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when there was one to remove.</returns>
    public async Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!_definitions.TryRemove(id, out var removed))
        {
            return false;
        }

        await AnnounceAsync(removed, ProviderChangeKind.Removed, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Marks definitions whose implementation is no longer registered, and restores those whose
    /// implementation has come back.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many definitions changed state.</returns>
    /// <remarks>
    /// Run after every change to the set of loaded extensions. Restoring is as important as marking: an
    /// extension that was quarantined and then fixed must not leave its operator re-entering credentials.
    /// </remarks>
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var changed = 0;

        foreach (var definition in All)
        {
            var present = _providers.TryGet(definition.Provider, out _);
            var wanted = present ? DefinitionState.Active : DefinitionState.Orphaned;

            if (definition.State == wanted)
            {
                continue;
            }

            var updated = definition with
            {
                State = wanted,
                Message = present
                    ? null
                    : new ProviderMessage(
                        $"The extension providing '{definition.Provider}' is not loaded. This definition is kept and will start working again when it is.",
                        ProviderMessageSeverity.Warning),
            };

            _definitions[updated.Id] = updated;
            changed++;

            await AnnounceAsync(
                updated,
                present ? ProviderChangeKind.Updated : ProviderChangeKind.Orphaned,
                cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    private async Task AnnounceAsync(
        ProviderDefinition definition,
        ProviderChangeKind change,
        CancellationToken cancellationToken)
    {
        if (_events is null)
        {
            return;
        }

        var announcement = new ProviderDefinitionChanged(
            Guid.NewGuid(),
            _clock.GetUtcNow(),
            Arronix.Host.Scheduling.CorrelationContext.Current,
            definition.Family,
            definition.Id,
            change);

        await _events.PublishAsync(announcement, cancellationToken).ConfigureAwait(false);
    }
}
