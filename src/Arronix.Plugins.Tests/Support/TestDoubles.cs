using System.Collections.ObjectModel;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Telemetry;

#pragma warning disable ARX0001 // Caching contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0004 // Event contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0006 // Health contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0007 // Hosting contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0009 // Naming contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0010 // Serialization contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0011 // Telemetry contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0013 // Media-shape contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0015 // Provider contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0016 // Intent contracts are experimental; these doubles stand in for them.
#pragma warning disable ARX0017 // Wire contracts are experimental; these doubles stand in for them.

namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// The smallest thing that satisfies each contract the registry admits.
/// </summary>
/// <remarks>
/// Deliberately behaviorless. What is under test is admission and attribution, not what an extension's
/// implementation does with a call it was allowed to make.
/// </remarks>
internal sealed class FakeReleaseParser : IReleaseParser
{
    public MediaKindId MediaKind => MediaKindId.FromString("example");

    public ParsedRelease? Parse(string releaseTitle) => null;

    public bool CanParse(string releaseTitle) => false;
}

internal sealed class FakeDiacriticFolding : IDiacriticFoldingProvider
{
    public string? Language => null;

    public IReadOnlyDictionary<char, string> Replacements { get; } = ReadOnlyDictionary<char, string>.Empty;
}

internal sealed class FakeHealthContributor : IHealthContributor
{
    public string ContributorId => "test";

    public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<HealthCheck>>([]);
}

internal sealed class FakeIndexer : IIndexer
{
    public ProviderId Id { get; set; }

