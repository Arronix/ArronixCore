using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Telemetry;

namespace Arronix.Host.Tests.G02AdmissionFixture;

internal static class G02FixtureLifetimeSignals
{
    private static ITelemetryEmitter? _telemetry;
    private static TimeProvider? _clock;

    internal static void Initialize(ITelemetryEmitter telemetry, TimeProvider clock)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    internal static async ValueTask EmitAsync(string message)
    {
        var telemetry = _telemetry;
        var clock = _clock;

        if (telemetry is null || clock is null)
        {
            return;
        }

        await Task.Yield();
        telemetry.Emit(new TelemetryEvent(
            Guid.NewGuid(),
            clock.GetUtcNow(),
            TelemetrySeverity.Info,
            message));
    }

    internal static void Clear()
    {
        _telemetry = null;
        _clock = null;
    }
}

/// <summary>The lifecycle stage used by the fixture's availability selection.</summary>
public enum G02FixtureStage
{
    /// <summary>The item has not been published.</summary>
    Announced = 0,

    /// <summary>The item is available.</summary>
    Published = 1
}

/// <summary>A small but valid typed media item whose shape produces real naming tokens.</summary>
public sealed class G02FixtureItem : IMediaItem
{
    /// <inheritdoc />
    [Identity]
    public required MediaItemId Key { get; init; }

    /// <inheritdoc />
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    /// <inheritdoc />
    [Title]
    public required string Title { get; init; }

    /// <inheritdoc />
    public Language? TitleLanguage { get; init; }

    /// <inheritdoc />
    public string? Overview { get; init; }

    /// <inheritdoc />
    public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

    /// <inheritdoc />
    public CatalogRecordState CatalogState { get; init; }

    /// <summary>Gets the item's release stage.</summary>
    [Status]
    public G02FixtureStage Status { get; init; }
}

/// <summary>The fixture's format-owned representation value.</summary>
public sealed class G02FixtureRepresentation : IRepresentation;

/// <summary>Accepts fixture release names into the common release shape.</summary>
public sealed class G02FixtureParser : IReleaseParser<Release<G02FixtureRepresentation>>
{
    /// <inheritdoc />
    public static ReleaseParseResult<Release<G02FixtureRepresentation>> Parse(ReleaseParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ReleaseParseResult<Release<G02FixtureRepresentation>>.Accepted(
            new Release<G02FixtureRepresentation>(context.Text, null));
    }
}

/// <summary>A complete typed media declaration used only to exercise packaged admission.</summary>
public sealed partial class G02FixtureMediaType() :
    MediaType<
        G02FixtureItem,
        ReleaseTarget<G02FixtureItem>,
        Release<G02FixtureRepresentation>,
        G02FixtureParser>(
            MediaKindId.FromString("g02-fixture"),
            "Fixture item",
            "Fixture items",
            formats:
            [
                new FormatUse<G02FixtureRepresentation>(new FormatFamilyDefinition<G02FixtureRepresentation>
                {
                    Id = "g02-fixture",
                    Name = "G02 fixture",
                    FileExtensions = [".g02"]
                })
            ],
            availability: new OrderedSelectionDefinition<G02FixtureItem, G02FixtureStage>(
                item => item.Status,
                "Minimum availability",
                G02FixtureStage.Published))
{
    /// <inheritdoc />
    public override IReadOnlyList<SearchDefinition> Searches { get; } =
    [
        new("g02-fixture", "Fixture item", [SearchTerm.WorkTitle], [])
    ];

    /// <inheritdoc />
    public override MatchingDefinition<G02FixtureItem> Matching { get; } = new()
    {
        Layers = [new("title", item => new[] { item.Title })],
        ScopeReplacesSearch = true,
        Ambiguity = AmbiguityPolicy.Reject
    };

    /// <inheritdoc />
    public override QueryDefinition<G02FixtureItem> Querying { get; } = new()
    {
        Tiers =
        [
            new("title", "g02-fixture")
            {
                Arguments =
                [
                    new QueryPropertyArgument<G02FixtureItem, string>(
                        SearchTerm.WorkTitle,
                        item => item.Title)
                ],
                FreeText = item => item.Title
            }
        ]
    };
}

