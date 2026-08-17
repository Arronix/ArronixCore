using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed.Builders;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Turns an item type and its configuration into the structure descriptor every engine already reads.
/// </summary>
/// <remarks>
/// <para>
/// This is the runtime model, in the sense a typed data mapper means it: the entity is what an author
/// writes, and this is what the platform holds. Nothing here is a second source of truth — every value is
/// either read off the type or carried from a builder call, so no part of it can disagree with the entity
/// it came from.
/// </para>
/// <para>
/// The zero-authoring cases are worth reading as a group. One level, its roles, the singleton coordinate
/// space, the file binding's four flags and the monitoring dimension are all produced without a kind saying
/// anything, because a kind whose items are its own acquisition units has nothing to say about any of them.
/// </para>
/// </remarks>
internal static class ShapeDerivation
{
    /// <summary>The coordinate space a kind whose items address themselves has.</summary>
    internal const string SingletonSpaceId = "singleton";

    /// <summary>The monitoring dimension every surveyed kind turned out to want, and only that one.</summary>
    internal static MonitorDimension WantedDimension { get; } = new()
    {
        DimensionId = "wanted",
        Name = "Wanted",
        Kind = MonitorDimensionKind.Toggle,
        DefaultChoice = "true"
    };

    /// <summary>
    /// Derives the structure of one media kind.
    /// </summary>
    /// <param name="kind">The media kind identifier.</param>
    /// <param name="item">The item type's reading.</param>
    /// <param name="declaration">Everything the configuration call recorded.</param>
    /// <returns>The structure.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static MediaShape Derive(MediaKindId kind, ItemTypeReader item, TypedDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(declaration);

        var singular = declaration.Singular ?? DerivedNames.Label(item.EntityType.Name);
        var plural = declaration.Plural ?? DerivedNames.Plural(singular);
        var levelId = MediaLevelId.FromString(DerivedNames.Identifier(item.EntityType.Name));

        var families = declaration.Formats.Select(DeriveFamily).ToArray();
        var axes = declaration.Groups.Select(draft => GroupAxisFactory.Derive(draft, levelId)).ToArray();
        var facets = families.SelectMany(static family => family.TechnicalFacets).ToArray();

        var level = new MediaLevel
        {
            Id = levelId,
            Name = singular,
            PluralName = plural,
            Parent = null,

            // Every role at once, because with one level and one file per item every role is this level's.
            // Restating that in a declaration was the archetype of a row that says nothing.
            Roles = MediaLevelRoles.LibraryEntry
                | MediaLevelRoles.AcquisitionUnit
                | MediaLevelRoles.CompletenessUnit
                | MediaLevelRoles.FileBearing,

            Identity = new LevelIdentity
            {
                HasCatalogRecord = true,
                HasLibraryRecord = true,
                SupportsIdentifierRedirects = declaration.Identity.SupportsRedirects,
                RequiredRoles = [.. declaration.Identity.Required],
                AdmittedRoles = [.. declaration.Identity.Admitted],

                // Composed by the host from the installed catalogers, and empty until one is installed.
                // A kind with a required role and no cataloger has no identifier search and no identity
                // stamp in its folder names, which is what "no cataloger" honestly means — but it has to
                // surface as a health warning rather than as a silent degradation.
                ExternalIds = []
            },

            CoordinateSpaceIds = [SingletonSpaceId],
            SequenceAxes = [],
            Fields = [.. item.Fields.Select(static candidate => candidate.Descriptor)],
            MonitorDimensions = [WantedDimension],
            FormatFamilyIds = [.. families.Select(static family => family.FamilyId)],
            Variant = null
        };

        return new MediaShape
        {
            Kind = kind,
            Name = singular,
            PluralName = plural,
            Levels = [level],
            FileBinding = DeriveBinding(levelId, declaration),
            CoordinateSpaces =
            [
                new CoordinateSpace
                {
                    SpaceId = SingletonSpaceId,
                    Name = singular,
                    Kind = CoordinateKind.Singleton,
                    IsCanonical = true,
                    IsDense = true
                }
            ],
            GroupingAxes = axes,
            FormatFamilies = families,
            SelectionFacets = [.. declaration.Selections.Select(draft => DeriveFacet(draft, levelId))],
            SearchKinds = [.. declaration.Searches.Select(draft => DeriveSearch(draft, levelId))],
            Tokens = TokenDerivation.Derive(
                DerivedNames.TokenWord(item.EntityType.Name),
                item,
                [.. declaration.Groups.Select(static draft =>
                    (DerivedNames.TokenWord(draft.GroupType.Name), ItemTypeReader.Read(draft.GroupType)))],
                facets,
                declaration.Identity.Required.Count > 0 || declaration.Identity.Admitted.Count > 0)
        };
    }

    private static FileBinding DeriveBinding(MediaLevelId levelId, TypedDeclaration declaration) =>
        new()
        {
            AnchorLevelId = levelId,
            UnitLevelId = levelId,
            AtMostOneFilePerUnit = declaration.FilesBindOnePerItem,
            AtMostOneUnitPerFile = declaration.FilesBindOnePerItem,
            OrdinalIsMeaningful = false,
            SpanConstraints = []
        };

    private static FormatFamily DeriveFamily(FormatFamilyDraft draft) =>
        new()
        {
            FamilyId = draft.FamilyId,
            Name = draft.Name,
            FileExtensions = draft.Extensions,
            Quality = draft.Quality
                ?? throw new InvalidOperationException(
                    $"The format family '{draft.FamilyId}' declared no quality model, so nothing can read "
                    + "what one of its files is."),
            CoexistsWithOtherFamilies = draft.CoexistsWithOtherFamilies,
            SupportsEmbeddedMetadata = draft.SupportsEmbeddedMetadata,
            TechnicalFacets = draft.Facets
        };

    private static SelectionFacet DeriveFacet(SelectionDraft draft, MediaLevelId levelId) =>
        new()
        {
            FacetId = draft.FacetId,
            Name = draft.Name,
            AppliesToLevelId = levelId,
            Kind = draft.Kind,
            Values = draft.Values,

            // Read off the source being an enumeration rather than declared. An ordered enumeration is a
            // threshold over named values, and a consumer that renders it as independent choices is
            // answering a different question from the one the domain asks.
            ValuesAreOrdered = draft.Kind == SelectionFacetKind.Enumerated,
            MultiValued = false,
            DefaultAllowed = draft.DefaultAllowed,
            ThresholdDirection = draft.ThresholdDirection,
            DefaultNumber = draft.DefaultNumber,
            Unit = draft.Unit,
            Application = draft.Application
        };

    private static SearchKind DeriveSearch(SearchDraft draft, MediaLevelId levelId) =>
        new()
        {
            SearchKindId = draft.SearchKindId,
            Name = draft.Name,
            TargetLevelId = levelId,
            Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
            RequiredTerms = draft.Required,
            OptionalTerms = draft.Optional,
            Categories = draft.Categories
        };
}
