using System.Collections;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Providers;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Host.Tests.Providers;

/// <summary>
/// What a plugin returns is copied into host-owned values before its ticket is released.
/// </summary>
/// <remarks>
/// A contract promises <c>IReadOnlyList&lt;T&gt;</c>; what arrives can be a lazy sequence that calls back
/// into the extension, or a type from its collectible context. Either one, enumerated or held after the
/// ticket ends, is what the ticket exists to prevent.
/// </remarks>
[TestFixture]
public class PluginBoundarySnapshotTests
{
    [Test]
    public void EveryCollectionInAQueryResultIsEnumeratedOnceAndReplaced()
    {
        var releases = new RecordingSequence<ReleaseListing>(
        [
            new ReleaseListing(
                new ReleaseId("r1"),
                "Title",
                new Uri("https://example.test/a"),
                "indexer",
                new MediaKindId("movies"),
                Size: 1,
                PublishDate: DateTime.UnixEpoch,
                AdditionalData: new RecordingMap<string, string> { ["seeders"] = "10" }),
        ]);

        var warnings = new RecordingSequence<string>(["slow"]);
        var result = new ReleaseQueryResult(releases, IsPartialResult: false, warnings);

        var snapshot = PluginBoundary.Snapshot(result);

        using (new AssertionScope())
        {
            releases.Enumerations.Should().Be(1, "the plugin's own sequence is read exactly once, in the lease");
            warnings.Enumerations.Should().Be(1);
            snapshot.Releases.Should().NotBeOfType<RecordingSequence<ReleaseListing>>();
            snapshot.Warnings.Should().NotBeOfType<RecordingSequence<string>>();
            snapshot.Releases[0].AdditionalData.Should().NotBeOfType<RecordingMap<string, string>>();
            snapshot.Releases[0].AdditionalData!["seeders"].Should().Be("10");
        }
    }

    [Test]
    public void NothingFromTheExtensionSurvivesInAQueryResultSnapshot()
    {
        var result = new ReleaseQueryResult(
            new RecordingSequence<ReleaseListing>(
            [
                new ReleaseListing(
                    new ReleaseId("r1"),
                    "Title",
                    new Uri("https://example.test/a"),
                    "indexer",
                    new MediaKindId("movies"),
                    Size: 1,
                    PublishDate: DateTime.UnixEpoch,
                    AdditionalData: new RecordingMap<string, string> { ["k"] = "v" }),
            ]),
            IsPartialResult: false,
            new RecordingSequence<string>(["warning"]));

        var snapshot = PluginBoundary.Snapshot(result);

        Reachable(snapshot).Should().NotContain(
            candidate => candidate.Assembly == typeof(PluginBoundarySnapshotTests).Assembly,
            "every collection reachable from the result must be a framework or contract type");
    }

    [Test]
    public void AnItemsFieldsCoordinatesAndIdentifiersAreAllReplaced()
    {
        var item = new ItemView
        {
            Ref = new MediaItemRef(new MediaKindId("movies"), MediaLevelId.FromString("movie"), new MediaItemId(1)),
            Title = "A film",
            Fields = new RecordingMap<string, FieldValue>
            {
                ["genres"] = new FieldValue
                {
                    Kind = FieldValueKind.Text,
                    Items = new RecordingSequence<FieldValue>([FieldValue.OfText("drama")]),
                },
            },
            Coordinates = new CoordinateSet { Readings = new RecordingSequence<CoordinateReading>([]) },
            ExternalIds = new RecordingSequence<ExternalId>([new ExternalId("tmdb", "1")]),
        };

        var snapshot = PluginBoundary.Snapshot(item);

        Reachable(snapshot).Should().NotContain(
            candidate => candidate.Assembly == typeof(PluginBoundarySnapshotTests).Assembly);
    }

    [Test]
    public void FetchedBytesAreCopiedOutOfTheExtensionsOwnStorage()
    {
        var storage = new byte[] { 1, 2, 3 };
        var fetch = new ReleaseFetch(storage, "a.nzb", "application/x-nzb");

        var snapshot = PluginBoundary.Snapshot(fetch);
        storage[0] = 9;

        using (new AssertionScope())
        {
            snapshot.Content.ToArray().Should().Equal([1, 2, 3], "the host holds its own copy");
            snapshot.Content.Span.Overlaps(storage).Should().BeFalse();
        }
    }

