using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

// The shape contracts are experimental; these fixtures are written against them.
#pragma warning disable ARX0013

namespace Arronix.Host.Tests.Support;

/// <summary>
/// Shapes the media tests are written against.
/// </summary>
/// <remarks>
/// Deliberately media-neutral. A fixture named after a real media kind would tempt a test into asserting
/// something about that kind rather than about the rule under test, and the rules here are the ones that
/// have to hold for every kind. The names are structural: a catalog entry, the work inside it, the parts
/// of the work, and a competing manifestation of the work.
/// </remarks>
internal static class ShapeFixtures
{
    internal static readonly MediaKindId Kind = MediaKindId.FromString("fixture");
    internal static readonly MediaLevelId Catalog = MediaLevelId.FromString("catalog");
    internal static readonly MediaLevelId Work = MediaLevelId.FromString("work");
    internal static readonly MediaLevelId Variant = MediaLevelId.FromString("variant");
    internal static readonly MediaLevelId Part = MediaLevelId.FromString("part");

    internal const string OrdinalSpaceId = "ordinal";
    internal const string GroupComponentId = "group";
    internal const string IndexComponentId = "index";
    internal const string SequenceAxisId = "run";

    /// <summary>
    /// The simplest shape that validates: one level carrying every role at once.
    /// </summary>
    /// <returns>The shape.</returns>
    internal static MediaShape Fused() => new()
    {
        Kind = Kind,
        Name = "Fixture",
        PluralName = "Fixtures",
        Levels =
        [
            new MediaLevel
            {
                Id = Catalog,
                Name = "Entry",
                PluralName = "Entries",
                Roles = MediaLevelRoles.LibraryEntry
                    | MediaLevelRoles.AcquisitionUnit
                    | MediaLevelRoles.CompletenessUnit
                    | MediaLevelRoles.FileBearing,
                Identity = new LevelIdentity
                {
                    HasCatalogRecord = true,
                    HasLibraryRecord = true,
                    ExternalIds = [new ExternalIdScheme { Scheme = "fixture", Name = "Fixture", IsPrimary = true }],
                },
                Fields = [Title("title")],
                MonitorDimensions = [Toggle("wanted")],
            },
        ],
        FileBinding = new FileBinding
        {
            AnchorLevelId = Catalog,
            UnitLevelId = Catalog,
            AtMostOneFilePerUnit = true,
            AtMostOneUnitPerFile = true,
        },
        FormatFamilies = [Family("primary", ["mkv", "mp4"])],
        SearchKinds =
        [
            new SearchKind
            {
                SearchKindId = "unit",
                Name = "Unit",
                TargetLevelId = Catalog,
                Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
                RequiredTerms = [SearchTerm.FreeText],
                Categories = [CategoryId.FromInt(2000)],
            },
        ],
        Tokens = [new NamingToken("{Title}", "The title", "Something")],
    };

