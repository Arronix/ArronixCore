using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Providers;
using FluentAssertions;

namespace Arronix.Host.Tests.Storage;

/// <summary>
/// What an operator's configured providers survive, what is derived rather than stored, and the order
/// subscribers are told things in.
/// </summary>
[TestFixture]
internal sealed class DurableProviderDefinitionTests
{
    private static readonly MediaKindId Works = MediaKindId.FromString("works");

    private DurableStoreFixture _store = null!;

    [SetUp]
    public void SetUp() => _store = new DurableStoreFixture();

    [TearDown]
    public void TearDown() => _store.Dispose();

    /// <summary>Everything the operator stated comes back, including what they entered by hand.</summary>
    [Test]
    public async Task ADefinitionSurvivesARestartWithItsSettingsKindsAndTags()
    {
        var registry = new ProviderRegistry();
        var id = Register(registry);
        using var first = Store(registry);

        var added = await first.AddAsync(new ProviderDefinition
        {
            Id = 0,
            Provider = id,
            Family = ProviderFamily.Cataloger,
            Name = "TMDb",
            Priority = 7,
            Enabled = false,
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["baseUrl"] = "https://example.invalid",
            },
            MediaKinds = [Works],
            Tags = ["primary", "eu"],
        });

        _store.Reopen();
        using var restarted = Store(registry);
        var read = restarted.Require(added.Id);

