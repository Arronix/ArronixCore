using System.Collections;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
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
    public void UnsealedContractValuesAreRebuiltAsTheirBaseTypes()
    {
        var listing = new ReleaseListing(
            new ReleaseId("r1"),
            "Title",
            new HostileUri("https://example.test/download"),
            "indexer",
            new MediaKindId("movies"),
            Size: 1,
            PublishDate: DateTime.UnixEpoch,
            InfoUrl: new HostileUri("https://example.test/info"));

        var item = new ItemView
        {
            Ref = new MediaItemRef(new MediaKindId("movies"), MediaLevelId.FromString("movie"), new MediaItemId(1)),
            Title = "A film",
            TitleLanguage = new HostileLanguage("en", "English"),
            Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                ["poster"] = new FieldValue
                {
                    Kind = FieldValueKind.Artwork,
                    Link = new HostileUri("https://example.test/poster.jpg"),
                    Language = new HostileLanguage("fr", "French"),
                },
            },
        };

        var copiedListing = PluginBoundary.Snapshot(listing);
        var copiedItem = PluginBoundary.Snapshot(item);

        using (new AssertionScope())
        {
            copiedListing.DownloadUrl.Should().NotBeOfType<HostileUri>();
            copiedListing.DownloadUrl.OriginalString.Should().Be("https://example.test/download");
            copiedListing.InfoUrl.Should().NotBeOfType<HostileUri>();
            copiedItem.TitleLanguage.Should().NotBeOfType<HostileLanguage>();
            copiedItem.TitleLanguage!.Code.Should().Be("en");
            copiedItem.Fields["poster"].Link.Should().NotBeOfType<HostileUri>();
            copiedItem.Fields["poster"].Language.Should().NotBeOfType<HostileLanguage>();
            Reachable(copiedItem).Should().NotContain(
                type => type.Assembly == typeof(PluginBoundarySnapshotTests).Assembly);
        }
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
    public void ADeclaredShapeAndIntentSurfaceKeepNothingOfTheExtension()
    {
        var shape = new MediaShape
        {
            Kind = new MediaKindId("hostile"),
            Name = "Hostile",
            PluralName = "Hostiles",
            Levels = new RecordingSequence<MediaLevel>(
            [
                new MediaLevel
                {
                    Id = MediaLevelId.FromString("root"),
                    Name = "Root",
                    PluralName = "Roots",
                    Identity = new LevelIdentity
                    {
                        HasCatalogRecord = true,
                        HasLibraryRecord = true,
                        RequiredRoles = new RecordingSequence<IdentifierRole>([IdentifierRole.PrimaryWork]),
                        AdmittedRoles = new RecordingSequence<IdentifierRole>([IdentifierRole.PrimaryWork]),
                        ExternalIds = new RecordingSequence<ExternalIdScheme>([]),
                    },
                    Fields = new RecordingSequence<FieldDescriptor>(
                    [
                        new FieldDescriptor
                        {
                            FieldId = "title",
                            Name = "Title",
                            ValueKind = FieldValueKind.Text,
                            Choices = new RecordingSequence<FacetValue>([new FacetValue("a", "A")]),
                            Components = new RecordingSequence<FieldDescriptor>(
                            [
                                new FieldDescriptor
                                {
                                    FieldId = "part",
                                    Name = "Part",
                                    ValueKind = FieldValueKind.Text,
                                    Choices = new RecordingSequence<FacetValue>([]),
                                    Components = new RecordingSequence<FieldDescriptor>([]),
                                },
                            ]),
                        },
                    ]),
                    CoordinateSpaceIds = new RecordingSequence<string>([]),
                    SequenceAxes = new RecordingSequence<SequenceAxis>([]),
                    MonitorDimensions = new RecordingSequence<MonitorDimension>([]),
                    FormatFamilyIds = new RecordingSequence<string>([]),
                },
            ]),
            FileBinding = new FileBinding
            {
                AnchorLevelId = MediaLevelId.FromString("root"),
                UnitLevelId = MediaLevelId.FromString("root"),
                SpanConstraints = new RecordingSequence<SpanConstraint>([]),
            },
            FormatFamilies = new RecordingSequence<FormatFamily>(
            [
                new FormatFamily
                {
                    FamilyId = "video",
                    Name = "Video",
                    FileExtensions = new RecordingSequence<string>([".mkv"]),
                    Ladder = new RecordingSequence<QualityTier>([new HostileTier("HD", 1)]),
                    Unknown = new HostileTier("Unknown", 0),
                },
            ]),
            Tokens = new RecordingSequence<NamingToken>([new HostileToken("title", "Title", "A film")]),
            CoordinateSpaces = new RecordingSequence<CoordinateSpace>([]),
            GroupingAxes = new RecordingSequence<GroupingAxis>([]),
            SelectionFacets = new RecordingSequence<SelectionFacet>([]),
            SearchKinds = new RecordingSequence<SearchKind>([]),
        };

        var surface = new PluginIntentSurface
        {
            MediaKind = new MediaKindId("hostile"),
            BrowseAxes = new RecordingSequence<BrowseAxis>([]),
            Sorts = new RecordingSequence<SortOption>([]),
            Filters = new RecordingSequence<FilterOption>([]),
            Actions = new RecordingSequence<ActionDescriptor>([]),
            States = new RecordingSequence<StateDescriptor>([]),
            ExternalSurfaces = new RecordingSequence<ExternalSurfaceDescriptor>([]),
            Workbenches = new RecordingSequence<WorkbenchDescriptor>(
            [
                new WorkbenchDescriptor
                {
                    WorkbenchId = "assign",
                    Name = "Assign",
                    Subject = WorkbenchSubject.LooseFiles,
                    CommitLabel = "Apply",
                    CommitConsequence = Consequence.Safe,
                    CommitConfirmation = ConfirmationRequirement.None,
                    Columns = new RecordingSequence<WorkbenchColumn>(
                    [
                        new WorkbenchColumn
                        {
                            Field = new FieldDescriptor
                            {
                                FieldId = "target",
                                Name = "Target",
                                ValueKind = FieldValueKind.Text,
                                Choices = new RecordingSequence<FacetValue>([]),
                                Components = new RecordingSequence<FieldDescriptor>([]),
                            },
                        },
                    ]),
                    Inputs = new RecordingSequence<ActionParameter>([]),
                },
            ]),
        };

        using (new AssertionScope())
        {
            Reachable(DeclarationBoundary.Snapshot(shape)).Should().NotContain(
                type => type.Assembly == typeof(PluginBoundarySnapshotTests).Assembly,
                "a shape is retained and re-read for the life of the kind");
            Reachable(DeclarationBoundary.Snapshot(surface)).Should().NotContain(
                type => type.Assembly == typeof(PluginBoundarySnapshotTests).Assembly,
                "and so is the surface projected from it");
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

        // Anything not from the contract assembly is recorded: an unsealed contract value that came back
        // as a subclass is exactly as much of an escape as a custom collection.
        if (value.GetType().Namespace?.StartsWith("Arronix.Abstractions", StringComparison.Ordinal) != true)
        {
            found.Add(value.GetType());
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

    /// <summary>A language defined outside the contract assembly. Language is not sealed.</summary>
    private sealed record HostileLanguage(string Code, string Name) : Language(Code, Name);

    /// <summary>A quality tier defined outside the contract assembly. QualityTier is not sealed.</summary>
    private sealed record HostileTier(string Name, int Rank) : QualityTier(Name, Rank);

    /// <summary>A naming token defined outside the contract assembly. NamingToken is not sealed.</summary>
    private sealed record HostileToken(string Name, string Description, string ExampleValue)
        : NamingToken(Name, Description, ExampleValue);

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