/// <summary>A notifier activated by Host from the fixture's recorded implementation type.</summary>
public sealed class G02FixtureNotifier : INotifier, IDisposable, IAsyncDisposable
{
    /// <summary>The provider-local identifier used by the module and implementation.</summary>
    public const string LocalId = "proof-notifier";
    private ITelemetryEmitter? _telemetry;
    private TimeProvider? _clock;

    /// <summary>Creates the notifier in the capability-scoped plugin context.</summary>
    public G02FixtureNotifier(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Id = ProviderId.Create(context.PluginId, LocalId);
        _telemetry = context.Telemetry;
        _clock = context.Clock;
    }

    /// <inheritdoc />
    public ProviderId Id { get; }

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Notifier;

    /// <inheritdoc />
    public IReadOnlyList<NotificationEvent> SupportedEvents { get; } = [NotificationEvent.ApplicationUpdated];

    /// <summary>Gets a value indicating whether Host used synchronous teardown.</summary>
    public bool IsSynchronouslyDisposed { get; private set; }

    /// <summary>Gets a value indicating whether Host awaited asynchronous teardown.</summary>
    public bool IsAsynchronouslyDisposed { get; private set; }

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ValidationOutcome.Success);

    /// <inheritdoc />
    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FacetValue>>([]);

    /// <inheritdoc />
    public Task NotifyAsync(
        ProviderInvocation invocation,
        NotificationMessage message,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        IsSynchronouslyDisposed = true;
        _telemetry = null;
        _clock = null;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var telemetry = Interlocked.Exchange(ref _telemetry, null);
        var clock = Interlocked.Exchange(ref _clock, null);

        await Task.Yield();
        IsAsynchronouslyDisposed = true;
        if (telemetry is not null && clock is not null)
        {
            telemetry.Emit(new TelemetryEvent(
                Guid.NewGuid(),
                clock.GetUtcNow(),
                TelemetrySeverity.Info,
                "G02 admission fixture notifier disposed asynchronously."));
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>A hostile provider whose constructor asks for the unrestricted root service locator.</summary>
public sealed class G02ForbiddenServiceProviderNotifier : INotifier
{
    /// <summary>The provider-local identifier used by the hostile activation scenario.</summary>
    public const string LocalId = "forbidden-service-provider";

    /// <summary>Attempts to resolve a root service if Host ever invokes this forbidden constructor.</summary>
    public G02ForbiddenServiceProviderNotifier(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService(typeof(ITelemetryEmitter)) is ITelemetryEmitter telemetry)
        {
            telemetry.Emit(new TelemetryEvent(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                TelemetrySeverity.Error,
                "G02 forbidden provider resolved a root service."));
        }

        throw new InvalidOperationException("G02 forbidden IServiceProvider constructor was invoked.");
    }

    /// <inheritdoc />
    public ProviderId Id { get; } = ProviderId.Create(
        PluginId.FromString("g02.admission.fixture"),
        LocalId);

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Notifier;

    /// <inheritdoc />
    public IReadOnlyList<NotificationEvent> SupportedEvents { get; } = [NotificationEvent.ApplicationUpdated];

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ValidationOutcome.Success);

    /// <inheritdoc />
    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FacetValue>>([]);

    /// <inheritdoc />
    public Task NotifyAsync(
        ProviderInvocation invocation,
        NotificationMessage message,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>A language implementation activated by Host from the fixture's recorded type.</summary>
public sealed class G02FixtureLanguage : ILanguageDefinition, IDisposable, IAsyncDisposable
{
    /// <summary>Gets a value indicating whether Host used synchronous teardown.</summary>
    public bool IsSynchronouslyDisposed { get; private set; }

    /// <summary>Gets a value indicating whether Host awaited asynchronous teardown.</summary>
    public bool IsAsynchronouslyDisposed { get; private set; }

    /// <inheritdoc />
    public Language Language { get; } = new("x-g02", "G02 fixture");

    /// <inheritdoc />
    public string PrepareComparison(string text) => Require(text);

    /// <inheritdoc />
    public string PrepareQuery(string text) => Require(text);

    /// <inheritdoc />
    public string PrepareFileName(string text) => Require(text);

    /// <inheritdoc />
    public string PrepareSort(string text) => Require(text);

    /// <inheritdoc />
    public void Dispose()
    {
        IsSynchronouslyDisposed = true;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await G02FixtureLifetimeSignals.EmitAsync(
            "G02 admission fixture language disposed asynchronously.");
        IsAsynchronouslyDisposed = true;
        GC.SuppressFinalize(this);
    }

    private static string Require(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text;
    }
}

/// <summary>A real scheduled job whose admitted media-kind association is observable in Host.</summary>
public sealed class G02FixtureJob : IScheduledJob, IAsyncDisposable
{
    private ITelemetryEmitter? _telemetry;
    private TimeProvider? _clock;
    private readonly bool _throwOnJobId;
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the job with the extension-scoped diagnostic surface and clock.</summary>
    public G02FixtureJob(ITelemetryEmitter telemetry, TimeProvider clock, bool throwOnJobId)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _throwOnJobId = throwOnJobId;
    }

    /// <summary>The fixture's stable job identifier.</summary>
    public const string Id = "g02.admission.fixture.proof";

    /// <inheritdoc />
    public string JobId => _throwOnJobId
        ? throw new InvalidOperationException("G02 fixture JobId getter failure.")
        : Id;

    /// <inheritdoc />
    public string Name => "G02 admission proof";

    /// <inheritdoc />
    public string Description => "Proves that a packaged extension job is associated with its admitted media kind.";

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public int MaxConcurrency => 1;

    /// <inheritdoc />
    public TimeSpan ShutdownDeadline => TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public async Task<JobExecutionResult> ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Parameters.ContainsKey("waitForCancellation"))
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var telemetry = _telemetry;
                var clock = _clock;
                if (telemetry is not null && clock is not null)
                {
                    telemetry.Emit(new TelemetryEvent(
                        Guid.NewGuid(),
                        clock.GetUtcNow(),
                        TelemetrySeverity.Info,
                        "G02 admission fixture scheduled job observed cancellation."));
                }

                throw;
            }
        }

        await _release.Task.ConfigureAwait(false);
        return new JobExecutionResult(true);
    }

    /// <summary>Releases the deliberately overrunning fixture execution.</summary>
    public void ReleaseExecution() => _release.TrySetResult();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var telemetry = Interlocked.Exchange(ref _telemetry, null);
        var clock = Interlocked.Exchange(ref _clock, null);

        if (telemetry is null || clock is null)
        {
            return;
        }

        await Task.Yield();
        telemetry.Emit(new TelemetryEvent(
            Guid.NewGuid(),
            clock.GetUtcNow(),
            TelemetrySeverity.Info,
            "G02 admission fixture scheduled job disposed asynchronously."));
    }
}

