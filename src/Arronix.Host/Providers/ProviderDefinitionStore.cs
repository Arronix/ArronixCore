using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;


using Arronix.Common.Diagnostics;

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
/// A definition outlives the process that configured it: it is written down before it is served, so what an
/// operator configured is there after a restart. Its credentials and secrets are not. A field that declares
/// itself never read back is never written down either, so those values live only as long as the process
/// they were given to, and a definition that needs one back says so instead of being routed work.
/// </para>
/// <para>
/// Changes are announced as domain events so that cross-cutting concerns — the health aggregator's cache,
/// the affordance rebuild, a consumer's live view — attach without the store knowing about any of them. The
/// family travels as a value rather than as a generic type argument, which is what lets the announcement
/// cross an extension boundary at all.
/// </para>
/// </remarks>
public sealed class ProviderDefinitionStore : IDisposable
{
    private readonly ConcurrentDictionary<int, ProviderDefinition> _definitions = new();
    private readonly ProviderRegistry _providers;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _clock;
    private readonly IProviderDefinitionJournal? _journal;
    private readonly ConcurrentDictionary<int, DefinitionState> _announced = new();
    private readonly SemaphoreSlim _mutations = new(1, 1);

    /// <summary>
    /// Announcements in the order the changes they describe were committed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A change is queued while its mutation gate is still held, so the queue's order is the durable order,
    /// and one reader publishes them in that order outside the gate. Publishing after releasing the gate
    /// instead would let a fast removal overtake a slow update and tell subscribers that an entry which is
    /// gone was just changed.
    /// </para>
    /// <para>
    /// <b>What a mutation waits for.</b> A mutation returns once it is durably committed and its
    /// announcement is queued. It does not wait for delivery, and a subscriber that refuses cannot fail a
    /// change that is already stored: the refusal happened after the change was handed over, so reporting
    /// it as the change failing would describe a state the store is not in. Waiting would also deadlock the
    /// one reader the moment a subscriber's handler mutated this store, because the reader cannot reach
    /// that handler's own announcement until the handler returns.
    /// </para>
    /// </remarks>
    private readonly Channel<Announcement> _outbox =
        Channel.CreateUnbounded<Announcement>(new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _publishing;
    private int _nextId;
    private int _disposals;
    private bool _closed;

    /// <summary>How long shutdown waits for queued announcements to be delivered.</summary>
    private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(5);

    /// <summary>How long it then waits for a cancelled reader to stop.</summary>
    private static readonly TimeSpan AbandonLimit = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether the current flow is running inside a subscriber this store is announcing to.
    /// </summary>
    /// <remarks>
    /// Flows into a handler's continuations, so a handler that reconciles re-entrantly queues its
    /// announcements and returns rather than waiting for a reader that is waiting for it. It names the
    /// store, not the fact: a handler for one store calling another is not re-entrant, and must wait for
    /// that other store's own reader as any ordinary caller would.
    /// </remarks>
    private static readonly AsyncLocal<ProviderDefinitionStore?> Announcing = new();

    /// <summary>
    /// Creates a store.
    /// </summary>
    /// <param name="providers">The registry orphan detection compares against.</param>
    /// <param name="events">The bus changes are announced on.</param>
    /// <param name="clock">The clock announcements are stamped with.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The bus is required rather than optional: the platform supplies one to every host, and a store that
    /// silently announced nothing would leave health, caches and schedules stale with nothing to say so.
    /// </remarks>
    public ProviderDefinitionStore(
        ProviderRegistry providers,
        IEventPublisher events,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);

        _providers = providers;
        _events = events;
        _clock = clock;
        _publishing = Task.Run(PublishAsync);
    }

    /// <summary>
    /// Creates a store that reads back what a previous process configured and writes down what this one does.
    /// </summary>
    /// <param name="providers">The registry orphan detection compares against.</param>
    /// <param name="events">The bus changes are announced on.</param>
    /// <param name="clock">The clock announcements are stamped with.</param>
    /// <param name="journal">Where definitions are kept between processes.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The definitions are read here rather than by a later start step, so no caller can reach an empty
    /// store and conclude that the operator configured nothing.
    /// </remarks>
    internal ProviderDefinitionStore(
        ProviderRegistry providers,
        IEventPublisher events,
        TimeProvider clock,
        IProviderDefinitionJournal journal)
        : this(providers, events, clock)
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;