        Assert.Multiple(() =>
        {
            read.Name.Should().Be("TMDb");
            read.Priority.Should().Be(7);
            read.Enabled.Should().BeFalse();
            read.Settings.Should().Contain("baseUrl", "https://example.invalid");
            read.MediaKinds.Should().Equal(Works);
            read.Tags.Should().Equal("primary", "eu");
        });
    }

    /// <summary>
    /// Whether an implementation is loaded is answered by the registry on every read, never by what was
    /// stored.
    /// </summary>
    [Test]
    public async Task PresenceIsDerivedOnEveryReadRatherThanStored()
    {
        var loaded = new ProviderRegistry();
        var id = RegisterOptionalOnly(loaded);
        using var first = Store(loaded);
        var added = await first.AddAsync(Definition(id));

        // A restart into a host where the extension is absent, then one where it is back.
        _store.Reopen();
        using var missing = Store(new ProviderRegistry());
        var whileMissing = missing.Require(added.Id);

        _store.Reopen();
        var reloaded = new ProviderRegistry();
        RegisterOptionalOnly(reloaded);
        using var back = Store(reloaded);
        var whenBack = back.Require(added.Id);

        Assert.Multiple(() =>
        {
            added.State.Should().Be(DefinitionState.Active);
            whileMissing.State.Should().Be(DefinitionState.Orphaned);
            whileMissing.Message.Should().NotBeNull("an operator is told why it is not working");
            whenBack.State.Should().Be(DefinitionState.Active, "and it starts working again on its own");
            whenBack.Message.Should().BeNull();
        });
    }

    /// <summary>A caller cannot store an answer to a question only the registry can answer.</summary>
    [Test]
    public async Task ACallerSuppliedPresenceIsNotStored()
    {
        var registry = new ProviderRegistry();
        var id = RegisterOptionalOnly(registry);
        using var store = Store(registry);

        var added = await store.AddAsync(Definition(id) with
        {
            State = DefinitionState.Orphaned,
            Message = new ProviderMessage("stale", ProviderMessageSeverity.Error),
        });

        _store.Reopen();
        using var restarted = Store(registry);

        Assert.Multiple(() =>
        {
            added.State.Should().Be(DefinitionState.Active, "the registry has it, whatever the caller said");
            restarted.Require(added.Id).State.Should().Be(DefinitionState.Active);
            restarted.Require(added.Id).Message.Should().BeNull();
        });
    }

    /// <summary>
    /// An update and a removal issued together leave the store and the file agreeing about what happened.
    /// </summary>
    [Test]
    public async Task AConcurrentUpdateAndRemovalConvergeInMemoryAndOnDisk()
    {
        var registry = new ProviderRegistry();
        var id = Register(registry);
        using var store = Store(registry);
        var added = await store.AddAsync(Definition(id));

        var update = Task.Run(async () =>
        {
            try
            {
                await store.UpdateAsync(added with { Name = "renamed" });
                return true;
            }
            catch (Abstractions.Errors.ArronixException)
            {
                // The removal won; the update is refused rather than resurrecting the definition.
                return false;
            }
        });

        var removal = Task.Run(() => store.RemoveAsync(added.Id));

        await Task.WhenAll(update, removal);

        _store.Reopen();
        using var restarted = Store(registry);

        Assert.Multiple(() =>
        {
            removal.Result.Should().BeTrue();
            store.Find(added.Id).Should().BeNull("the removal is what the store ended up holding");
            restarted.Find(added.Id).Should().BeNull("and the file agrees with it");
        });
    }

    /// <summary>
    /// Subscribers are told about changes in the order the changes were committed, not in the order the
    /// publishes happened to finish.
    /// </summary>
    /// <remarks>
    /// The bus is held open on the first announcement, and the removal is allowed to commit while it is
    /// held. Publishing after releasing the mutation gate would let the removal overtake the update and
    /// tell a subscriber that an entry which is gone was just renamed.
    /// </remarks>
    [Test]
    public async Task AnUpdateAndARemovalAreAnnouncedInTheOrderTheyCommitted()
    {
        var registry = new ProviderRegistry();
        var id = Register(registry);
        var bus = new HeldBus();
        using var store = new ProviderDefinitionStore(registry, bus, TimeProvider.System, _store.Definitions());
        var added = await store.AddAsync(Definition(id));

        bus.HoldNext();
        var update = store.UpdateAsync(added with { Name = "renamed" });
        await bus.Held;

        var removal = store.RemoveAsync(added.Id);

        // The removal has committed while the update's announcement is still in flight, which is the
        // interleaving that used to reorder the two events.
        while (store.Find(added.Id) is not null)
        {
            await Task.Yield();
        }

        bus.Release();
        await Task.WhenAll(update, removal);
        await bus.Delivered(3);

        bus.Published.Should().Equal(
            [ProviderChangeKind.Added, ProviderChangeKind.Updated, ProviderChangeKind.Removed]);
    }

    /// <summary>A reconcile takes the same gate, so its observation cannot land after a removal.</summary>
    [Test]
    public async Task AReconcileAndARemovalAreAnnouncedInTheOrderTheyCommitted()
    {
        var registry = new ProviderRegistry();
        var id = Register(registry);
        var bus = new HeldBus();
        using var store = new ProviderDefinitionStore(registry, bus, TimeProvider.System, _store.Definitions());
        var added = await store.AddAsync(Definition(id));

        bus.HoldNext();
        var reconcile = store.ReconcileAsync();
        await bus.Held;

        var removal = store.RemoveAsync(added.Id);

        while (store.Find(added.Id) is not null)
        {
            await Task.Yield();
        }

        bus.Release();
        await Task.WhenAll(reconcile, removal);
        await bus.Delivered(3);

        bus.Published.Should().Equal(
            [ProviderChangeKind.Added, ProviderChangeKind.Updated, ProviderChangeKind.Removed]);
    }

    /// <summary>
    /// A subscriber that changes the store from inside its own handler does not wait for itself.
    /// </summary>
    /// <remarks>
    /// One reader publishes announcements in commit order, so a handler running on that reader cannot wait
    /// for the announcement of a change it makes: the reader is the handler. The change is committed and
    /// queued regardless, which is the part order depends on.
    /// </remarks>
    [Test]
    [Repeat(20)]
    public async Task ASubscriberThatChangesTheStoreFromItsOwnHandlerDoesNotDeadlock()
    {
        var registry = new ProviderRegistry();
        var id = Register(registry);
        var bus = new ReentrantBus();
        using var store = new ProviderDefinitionStore(registry, bus, TimeProvider.System, _store.Definitions());

        bus.OnFirst = async () => await store.AddAsync(Definition(id) with { Name = "from the handler" });

        var added = await store.AddAsync(Definition(id)).WaitAsync(TimeSpan.FromSeconds(10));
        await bus.Delivered(2).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            added.Id.Should().Be(1);
            store.All.Select(definition => definition.Name)
                .Should().Equal(["Configured", "from the handler"], "both changes are stored");
            bus.Published.Should().HaveCount(2, "and both were announced");
        });
    }

    /// <summary>
    /// A change already writing to the journal when shutdown begins finishes committing and announcing.
    /// </summary>
    /// <remarks>
    /// The window this rules out is between committing and queueing. It is forced rather than raced for:
    /// the journal is held open while the mutation owns the gate, disposal is started and must wait for
    /// that gate, and only then is the write released.
    /// </remarks>
    [Test]
    public async Task AChangeCommittingWhenShutdownBeginsStillCommitsAndAnnounces()
    {
        var registry = new ProviderRegistry();
        var id = RegisterOptionalOnly(registry);
        var bus = new HeldBus();
        var journal = new HeldJournal(_store.Definitions());
        var store = new ProviderDefinitionStore(registry, bus, TimeProvider.System, journal);

        journal.HoldNext();
        var adding = store.AddAsync(Definition(id));
        await journal.Held;

        // Disposal now waits on the gate this add is holding, which is the interleaving under test.
        var shutdown = Task.Run(store.Dispose);
        await Task.Delay(20);

        journal.Release();
        var added = await adding;
        await shutdown;

        _store.Reopen();
        using var restarted = Store(registry);

        Assert.Multiple(() =>
        {
            added.Id.Should().Be(1, "the change that was mid-commit completed");
            restarted.All.Should().ContainSingle("and is in the file");
            bus.Published.Should().Equal(
                [ProviderChangeKind.Added],
                "and shutdown delivered the announcement it had already accepted");
        });
    }

    /// <summary>A change that begins after shutdown is refused before it reaches the journal.</summary>
    [Test]
    public async Task AChangeBeginningAfterShutdownIsRefusedBeforeItIsWritten()
    {
        var registry = new ProviderRegistry();
        var id = RegisterOptionalOnly(registry);
        var journal = new HeldJournal(_store.Definitions());
        var store = new ProviderDefinitionStore(registry, new SilentBus(), TimeProvider.System, journal);

        store.Dispose();

        var act = async () => await store.AddAsync(Definition(id));

        await act.Should().ThrowAsync<ObjectDisposedException>();

        _store.Reopen();
        using var restarted = Store(registry);

        Assert.Multiple(() =>
        {
            journal.Writes.Should().Be(0, "it never reached the journal");
            restarted.All.Should().BeEmpty("so there is nothing in the file either");
        });
    }

    /// <summary>
    /// A caller that keeps its own dictionary and changes it later does not change what was committed.
    /// </summary>
    /// <remarks>
    /// A definition held by reference would let a caller edit this store's state and the file's state apart
    /// from each other, with no change to announce and nothing able to notice.
    /// </remarks>
    [Test]
    public async Task ChangingTheCollectionsAfterAnAddDoesNotChangeWhatWasCommitted()
    {
        var registry = new ProviderRegistry();
        var id = RegisterOptionalOnly(registry);
        using var store = Store(registry);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseUrl"] = "https://example.invalid",
        };

        var tags = new List<string> { "first" };
        var kinds = new List<MediaKindId> { Works };

        var added = await store.AddAsync(Definition(id) with
        {
            Settings = settings,
            Tags = tags,
            MediaKinds = kinds,
        });

        settings["baseUrl"] = "https://elsewhere.invalid";
        settings["injected"] = "value";
        tags.Add("second");
        kinds.Clear();

        var held = store.Require(added.Id);

        _store.Reopen();
        using var restarted = Store(registry);
        var read = restarted.Require(added.Id);

        Assert.Multiple(() =>
        {
            held.Settings.Should().Contain("baseUrl", "https://example.invalid");
            held.Settings.Should().NotContainKey("injected");
            held.Tags.Should().Equal(["first"]);
            held.MediaKinds.Should().Equal([Works]);

            read.Settings.Should().Contain("baseUrl", "https://example.invalid");
            read.Tags.Should().Equal(["first"]);
            read.MediaKinds.Should().Equal([Works]);
        });
    }

    /// <summary>
    /// A settings key the loaded provider does not declare is never written down.
    /// </summary>
    /// <remarks>
    /// This store is where a value becomes plain text on a disk, so it decides from what the provider
    /// declares rather than trusting that somebody upstream classified the key. A key nobody declared is
    /// precisely the one nobody has said is safe to read back.
    /// </remarks>
    [Test]
    public async Task ASettingTheProviderDoesNotDeclareIsNeverWrittenDown()
    {
        var registry = new ProviderRegistry();
        var id = RegisterOptionalOnly(registry);
        using var store = Store(registry);

        await store.AddAsync(Definition(id) with
        {
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["baseUrl"] = "https://example.invalid",
                ["apiKey"] = "undeclared-token-value",
            },
        });

        using var raw = _store.Read();
        var written = raw.ProviderDefinitionSettings.ToList();

        Assert.Multiple(() =>
        {
            written.Select(setting => setting.FieldId).Should().Equal(["baseUrl"]);
            written.Should().NotContain(setting =>
                setting.Value.Contains("undeclared-token-value", StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// A handler for one store calling another is an ordinary caller of that other store.
    /// </summary>
    /// <remarks>
    /// The re-entrancy that must not wait is a store waiting for its own reader. Two independent stores
    /// share no reader, so the second must deliver normally — a marker that recorded only "something is
    /// announcing" would make the second skip its wait and report a delivery that had not happened.
    /// </remarks>
    [Test]
    [Repeat(20)]
    public async Task AHandlerForOneStoreIsAnOrdinaryCallerOfAnother()
    {
        var registry = new ProviderRegistry();
        var id = RegisterOptionalOnly(registry);
        var second = new DurableStoreFixture();

        try
        {
            var otherBus = new HeldBus();
            using var other = new ProviderDefinitionStore(
                registry, otherBus, TimeProvider.System, second.Definitions());

            await other.AddAsync(Definition(id));
            await otherBus.Delivered(1);

            var bus = new ReentrantBus();
            using var store = new ProviderDefinitionStore(
                registry, bus, TimeProvider.System, _store.Definitions());

            var reconciled = 0;
            bus.OnFirst = async () => reconciled = await other.ReconcileAsync();

            await store.AddAsync(Definition(id)).WaitAsync(TimeSpan.FromSeconds(10));
            await bus.Delivered(1).WaitAsync(TimeSpan.FromSeconds(10));

            // The reconcile ran to completion inside the other store's handler rather than deadlocking or
            // skipping its own delivery.
            reconciled.Should().Be(1);
            otherBus.Published.Should().HaveCount(2);
        }
        finally
        {
            second.Dispose();
        }
    }

    /// <summary>Shutdown delivers what was already queued, in order, before it lets go.</summary>
    [Test]
    public async Task DisposingDeliversTheChangesAlreadyQueued()
    {
        var registry = new ProviderRegistry();
        var id = RegisterOptionalOnly(registry);
        var bus = new HeldBus();
        var store = new ProviderDefinitionStore(registry, bus, TimeProvider.System, _store.Definitions());

        // Held on the first announcement, so the next two are queued behind it when disposal begins.
        bus.HoldNext();
        var added = await store.AddAsync(Definition(id));
        await bus.Held;
        await store.UpdateAsync(added with { Name = "renamed" });
        await store.RemoveAsync(added.Id);

        var shutdown = Task.Run(store.Dispose);
        bus.Release();
        await shutdown;

        bus.Published.Should().Equal(
            [ProviderChangeKind.Added, ProviderChangeKind.Updated, ProviderChangeKind.Removed],
            "a change that committed is announced even though shutdown had already begun");
    }

    private ProviderDefinitionStore Store(ProviderRegistry registry)
        => new(registry, new SilentBus(), TimeProvider.System, _store.Definitions());

    private static ProviderDefinition Definition(ProviderId provider) => new()
    {
        Id = 0,
        Provider = provider,
        Family = ProviderFamily.Cataloger,
        Name = "Configured",
        Settings = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    /// <summary>
    /// A value a field declares is never read back is never written down either.
    /// </summary>
    /// <remarks>
    /// <c>Credential</c> and <c>Secret</c> both declare that the value is never read back. Writing one to
    /// the local database would read it back, in plain text, off whatever medium that file sits on — a
    /// protection decision this vertical has no mandate to make. What an operator gets instead is a
    /// definition that survives with everything else intact and says which values it needs again.
    /// </remarks>
    [Test]
    public async Task ValuesAFieldNeverReadsBackAreNotWrittenDown()
    {
        var registry = new ProviderRegistry();
        var id = Register(registry);
        using var store = Store(registry);

        var added = await store.AddAsync(Definition(id) with
        {
            Settings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["baseUrl"] = "https://example.invalid",
                ["operator"] = "an-operator",
                ["password"] = "a-password",
                ["readAccessToken"] = "a-secret-token",
            },
        });

        using var raw = _store.Read();
        var written = raw.ProviderDefinitionSettings.ToList();

        _store.Reopen();
        using var restarted = Store(registry);
        var read = restarted.Require(added.Id);

        Assert.Multiple(() =>
        {
            added.Settings.Should().ContainKey("readAccessToken", "the running process was given it");

            written.Select(setting => setting.FieldId).Order(StringComparer.Ordinal)
                .Should().Equal(["baseUrl", "operator"], "and only what may be read back was written");

            written.Should().NotContain(setting => setting.Value.Contains("secret", StringComparison.Ordinal));
            written.Should().NotContain(setting => setting.Value.Contains("password", StringComparison.Ordinal));

            read.Settings.Should().ContainKey("baseUrl");
            read.Settings.Should().NotContainKey("password");
            read.Settings.Should().NotContainKey("readAccessToken");
        });
    }

    /// <summary>
    /// After a restart, a definition missing a value it requires is not usable, and one missing only an
    /// optional value is.
    /// </summary>
    /// <remarks>
    /// The distinction is what keeps the rule from being a blanket refusal: an optional credential that was
    /// never carried is the ordinary state of an optional setting, while a required one makes the
    /// definition something the platform must not route work to.
    /// </remarks>
    [TestCase(true, TestName = "ARestartLeavesADefinitionNeedingARequiredSecretIncomplete")]
    [TestCase(false, TestName = "ARestartLeavesADefinitionNeedingOnlyAnOptionalCredentialUsable")]
    public async Task ARestartReportsWhetherADefinitionIsStillUsable(bool needsRequiredSecret)
    {
        var registry = new ProviderRegistry();
        var id = needsRequiredSecret ? Register(registry) : RegisterOptionalOnly(registry);
        using var store = Store(registry);

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["baseUrl"] = "https://example.invalid",
            ["password"] = "an-optional-credential",
        };

        if (needsRequiredSecret)
        {
            settings["readAccessToken"] = "a-required-secret";
        }

        var added = await store.AddAsync(Definition(id) with { Settings = settings });

        _store.Reopen();
        using var restarted = Store(registry);
        var read = restarted.Require(added.Id);
        var routed = restarted.Query(ProviderFamily.Cataloger, enabledOnly: true);

        Assert.Multiple(() =>
        {
            if (needsRequiredSecret)
            {
                read.State.Should().Be(DefinitionState.Incomplete);
                read.Message!.Text.Should().Contain("readAccessToken");
                read.Message.Text.Should().NotContain("password", "an optional value is not a fault");
                routed.Should().BeEmpty("an incomplete definition is not routed work");
            }
            else
            {
                read.State.Should().Be(DefinitionState.Active, "nothing it requires is missing");
                read.Message.Should().BeNull();
                routed.Should().ContainSingle();
            }
        });
    }

    /// <summary>
    /// A readable field may survive a restart, but a definition that never supplied a required one is still
    /// incomplete before and after it. Requiredness is the provider's contract, not a secret-storage rule.
    /// </summary>
    [TestCase(SettingSensitivity.Public, "baseUrl")]
    [TestCase(SettingSensitivity.UserName, "operator")]
    public async Task ARequiredReadableSettingMissingInitiallyOrAfterARestartIsIncomplete(
        SettingSensitivity sensitivity,
        string fieldId)
    {
        var registry = new ProviderRegistry();
        var id = RegisterRequiredReadable(registry, sensitivity, fieldId);
        using var first = Store(registry);

        var added = await first.AddAsync(Definition(id));

        _store.Reopen();
        using var restarted = Store(registry);
        var read = restarted.Require(added.Id);

        Assert.Multiple(() =>
        {
            added.State.Should().Be(DefinitionState.Incomplete);
            added.Message!.Text.Should().Contain(fieldId);
            first.Query(ProviderFamily.Cataloger, enabledOnly: true).Should().BeEmpty();

            read.State.Should().Be(DefinitionState.Incomplete);
            read.Message!.Text.Should().Contain(fieldId);
            restarted.Query(ProviderFamily.Cataloger, enabledOnly: true).Should().BeEmpty();
        });
    }

    /// <summary>A provider that is not loaded cannot say which of its fields are secret, so none is kept.</summary>
    [Test]
    public async Task NothingIsWrittenForAProviderThatCannotSayWhichFieldsAreSecret()
    {
        using var store = Store(new ProviderRegistry());

        await store.AddAsync(Definition(ProviderId.Create(PluginId.FromString("absent"), "catalog")) with
        {
            Settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "a-value" },
        });

        using var raw = _store.Read();

        raw.ProviderDefinitionSettings.Should().BeEmpty(
            "an unknown descriptor is answered fail-safe, not by guessing which values are safe");
    }

    private static SettingsField Field(
        string id,
        string name,
        SettingSensitivity sensitivity,
        bool required = false)
        => new()
        {
            FieldId = id,
            Name = name,
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,
            Sensitivity = sensitivity,
            Required = required,
        };

    private static ProviderId Register(ProviderRegistry registry)
        => registry.Register(
            PluginId.FromString("tmdb"),
            ProviderFamily.Cataloger,
            Declared,
            new StubProvider(),
            typeof(TypedMedia.Work));

    private static ProviderId RegisterOptionalOnly(ProviderRegistry registry)
        => registry.Register(
            PluginId.FromString("simple"),
            ProviderFamily.Cataloger,
            new ProviderDescriptor
            {
                LocalId = "catalog",
                Name = "Catalog",
                Settings =
                [
                    Field("baseUrl", "Base URL", SettingSensitivity.Public),
                    Field("password", "Password", SettingSensitivity.Credential),
                ],
            },
            new StubProvider(),
            typeof(TypedMedia.Work));

    private static ProviderId RegisterRequiredReadable(
        ProviderRegistry registry,
        SettingSensitivity sensitivity,
        string fieldId)
        => registry.Register(
            PluginId.FromString("required.readable"),
            ProviderFamily.Cataloger,
            new ProviderDescriptor
            {
                LocalId = "catalog",
                Name = "Catalog",
                Settings = [Field(fieldId, fieldId, sensitivity, required: true)],
            },
            new StubProvider(),
            typeof(TypedMedia.Work));

    /// <summary>
    /// What the provider declares about its own settings, which is what says which are never read back.
    /// </summary>
    private static ProviderDescriptor Declared => new()
    {
        LocalId = "catalog",
        Name = "Catalog",
        Settings =
        [
            Field("baseUrl", "Base URL", SettingSensitivity.Public),
            Field("operator", "Operator", SettingSensitivity.UserName),

            // Required: without it the provider cannot work, so a restart leaves the definition unusable.
            Field("readAccessToken", "Read access token", SettingSensitivity.Secret, required: true),

            // Optional: its absence after a restart is the ordinary state of an optional setting.
            Field("password", "Password", SettingSensitivity.Credential),
        ],
    };

    private sealed class StubProvider : IProvider
    {
        public ProviderDescriptor Descriptor => Declared;

        public Task<ValidationOutcome> TestAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
            ProviderInvocation invocation,
            string sourceId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A bus whose first handler changes the store it is being told about.</summary>
    private sealed class ReentrantBus : IEventPublisher
    {
        private readonly ConcurrentQueue<ProviderChangeKind> _published = [];
        private int _completed;
        private int _first;

        internal Func<Task>? OnFirst { get; set; }

        internal IReadOnlyList<ProviderChangeKind> Published => [.. _published];

        internal async Task Delivered(int count)
        {
            for (var attempt = 0; attempt < 200 && Volatile.Read(ref _completed) < count; attempt++)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            if (domainEvent is ProviderDefinitionChanged changed)
            {
                _published.Enqueue(changed.Change);
            }

            if (Interlocked.CompareExchange(ref _first, 1, 0) == 0 && OnFirst is { } reentrant)
            {
                await reentrant().ConfigureAwait(false);
            }

            Interlocked.Increment(ref _completed);
        }
    }

    /// <summary>A journal that can be held open on one write, and counts what it was asked to do.</summary>
    private sealed class HeldJournal(IProviderDefinitionJournal inner) : IProviderDefinitionJournal
    {
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hold;
        private int _writes;

        internal Task Held => _held.Task;

        internal int Writes => Volatile.Read(ref _writes);

        internal void HoldNext() => Interlocked.Exchange(ref _hold, 1);

        internal void Release() => _release.TrySetResult();

        public IReadOnlyList<ProviderDefinition> Load() => inner.Load();

        public async ValueTask WriteAsync(
            ProviderDefinition definition,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _hold, 0, 1) == 1)
            {
                _held.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }

            Interlocked.Increment(ref _writes);
            await inner.WriteAsync(definition, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DeleteAsync(int id, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(id, cancellationToken);
    }

    private sealed class SilentBus : IEventPublisher
    {
        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
    }

    /// <summary>A bus that can be held open on one announcement, and remembers the order of all of them.</summary>
    private sealed class HeldBus : IEventPublisher
    {
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<ProviderChangeKind> _published = [];
        private int _hold;

        internal Task Held => _held.Task;

        internal IReadOnlyList<ProviderChangeKind> Published => [.. _published];

        internal void HoldNext() => Interlocked.Exchange(ref _hold, 1);

        internal void Release() => _release.TrySetResult();

        /// <summary>Waits until at least this many announcements have been delivered.</summary>
        internal async Task Delivered(int count)
        {
            for (var attempt = 0; attempt < 200 && _published.Count < count; attempt++)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            if (Interlocked.CompareExchange(ref _hold, 0, 1) == 1)
            {
                _held.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }

            if (domainEvent is ProviderDefinitionChanged changed)
            {
                _published.Enqueue(changed.Change);
            }
        }
    }
}