/// <summary>A contributor whose qualified check proves Host retained and invoked it.</summary>
public sealed class G02FixtureHealthContributor : IHealthContributor, IAsyncDisposable
{
    private ITelemetryEmitter? _telemetry;
    private TimeProvider? _clock;

    /// <summary>Creates the contributor with the extension-scoped diagnostic surface and clock.</summary>
    public G02FixtureHealthContributor(ITelemetryEmitter telemetry, TimeProvider clock)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public string ContributorId => "proof-health";

    /// <inheritdoc />
    public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<HealthCheck>>(
            [HealthCheck.Healthy("alive", "G02 admission fixture")]);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var telemetry = Interlocked.Exchange(ref _telemetry, null);
        var clock = Interlocked.Exchange(ref _clock, null);

        if (telemetry is null || clock is null)
        {
            return;
        }

        await Task.Yield();
        telemetry.Emit(new TelemetryEvent(
            Guid.NewGuid(),
            clock.GetUtcNow(),
            TelemetrySeverity.Info,
            "G02 admission fixture health contributor disposed asynchronously."));
    }
}

/// <summary>Registers one contribution of every kind G02 must withdraw atomically.</summary>
public sealed class G02AdmissionFixtureModule : IPluginModule, IHealthContributor, IAsyncDisposable
{
    private static readonly PluginId Plugin = PluginId.FromString("g02.admission.fixture");
    private const string ThrowingUnloadVersion = "0.1.1";
    private const string ThrowingJobEnvelopeVersion = "0.1.2";
    private const string ForbiddenServiceProviderVersion = "0.1.3";
    private ITelemetryEmitter? _telemetry;
    private TimeProvider? _clock;