        foreach (var definition in journal.Load())
        {
            _definitions[definition.Id] = definition;
            _nextId = Math.Max(_nextId, definition.Id);
        }
    }

    /// <summary>Determines whether this store observes the exact provider registry supplied by its caller.</summary>
    internal bool Uses(ProviderRegistry providers) => ReferenceEquals(_providers, providers);

    /// <summary>
    /// Gets every definition, ordered by identifier, each stating whether its implementation is loaded now.
    /// </summary>
    /// <remarks>
    /// Presence is derived on every read rather than served from what was stored. Whether an extension is
    /// loaded changes without this store being told, so a stored answer is a stale answer, and a caller
    /// deciding whether to use a definition must not be handed one.
    /// </remarks>
    public IReadOnlyList<ProviderDefinition> All
        => [.. _definitions.Values.Select(Present).OrderBy(static definition => definition.Id)];

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
    public ProviderDefinition? Find(int id)
        => _definitions.TryGetValue(id, out var definition) ? Present(definition) : null;

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

        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);

        ProviderDefinition stored;

        try
        {
            RequireOpen();
            stored = Configured(definition) with { Id = Interlocked.Increment(ref _nextId) };

            // Written before it is served, so a definition a caller was told about is one a restart has.
            if (_journal is { } journal)
            {
                await journal.WriteAsync(Journaled(stored), cancellationToken).ConfigureAwait(false);
            }

            _definitions[stored.Id] = stored;
            Observe(Queue(stored, ProviderChangeKind.Added));
        }
        finally
        {
            _mutations.Release();
        }

        return Present(stored);
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

        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);

        ProviderDefinition stored;

        try
        {
            RequireOpen();

            // Checked inside the gate, so an update cannot pass a check that a concurrent removal has
            // already invalidated and then write a definition back into a store that had dropped it.
            if (!_definitions.ContainsKey(definition.Id))
            {
                throw new ArronixException(
                    CoreErrorCode.InvalidConfiguration,
                    $"No provider definition {definition.Id} exists.");
            }

            stored = Configured(definition);

            if (_journal is { } journal)
            {
                await journal.WriteAsync(Journaled(stored), cancellationToken).ConfigureAwait(false);
            }

            _definitions[stored.Id] = stored;
            Observe(Queue(stored, ProviderChangeKind.Updated));
        }
        finally
        {
            _mutations.Release();
        }

        return Present(stored);
    }

    /// <summary>
    /// Removes a definition.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when there was one to remove.</returns>
    public async Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            RequireOpen();

            if (!_definitions.TryGetValue(id, out var held))
            {
                return false;
            }

            if (_journal is { } journal)
            {
                await journal.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            }

            _definitions.TryRemove(id, out _);
            _announced.TryRemove(id, out _);
            Observe(Queue(held, ProviderChangeKind.Removed));
        }
        finally
        {
            _mutations.Release();
        }

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
        var queued = new List<Task>();

        // Taken under the same gate the mutations use, so an observation cannot be queued after a removal
        // that had already committed. Reading and queueing happen here; the subscribers run afterwards,
        // outside it, like every other announcement this store makes.
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var definition in All)
            {
                var present = definition.State != DefinitionState.Orphaned;

                // What is compared is what was last announced, not what is stored: presence is derived on
                // every read, so there is nothing stored to change. A definition nobody has been told about
                // yet has transitioned from unknown, which is what a restart needs to announce.
                if (_announced.TryGetValue(definition.Id, out var last) && last == definition.State)
                {
                    continue;
                }

                _announced[definition.Id] = definition.State;
                queued.Add(Queue(
                    definition,
                    present ? ProviderChangeKind.Updated : ProviderChangeKind.Orphaned));
            }
        }
        finally
        {
            _mutations.Release();
        }

        // A reconcile stores nothing, so it is the one caller that may wait: a subscriber refusing what it
        // observed is that caller's answer, and there is no committed change for the refusal to contradict.
        foreach (var announced in queued)
        {
            await DeliveredAsync(announced).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return queued.Count;
    }

    /// <summary>
    /// The definition as it may be written down: everything except the values a field declares are never
    /// read back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A settings field declares its own sensitivity, and <c>Credential</c> and <c>Secret</c> both declare
    /// that the value is never read back. Writing one to a file would read it back, in plain text, on
    /// whatever medium the database sits on — which is a protection decision this store has no mandate to
    /// make and no key management to make it with. So those values live for as long as the process that was
    /// given them, and no longer.
    /// </para>
    /// <para>
    /// A provider whose implementation is not loaded cannot say which of its fields those are, so nothing
    /// is written for it. Fail-safe: the cost is an operator re-entering configuration, and the cost of the
    /// other answer is a credential on disk nobody decided to put there.
    /// </para>
    /// </remarks>
    private ProviderDefinition Journaled(ProviderDefinition definition)
    {
        if (definition.Settings.Count == 0)
        {
            return definition;
        }

        if (!_providers.TryGet(definition.Provider, out var registered) || registered is null)
        {
            return definition with { Settings = new Dictionary<string, string>(StringComparer.Ordinal) };
        }

        // An allow-list, not a deny-list. This store is where a value becomes plain text on somebody's
        // disk, so it decides from what the loaded provider currently declares rather than trusting that a
        // caller filtered first: a key the descriptor does not name — a stale one, a drifted one, a
        // hand-written one — is exactly the key nobody has classified, and is not written.
        var readable = registered.Descriptor.Settings
            .Where(static field => field.Sensitivity is SettingSensitivity.Public or SettingSensitivity.UserName)
            .Select(static field => field.FieldId)
            .ToHashSet(StringComparer.Ordinal);

        return definition with
        {
            Settings = Snapshot(definition.Settings.Where(setting => readable.Contains(setting.Key))),
        };
    }

    /// <summary>
    /// Names the values a definition cannot work without and this store was not allowed to keep.
    /// </summary>
    /// <remarks>
    /// Required fields only. A provider that declares an optional credential works without it, so its
    /// absence after a restart is the ordinary state of an optional setting rather than a fault.
    /// </remarks>
    private IReadOnlyList<string> Missing(ProviderDefinition definition)
    {
        if (!_providers.TryGet(definition.Provider, out var registered) || registered is null)
        {
            return [];
        }

        return
        [
            .. registered.Descriptor.Settings
                .Where(static field => field.Required)
                .Where(static field => field.Sensitivity is SettingSensitivity.Credential or SettingSensitivity.Secret)
                .Where(field => string.IsNullOrEmpty(definition.Settings.GetValueOrDefault(field.FieldId)))
                .Select(static field => field.FieldId)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Strips the state a caller cannot decide, so nothing derived is ever written down.
    /// </summary>
    /// <remarks>
    /// Whether an implementation is loaded is the registry's answer and changes without this store being
    /// told. A caller that supplied one would be storing a guess that later reads would have to either
    /// believe or silently override.
    /// </remarks>
    private static ProviderDefinition Configured(ProviderDefinition definition)
        => definition with
        {
            State = DefinitionState.Active,
            Message = null,

            // Copied on the way in, because a caller keeps its own reference to whatever it passed. A
            // dictionary changed after the journal write would leave this store and the file describing
            // different definitions, with no change to announce and nothing to notice it.
            Settings = Snapshot(definition.Settings),
            Tags = Snapshot(definition.Tags),
            MediaKinds = Snapshot(definition.MediaKinds),
        };

    /// <summary>Takes a copy nothing outside this store holds a reference to.</summary>
    private static IReadOnlyDictionary<string, string> Snapshot(IEnumerable<KeyValuePair<string, string>> settings)
        => new ReadOnlyDictionary<string, string>(
            settings.ToDictionary(
                static setting => setting.Key,
                static setting => setting.Value,
                StringComparer.Ordinal));

    /// <summary>Takes a copy nothing outside this store holds a reference to.</summary>
    private static IReadOnlyList<TValue> Snapshot<TValue>(IEnumerable<TValue> values)
        => new ReadOnlyCollection<TValue>([.. values]);

    /// <summary>Answers one definition with the presence its implementation has right now.</summary>
    private ProviderDefinition Present(ProviderDefinition definition)
    {
        if (_providers.TryGet(definition.Provider, out _))
        {
            var missing = Missing(definition);

            if (missing.Count == 0)
            {
                return definition with { State = DefinitionState.Active, Message = null };
            }

            // Not Active: an Active definition is one the platform will route work to, and routing this one
            // would call a provider with settings already known to be incomplete. Said here rather than
            // discovered through a call that fails for a reason the operator cannot see.
            return definition with
            {
                State = DefinitionState.Incomplete,
                Message = new ProviderMessage(
                    $"This definition needs {string.Join(", ", missing)} entered again before it can be "
                    + "used. A value a provider declares as a credential or a secret is never read back, so "
                    + "it is not written down and does not survive a restart.",
                    ProviderMessageSeverity.Warning),
            };
        }

        return definition with
        {
            State = DefinitionState.Orphaned,
            Message = new ProviderMessage(
                $"The extension providing '{definition.Provider}' is not loaded. This definition is kept and "
                + "will start working again when it is.",
                ProviderMessageSeverity.Warning),
        };
    }

    /// <summary>Refuses a mutation this store can no longer announce.</summary>
    /// <remarks>
    /// Checked under the mutation gate, and shutdown closes the queue under that same gate, so a change
    /// that passes here cannot find the queue closed after it has committed. There is no outcome in which
    /// something is stored and its announcement was refused.
    /// </remarks>
    private void RequireOpen() => ObjectDisposedException.ThrowIf(_closed, this);

    /// <summary>
    /// Marks an announcement as observed, which is all this store can honestly do with one.
    /// </summary>
    /// <remarks>
    /// Reporting a publication failure is the publisher's job and it does it: the platform's
    /// <c>HostEventPublisher</c> contains a handler that throws, logs it, and completes the publish anyway,
    /// so a faulted task here means the publisher itself failed rather than a subscriber. This store has no
    /// reporting path of its own and does not pretend to — the continuation exists so a fault does not
    /// become an unobserved task exception, not because anything here will tell anyone about it.
    /// </remarks>
    private static void Observe(Task announcement)
        => _ = announcement.ContinueWith(
            static delivered => delivered.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <inheritdoc />
    /// <remarks>
    /// Shutdown closes the queue under the mutation gate, so no change can commit and then find nowhere to
    /// announce itself, and then lets the reader finish what is already queued so a change that committed is
    /// still delivered. The drain is bounded: a subscriber that never returns cannot hold shutdown open
    /// indefinitely, and past that bound the reader is cancelled and the remaining announcements are
    /// abandoned rather than waited on.
    /// </remarks>
    public void Dispose()
    {
        // Only the first call does anything, and every other returns without touching a primitive the first
        // one is about to dispose. Disposal is allowed to be repeated; it is not allowed to be concurrent
        // with itself.
        if (Interlocked.Exchange(ref _disposals, 1) != 0)
        {
            return;
        }

        _mutations.Wait();

        try
        {
            _closed = true;
            _outbox.Writer.TryComplete();
        }
        finally
        {
            _mutations.Release();
        }

        // Not cancelled first: cancelling here would drop announcements whose changes are already stored.
        if (!_publishing.Wait(DrainLimit))
        {
            _stopping.Cancel();
            _publishing.Wait(AbandonLimit);
        }

        _stopping.Dispose();
        _mutations.Dispose();
    }

    /// <summary>Publishes queued announcements, one at a time, in the order they were committed.</summary>
    private async Task PublishAsync()
    {
        try
        {
            await foreach (var pending in _outbox.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                var outer = Announcing.Value;
                Announcing.Value = this;

                try
                {
                    await _events.PublishAsync(pending.Change, _stopping.Token).ConfigureAwait(false);
                    pending.Published.TrySetResult();
                }
                catch (Exception failure)
                {
                    // Handed to whoever is waiting on this announcement rather than swallowed here. Only a
                    // caller that had nothing to commit — a reconcile — waits, so this never turns a stored
                    // change into a reported failure.
                    pending.Published.TrySetException(failure);
                }
                finally
                {
                    Announcing.Value = outer;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }

        while (_outbox.Reader.TryRead(out var abandoned))
        {
            abandoned.Published.TrySetCanceled();
        }
    }

    /// <summary>Queues one announcement in commit order.</summary>
    /// <returns>A task that completes when it has been published, for a caller entitled to wait.</returns>
    /// <exception cref="InvalidOperationException">
    /// The queue would not take it, which can only mean this store was disposed after its caller passed the
    /// disposal check. A change that is stored and cannot be announced is said rather than dropped.
    /// </exception>
    private Task Queue(ProviderDefinition definition, ProviderChangeKind change)
    {
        // Built here, not at publication: the correlation and the instant belong to the change, and the
        // reader that publishes it is nobody's caller.
        var pending = new Announcement(
            new ProviderDefinitionChanged(
                Guid.NewGuid(),
                _clock.GetUtcNow(),
                CorrelationContext.Current,
                definition.Family,
                definition.Id,
                change),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        return _outbox.Writer.TryWrite(pending)
            ? pending.Published.Task
            : throw new InvalidOperationException(
                $"Provider definition {definition.Id} was stored, and the announcement of that change could "
                + "not be queued because this store is shutting down.");
    }

    /// <summary>
    /// Waits for one announcement, unless waiting would be waiting for the caller itself.
    /// </summary>
    /// <remarks>
    /// A subscriber's handler runs on the one reader that publishes announcements, so a change made from
    /// inside a handler cannot wait for its own: the reader is that handler. Its announcement is queued in
    /// commit order either way, which is the part subscribers depend on.
    /// </remarks>
    private Task DeliveredAsync(Task announcement)
        => ReferenceEquals(Announcing.Value, this) ? Task.CompletedTask : announcement;

    /// <summary>One queued change, and the caller waiting to hear that it was announced.</summary>
    private sealed record Announcement(ProviderDefinitionChanged Change, TaskCompletionSource Published);
}