    public ProviderFamily Family => ProviderFamily.Indexer;

    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IndexerProfile> DescribeAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ReleaseQueryResult> SearchAsync(
        ProviderInvocation invocation,
        ReleaseQuery query,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ReleaseFetch> FetchAsync(
        ProviderInvocation invocation,
        Uri link,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// Builds the provider declarations the registry records.
/// </summary>
internal static class Declarations
{
    public static ProviderDescriptor Indexer(string localId) => new()
    {
        LocalId = localId,
        Family = ProviderFamily.Indexer,
        Name = localId,
        Settings = []
    };

    public static IndexerRegistration IndexerRegistration(string localId)
        => new(Indexer(localId), new FakeIndexer());
}

/// <summary>
/// A media shape with exactly one level and nothing optional, used where a shape has to exist but its
/// contents are not what is under test.
/// </summary>
internal static class MinimalShape
{
    public static MediaShape Create(string kind, params NamingToken[] tokens)
    {
        var level = new MediaLevel
        {
            Id = MediaLevelId.FromString("item"),
            Name = "Item",
            PluralName = "Items",
            Roles = MediaLevelRoles.LibraryEntry
                | MediaLevelRoles.AcquisitionUnit
                | MediaLevelRoles.CompletenessUnit
                | MediaLevelRoles.FileBearing,
            Identity = new LevelIdentity { HasCatalogRecord = true, HasLibraryRecord = true },
            Fields =
            [
                new FieldDescriptor
                {
                    FieldId = "title",
                    Name = "Title",
                    ValueKind = FieldValueKind.Text,
                    Semantics = FieldSemantics.Title
                }
            ]
        };

        return new MediaShape
        {
            Kind = MediaKindId.FromString(kind),
            Name = "Example",
            PluralName = "Examples",
            Levels = [level],
            FileBinding = new FileBinding
            {
                AnchorLevelId = level.Id,
                UnitLevelId = level.Id,
                AtMostOneFilePerUnit = true,
                AtMostOneUnitPerFile = true
            },
            FormatFamilies =
            [
                new FormatFamily
                {
                    FamilyId = "video",
                    Name = "Video",
                    FileExtensions = [".mkv"],
                    Ladder = [new QualityTier("HD", 1)],
                    Unknown = new QualityTier("Unknown", 0)
                }
            ],
            Tokens = [.. tokens]
        };
    }
}

internal sealed class FakeMediaShapeProvider : IMediaShapeProvider
{
    public FakeMediaShapeProvider(MediaShape shape) => Shape = shape;

    public MediaShape Shape { get; }
}

internal sealed class FakeMediaItemSource : IMediaItemSource
{
    public FakeMediaItemSource(string kind) => MediaKind = MediaKindId.FromString(kind);

    public MediaKindId MediaKind { get; }

    public Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<MediaItemRef?> ResolveExternalAsync(
        ExternalId externalId,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Abstractions.Intent.WorkbenchProposal> ProposeAsync(
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<Abstractions.Wire.ActionResult> CommitAsync(
        Abstractions.Intent.WorkbenchCommit commit,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// Collects what an extension emitted, so attribution can be asserted on.
/// </summary>
internal sealed class RecordingTelemetryEmitter : ITelemetryEmitter
{
    public List<TelemetryEvent> Events { get; } = [];

    public void Emit(TelemetryEvent telemetryEvent) => Events.Add(telemetryEvent);
}

internal sealed class RecordingEventPublisher : IEventPublisher
{
    public List<IDomainEvent> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        Published.Add(domainEvent);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records the partition names a decorator resolved to, which is the whole of what the decorator promises.
/// </summary>
internal sealed class RecordingCacheProvider : ICacheProvider
{
    public List<string> Partitions { get; } = [];

    public ICache<TValue> GetCache<TOwner, TValue>(string partition)
    {
        Partitions.Add(partition);
        return new RecordingCache<TValue>(partition);
    }

    public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
    {
        Partitions.Add(partition);
        return new RecordingCache<TValue>(partition);
    }

    public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null)
    {
        Partitions.Add(partition);
        return new RecordingSelfRefreshingCache<TValue>(partition);
    }
}

internal sealed class RecordingCache<TValue> : ICache<TValue>
{
    private readonly Dictionary<string, TValue> _values = [];

    public RecordingCache(string name) => Name = name;

    public string Name { get; }

    public int Count => _values.Count;

    public IReadOnlyCollection<TValue> Values => _values.Values;

    public void Clear() => _values.Clear();

    public void ClearExpired()
    {
    }

    public void Remove(string key) => _values.Remove(key);

    public void Set(string key, TValue value, TimeSpan? lifetime = null) => _values[key] = value;

    public TValue Get(string key, Func<TValue> valueFactory, TimeSpan? lifetime = null)
        => _values.TryGetValue(key, out var value) ? value : _values[key] = valueFactory();

    public Task<TValue> GetAsync(
        string key,
        Func<CancellationToken, Task<TValue>> valueFactory,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_values.TryGetValue(key, out var value) ? value : _values[key] = valueFactory(cancellationToken).GetAwaiter().GetResult());

    public TValue? Find(string key) => _values.TryGetValue(key, out var value) ? value : default;
}

internal sealed class RecordingSelfRefreshingCache<TValue> : ISelfRefreshingCache<TValue>
{
    public RecordingSelfRefreshingCache(string name) => Name = name;

    public string Name { get; }

    public int Count => 0;

    public void Clear()
    {
    }

    public void ClearExpired()
    {
    }

    public void Remove(string key)
    {
    }

    public Task<TValue> GetAsync(string key, CancellationToken cancellationToken = default)
        => throw new KeyNotFoundException(key);

    public TValue? Find(string key) => default;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshIfExpiredAsync(TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Update(IReadOnlyDictionary<string, TValue> items)
    {
    }

    public void Touch()
    {
    }

    public bool IsExpired(TimeSpan timeToLive) => false;
}

internal sealed class StubHostRuntimeInfo : IHostRuntimeInfo
{
    public DateTimeOffset StartTime => DateTimeOffset.UnixEpoch;

    public bool IsUserInteractive => false;

    public bool IsAdministrator => false;

    public bool IsWindowsService => false;

    public bool IsContainerized => false;

    public string? ExecutingApplication => null;
}

internal sealed class StubOperatingSystemInfo : IOperatingSystemInfo
{
    public string Name => "test";

    public string Version => "0.0";

    public string FullName => "test 0.0";

    public bool IsDocker => false;

    public bool IsPodman => false;

    public bool IsContainerized => false;
}

internal sealed class StubJsonSerializer : IJsonSerializer
{
    public string Serialize<TValue>(TValue value) => string.Empty;

    public TValue? Deserialize<TValue>(string json) => default;

    public bool TryDeserialize<TValue>(string json, out TValue? value)
    {
        value = default;
        return false;
    }

    public Task SerializeAsync<TValue>(TValue value, System.IO.Stream destination, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public ValueTask<TValue?> DeserializeAsync<TValue>(System.IO.Stream source, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<TValue?>(default);
}