    /// <inheritdoc />
    public PluginId Id => Plugin;

    /// <inheritdoc />
    public string ContributorId => "module-proof-health";

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _telemetry = context.Telemetry;
        _clock = context.Clock;
        G02FixtureLifetimeSignals.Initialize(context.Telemetry, context.Clock);

        if (StringComparer.Ordinal.Equals(context.PluginVersion, ThrowingUnloadVersion))
        {
            var loadContext = AssemblyLoadContext.GetLoadContext(typeof(G02AdmissionFixtureModule).Assembly)
                ?? throw new InvalidOperationException("The packaged fixture must be loaded into an assembly context.");
            loadContext.Unloading += ThrowDuringUnload;
        }

        var registry = context.Registry.AddMediaType<G02FixtureMediaType>();
        var notifier = new ProviderDescriptor
        {
            LocalId = StringComparer.Ordinal.Equals(context.PluginVersion, ForbiddenServiceProviderVersion)
                ? G02ForbiddenServiceProviderNotifier.LocalId
                : G02FixtureNotifier.LocalId,
            Family = ProviderFamily.Notifier,
            Name = "Proof notifier",
            Settings = []
        };

        if (StringComparer.Ordinal.Equals(context.PluginVersion, ForbiddenServiceProviderVersion))
        {
            registry.AddNotifier<G02ForbiddenServiceProviderNotifier>(notifier);
        }
        else
        {
            registry.AddNotifier<G02FixtureNotifier>(notifier);
        }

        registry
            .AddLanguage<G02FixtureLanguage>()
            .AddScheduledJob(
                new G02FixtureJob(
                    context.Telemetry,
                    context.Clock,
                    StringComparer.Ordinal.Equals(context.PluginVersion, ThrowingJobEnvelopeVersion)),
                "manual")
            .AddHealthContributor(new G02FixtureHealthContributor(context.Telemetry, context.Clock))
            .AddHealthContributor(this);
    }

    private static void ThrowDuringUnload(AssemblyLoadContext loadContext)
        => throw new InvalidOperationException("G02 fixture unloading failure.");

    /// <inheritdoc />
    public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<HealthCheck>>(
            [HealthCheck.Healthy("module-alive", "G02 admission fixture module")]);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var telemetry = Interlocked.Exchange(ref _telemetry, null);
        var clock = Interlocked.Exchange(ref _clock, null);

        if (telemetry is null || clock is null)
        {
            return;
        }

        await Task.Yield();
        telemetry.Emit(new TelemetryEvent(
            Guid.NewGuid(),
            clock.GetUtcNow(),
            TelemetrySeverity.Info,
            "G02 admission fixture module disposed asynchronously."));
        G02FixtureLifetimeSignals.Clear();
    }
}