    [Test]
    public void AProviderDeclarationIsCopiedAtAdmissionAndKeepsNothingOfTheExtension()
    {
        var settings = new RecordingSequence<SettingsField>(
        [
            new SettingsField
            {
                FieldId = "url",
                Name = "URL",
                ValueKind = FieldValueKind.Text,
                Role = SettingRole.Endpoint,
                Choices = new RecordingSequence<FacetValue>([new FacetValue("a", "A")]),
                HelpLink = new HostileUri("https://example.test/help"),
            },
        ]);

        var descriptor = new ProviderDescriptor
        {
            LocalId = "hostile",
            Name = "Hostile",
            Settings = settings,
            Protocols = new RecordingSequence<DownloadProtocol>([DownloadProtocol.Usenet]),
            Presets = new RecordingSequence<ProviderPreset>(
                [new ProviderPreset("p", "Preset", new RecordingMap<string, string> { ["k"] = "v" })]),
            InfoLink = new HostileUri("https://example.test/info"),
        };

        var registry = new ProviderRegistry();

        registry.TryPrepare(
            PluginId.FromString("hostile.fixture"),
            ProviderFamily.Indexer,
            descriptor,
            new InertProvider(),
            mediaItemType: null,
            out var candidate,
            out _).Should().BeTrue();

        using (new AssertionScope())
        {
            settings.Enumerations.Should().Be(1, "the declaration is read once, at admission");
            Reachable(candidate.Descriptor).Should().NotContain(
                type => type.Assembly == typeof(PluginBoundarySnapshotTests).Assembly,
                "nothing an extension defined may survive in a declaration the host keeps and publishes");
            candidate.Descriptor.InfoLink.Should().NotBeOfType<HostileUri>();
            candidate.Descriptor.Settings[0].HelpLink.Should().NotBeOfType<HostileUri>();
            candidate.Descriptor.Settings[0].HelpLink!.OriginalString.Should().Be("https://example.test/help");
        }
    }

    [Test]
    public void AHealthCheckContractCannotBeDerivedFromAtAll()
    {
        // The structural half of the guarantee the reconstruction below makes. An extension that could
        // derive a check would hand the host a live plugin type that outlives its contributor's ticket.
        typeof(Arronix.Abstractions.Health.HealthCheck).IsSealed.Should().BeTrue();
    }

    /// <summary>Every collection type reachable from a value, one level of records deep.</summary>
    private static IReadOnlyList<Type> Reachable(object root)
    {
        var found = new List<Type>();
        Walk(root, found, depth: 0);
        return found;
    }

    private static void Walk(object? value, List<Type> found, int depth)
    {
        if (value is null || depth > 6 || value is string)
        {
            return;
        }

        if (value is IEnumerable sequence)
        {
            found.Add(value.GetType());

            foreach (var element in sequence)
            {
                Walk(element is DictionaryEntry entry ? entry.Value : element, found, depth + 1);
            }

            return;
        }

        if (value.GetType().Namespace?.StartsWith("Arronix.Abstractions", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var property in value.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
        {
            Walk(property.GetValue(value), found, depth + 1);
        }
    }

    /// <summary>A sequence defined outside the contract assembly, which records being enumerated.</summary>
    private sealed class RecordingSequence<TValue>(IReadOnlyList<TValue> values) : IReadOnlyList<TValue>
    {
        internal int Enumerations { get; private set; }

        public int Count => values.Count;

        public TValue this[int index] => values[index];

        public IEnumerator<TValue> GetEnumerator()
        {
            Enumerations++;
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A map defined outside the contract assembly.</summary>
    private sealed class RecordingMap<TKey, TValue> : Dictionary<TKey, TValue>
        where TKey : notnull;

    /// <summary>An address type defined outside the contract assembly. Uri is not sealed.</summary>
    private sealed class HostileUri(string value) : Uri(value);

    private sealed class InertProvider : IProvider
    {
        public Task<ValidationOutcome> TestAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ValidationOutcome.Success);

        public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
            ProviderInvocation invocation,
            string optionSourceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FacetValue>>([]);
    }
}