    /// <summary>
    /// The hard shape: four levels, a variant level whose parent is the acquisition unit, a file anchored two
    /// levels above the unit it satisfies, an ordinal coordinate space with a sequence axis and an excluded
    /// reserved value, and a constraint forbidding one file from spanning the sequence.
    /// </summary>
    /// <returns>The shape.</returns>
    internal static MediaShape Layered() => new()
    {
        Kind = Kind,
        Name = "Fixture",
        PluralName = "Fixtures",
        Levels =
        [
            new MediaLevel
            {
                Id = Catalog,
                Name = "Entry",
                PluralName = "Entries",
                Roles = MediaLevelRoles.LibraryEntry,
                Identity = new LevelIdentity { HasCatalogRecord = true, HasLibraryRecord = true },
                Fields = [Title("entry-title")],
                MonitorDimensions = [Toggle("future")],
            },
            new MediaLevel
            {
                Id = Work,
                Name = "Work",
                PluralName = "Works",
                Parent = Catalog,
                Roles = MediaLevelRoles.AcquisitionUnit | MediaLevelRoles.FileBearing,
                Identity = new LevelIdentity { HasCatalogRecord = true, HasLibraryRecord = true },
                Fields = [Title("work-title")],
                MonitorDimensions = [Toggle("wanted")],
            },
            new MediaLevel
            {
                Id = Variant,
                Name = "Manifestation",
                PluralName = "Manifestations",
                Parent = Work,
                Roles = MediaLevelRoles.VariantAxis,
                Identity = new LevelIdentity { HasCatalogRecord = true, HasLibraryRecord = false },
                Fields = [Title("variant-title")],
                Variant = new VariantSelection
                {
                    AutoSwitchByDefault = true,
                    Triggers = SelectionTrigger.Manual | SelectionTrigger.OnImport,
                    CompletenessIsVariantRelative = true,
                },
            },
            new MediaLevel
            {
                Id = Part,
                Name = "Part",
                PluralName = "Parts",
                Parent = Variant,
                Roles = MediaLevelRoles.CompletenessUnit | MediaLevelRoles.FileBearing,
                Identity = new LevelIdentity { HasCatalogRecord = true, HasLibraryRecord = false },
                Fields = [Title("part-title")],
                CoordinateSpaceIds = [OrdinalSpaceId],
                SequenceAxes =
                [
                    new SequenceAxis
                    {
                        AxisId = SequenceAxisId,
                        Name = "Run",
                        PluralName = "Runs",
                        SpaceId = OrdinalSpaceId,
                        ComponentIndex = 0,
                        HasPolicyRecord = true,
                        Exceptions = [new SequenceException(0, "Reserved", ExcludedFromCompleteness: true)],
                    },
                ],
            },
        ],
        CoordinateSpaces = [OrdinalSpace()],
        FileBinding = new FileBinding
        {
            // The anchor sits two levels above the unit, so file ownership survives a change of chosen
            // manifestation.
            AnchorLevelId = Work,
            UnitLevelId = Part,
            AtMostOneFilePerUnit = true,
            AtMostOneUnitPerFile = false,
            SpanConstraints = [new SpanConstraint(OrdinalSpaceId, GroupComponentId, SpanRule.MustNotSpan)],
        },
        GroupingAxes =
        [
            new GroupingAxis
            {
                AxisId = "collection",
                Name = "Collection",
                PluralName = "Collections",
                MemberLevelId = Catalog,
                Arity = GroupingArity.ManyToMany,
                Position = MemberPosition.Labeled,
                HasPrimaryMember = true,
                Lifetime = GroupLifetime.RefCounted,
            },
        ],
        FormatFamilies = [Family("primary", ["mkv"]), Family("secondary", ["mp3"])],
        SearchKinds =
        [
            new SearchKind
            {
                SearchKindId = "unit",
                Name = "Unit",
                TargetLevelId = Work,
                Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
                RequiredTerms = [SearchTerm.FreeText, SearchTerm.WorkTitle],
                Categories = [CategoryId.FromInt(5000)],
            },
            new SearchKind
            {
                SearchKindId = "span",
                Name = "Span",
                TargetLevelId = Part,
                Scope = new AcquisitionScope
                {
                    Kind = AcquisitionScopeKind.SequenceSpan,
                    SequenceAxisId = SequenceAxisId,
                },
                RequiredTerms = [SearchTerm.FreeText],
                Categories = [CategoryId.FromInt(5000)],
            },
            new SearchKind
            {
                SearchKindId = "ancestor",
                Name = "Ancestor",
                TargetLevelId = Part,
                Scope = new AcquisitionScope
                {
                    Kind = AcquisitionScopeKind.Ancestor,
                    AncestorLevelId = Catalog,
                },
                Categories = [CategoryId.FromInt(5000)],
            },
        ],
        Tokens = [new NamingToken("{Title}", "The title", "Something")],
    };

    internal static CoordinateSpace OrdinalSpace() => new()
    {
        SpaceId = OrdinalSpaceId,
        Name = "Ordinal",
        Kind = CoordinateKind.Ordinal,
        IsCanonical = true,
        IsDense = true,
        Components =
        [
            new CoordinateComponent(GroupComponentId, "Group", Required: true),
            new CoordinateComponent(IndexComponentId, "Index", Required: true),
        ],
    };

    internal static FieldDescriptor Title(string fieldId) => new()
    {
        FieldId = fieldId,
        Name = "Title",
        ValueKind = FieldValueKind.Text,
        Semantics = FieldSemantics.Title | FieldSemantics.Sortable,
        Prominence = Prominence.Primary,
    };

    internal static MonitorDimension Toggle(string dimensionId) => new()
    {
        DimensionId = dimensionId,
        Name = dimensionId,
        Kind = MonitorDimensionKind.Toggle,
    };

    internal static FormatFamily Family(string familyId, IReadOnlyList<string> extensions) => new()
    {
        FamilyId = familyId,
        Name = familyId,
        FileExtensions = extensions,
        Ladder = [new QualityTier("Low", 1), new QualityTier("High", 2)],
        Unknown = new QualityTier("Unknown", 0),
    };

    internal static MediaItemRef Item(MediaLevelId level, int id)
        => new(Kind, level, MediaItemId.FromInt64(id));

    internal static CoordinateSet At(long group, long index)
        => CoordinateSet.Of(new CoordinateReading(
            OrdinalSpaceId,
            Coordinate.OfOrdinals(OrdinalPath.Of(group, index)),
            CoordinateConfidence.Verified));

    /// <summary>
    /// Applies an edit to one level of a shape, which is how the defect corpus produces one malformed shape
    /// per rule without restating the whole declaration each time.
    /// </summary>
    /// <param name="shape">The sound shape.</param>
    /// <param name="level">Which level to replace.</param>
    /// <param name="edit">The replacement.</param>
    /// <returns>The edited shape.</returns>
    internal static MediaShape WithLevel(this MediaShape shape, MediaLevelId level, Func<MediaLevel, MediaLevel> edit)
        => shape with
        {
            Levels = [.. shape.Levels.Select(candidate => candidate.Id == level ? edit(candidate) : candidate)],
        };
}
