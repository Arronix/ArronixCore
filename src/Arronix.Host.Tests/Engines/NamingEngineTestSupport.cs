using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Naming;

// The shape contracts are experimental; these fixtures are written against them.
#pragma warning disable ARX0013

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The shape and items the naming-engine tests render from. Media-neutral, per the standing fixture
/// rule: a two-level kind — an entry with a title and a year, and works beneath it addressed by an
/// ordinal space with a sequence axis carrying a zero exception.
/// </summary>
internal static class NamingEngineTestSupport
{
    internal static readonly MediaKindId Kind = MediaKindId.FromString("fixture");
    internal static readonly MediaLevelId Entry = MediaLevelId.FromString("entry");
    internal static readonly MediaLevelId Work = MediaLevelId.FromString("work");

    internal const string SpaceId = "ordinal";

    internal static MediaShape Shape() => new()
    {
        Kind = Kind,
        Name = "Fixture",
        PluralName = "Fixtures",
        Levels =
        [
            new MediaLevel
            {
                Id = Entry,
                Name = "Entry",
                PluralName = "Entries",
                Identity = new LevelIdentity
                {
                    HasCatalogRecord = true,
                    HasLibraryRecord = true,
                    ExternalIds = [new ExternalIdScheme { Scheme = "ext", Name = "External" }],
                },
                Fields =
                [
                    new FieldDescriptor
                    {
                        FieldId = "title",
                        Name = "Title",
                        ValueKind = FieldValueKind.Text,
                        Semantics = FieldSemantics.Title,
                    },
                    new FieldDescriptor { FieldId = "year", Name = "Year", ValueKind = FieldValueKind.Integer },
                ],
            },
            new MediaLevel
            {
                Id = Work,
                Name = "Work",
                PluralName = "Works",
                Parent = Entry,
                Identity = new LevelIdentity { HasCatalogRecord = true, HasLibraryRecord = true },
                CoordinateSpaceIds = [SpaceId],
                SequenceAxes =
                [
                    new SequenceAxis
                    {
                        AxisId = "run",
                        Name = "Run",
                        PluralName = "Runs",
                        SpaceId = SpaceId,
                        ComponentIndex = 0,
                        Exceptions = [new SequenceException(0, "Extras", ExcludedFromCompleteness: true)],
                    },
                ],
                Fields =
                [
                    new FieldDescriptor
                    {
                        FieldId = "title",
                        Name = "Title",
                        ValueKind = FieldValueKind.Text,
                        Semantics = FieldSemantics.Title,
                    },
                ],
            },
        ],
        CoordinateSpaces =
        [
            new CoordinateSpace
            {
                SpaceId = SpaceId,
                Name = "Ordinal",
                Kind = CoordinateKind.Ordinal,
                IsCanonical = true,
                Components = [new CoordinateComponent("run", "Run", Required: true), new CoordinateComponent("index", "Index", Required: true)],
            },
        ],
        FileBinding = new FileBinding { AnchorLevelId = Entry, UnitLevelId = Work },
        FormatFamilies =
        [
            new FormatFamily
            {
                FamilyId = "digital",
                Name = "Digital",
                FileExtensions = [".mkv"],
                Ladder = [Tier],
                Unknown = new QualityTier("Unknown", 0),
            },
        ],
        Tokens = [],
    };

    internal static QualityTier Tier { get; } = new("HD-1080p", 5, Revision: new QualityRevision(2, 0, false));

    internal static ItemView EntryItem(string title = "The Fixture Show", int year = 2020) => new()
    {
        Ref = new MediaItemRef(Kind, Entry, MediaItemId.FromInt64(1)),
        Title = title,
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["title"] = FieldValue.OfText(title),
            ["year"] = FieldValue.OfInteger(year),
        },
        ExternalIds = [ExternalId.Of("ext", "42")],
    };

    internal static ItemView WorkItem(long run = 1, long index = 3, string title = "A Long Awaited Part") => new()
    {
        Ref = new MediaItemRef(Kind, Work, MediaItemId.FromInt64(2)),
        Parent = new MediaItemRef(Kind, Entry, MediaItemId.FromInt64(1)),
        Title = title,
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["title"] = FieldValue.OfText(title),
        },
        Coordinates = CoordinateSet.Of(
            new CoordinateReading(SpaceId, Coordinate.OfOrdinals(OrdinalPath.Of(run, index)), CoordinateConfidence.Verified)),
    };

    internal static MediaFileFacts File(string? group = "GROUP") => new()
    {
        Id = new MediaFileId(7),
        Path = "/library/incoming/original.file.name.mkv",
        SizeBytes = 1_000,
        Quality = Tier,
        ReleaseGroup = group,
        OriginalFileName = "original.file.name.mkv",
        Languages = [new Language("en", "English")],
    };

    internal static NamingTokenBindings Bind(MediaFileFacts? file = null, params ItemView[] chain) =>
        new ShapeTokenDeriver(Shape()).Bind(chain, file);
}
