using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Media;

/// <summary>
/// A declared shape that has been checked once and can no longer disappoint a caller.
/// </summary>
/// <remarks>
/// <para>
/// This is the load-bearing safety move of the whole media model. Every identifier-to-object cross reference
/// in a declaration is resolved here, once, at load. After the gate <see cref="LevelOf"/> is total,
/// <see cref="AcquisitionUnit"/> is non-null, the level graph is acyclic and topologically ordered, and
/// every sequence axis points at a coordinate component that exists. Downstream host code — the scheduler,
/// the importer, the projection, the API — cannot write a lookup that fails, because there are no lookups
/// left to write.
/// </para>
/// <para>
/// The alternative, a validator that returns a verdict and leaves the declaration as it was, keeps every
/// downstream call site responsible for the failure it can no longer cause. That is how identifier handling
/// rots: each site handles the impossible case slightly differently, and one of them handles it by throwing
/// in a background job at three in the morning.
/// </para>
/// <para>
/// Held host-side deliberately. An extension declares a shape; it never reads another extension's, because
/// direct cross-extension interaction is forbidden. The promotion trigger is an extension that must read a
/// foreign shape, which would itself need review.
/// </para>
/// </remarks>
public sealed class ValidatedShape
{
    private readonly Dictionary<MediaLevelId, MediaLevel> _levelsById;
    private readonly Dictionary<string, CoordinateSpace> _spacesById;
    private readonly Dictionary<string, SearchKind> _searchKindsById;
    private readonly Dictionary<string, FormatFamily> _familiesByExtension;
    private readonly Dictionary<MediaLevelId, IReadOnlyList<MediaLevel>> _pathsByLevel;

    private ValidatedShape(
        MediaShape declaration,
        IReadOnlyList<MediaLevel> ordered,
        MediaLevel libraryEntry,
        MediaLevel acquisitionUnit,
        MediaLevel? variantLevel,
        MediaLevel completenessUnit,
        MediaLevel fileAnchor,
        MediaLevel fileUnit)
    {
        Declaration = declaration;
        Levels = ordered;
        LibraryEntry = libraryEntry;
        AcquisitionUnit = acquisitionUnit;
        VariantLevel = variantLevel;
        CompletenessUnit = completenessUnit;
        FileAnchor = fileAnchor;
        FileUnit = fileUnit;

        _levelsById = ordered.ToDictionary(level => level.Id);
        _spacesById = declaration.CoordinateSpaces.ToDictionary(space => space.SpaceId, StringComparer.Ordinal);
        _searchKindsById = declaration.SearchKinds.ToDictionary(kind => kind.SearchKindId, StringComparer.Ordinal);

        _familiesByExtension = new Dictionary<string, FormatFamily>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in declaration.FormatFamilies)
        {
            foreach (var extension in family.FileExtensions)
            {
                _familiesByExtension[Normalize(extension)] = family;
            }
        }

        _pathsByLevel = new Dictionary<MediaLevelId, IReadOnlyList<MediaLevel>>();
        foreach (var level in ordered)
        {
            var path = new List<MediaLevel>();
            var cursor = level;
            while (true)
            {
                path.Insert(0, cursor);
                if (cursor.Parent is not { } parentId)
                {
                    break;
                }

                cursor = _levelsById[parentId];
            }

            _pathsByLevel[level.Id] = path;
        }
    }

    /// <summary>
    /// Gets the media kind this shape describes.
    /// </summary>
    public MediaKindId Kind => Declaration.Kind;

    /// <summary>
    /// Gets the declaration verbatim, so what is published on the wire is what the extension wrote.
    /// </summary>
    public MediaShape Declaration { get; }

    /// <summary>
    /// Gets the levels, topologically ordered with the root first.
    /// </summary>
    public IReadOnlyList<MediaLevel> Levels { get; }

    /// <summary>
    /// Gets the level a user adds — the one that owns a path, profiles and tags. Exactly one exists.
    /// </summary>
    public MediaLevel LibraryEntry { get; }

    /// <summary>
    /// Gets the level a search or a grab targets. The deepest one carrying the role, so that a shape which
    /// fuses roles onto one level still answers correctly.
    /// </summary>
    public MediaLevel AcquisitionUnit { get; }

    /// <summary>
    /// Gets the level whose members compete to be the chosen manifestation, when the shape has one.
    /// </summary>
    public MediaLevel? VariantLevel { get; }

    /// <summary>
    /// Gets the level "have" and "missing" are counted in.
    /// </summary>
    public MediaLevel CompletenessUnit { get; }

    /// <summary>
    /// Gets the level a file row hangs from. May sit above <see cref="FileUnit"/>, which is what lets file
    /// ownership survive a change of chosen variant.
    /// </summary>
    public MediaLevel FileAnchor { get; }

    /// <summary>
    /// Gets the level a file satisfies.
    /// </summary>
    public MediaLevel FileUnit { get; }

    /// <summary>
    /// Checks a declaration and, when it is sound, produces the resolved form.
    /// </summary>
    /// <param name="shape">The declaration to check.</param>
    /// <param name="validated">The resolved shape when the declaration is sound; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Every fault found. Empty exactly when the declaration is sound.</param>
    /// <returns><see langword="true"/> when the declaration is sound.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Uniqueness of the media kind across extensions is not checked here, because one declaration cannot
    /// see another. The registry enforces it on admission.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "The try-and-out form is the parse-don't-validate idiom this type exists to provide, and it returns two results: the parsed shape and the complete defect list.")]
    public static bool TryValidate(
        MediaShape shape,
        out ValidatedShape? validated,
        out IReadOnlyList<ShapeDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var found = new List<ShapeDefect>();
        validated = null;

        var ordered = ShapeValidationRules.Check(shape, found);

        if (found.Count > 0 || ordered is null)
        {
            defects = found;
            return false;
        }

        var levelsById = ordered.ToDictionary(level => level.Id);

        validated = new ValidatedShape(
            shape,
            ordered,
            ordered.Last(level => level.Roles.HasFlag(MediaLevelRoles.LibraryEntry)),
            ordered.Last(level => level.Roles.HasFlag(MediaLevelRoles.AcquisitionUnit)),
            ordered.LastOrDefault(level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis)),
            ordered.Last(level => level.Roles.HasFlag(MediaLevelRoles.CompletenessUnit)),
            levelsById[shape.FileBinding.AnchorLevelId],
            levelsById[shape.FileBinding.UnitLevelId]);

        defects = [];
        return true;
    }

    /// <summary>
    /// Gets a level by its identifier.
    /// </summary>
    /// <param name="id">The identifier, which came from this shape.</param>
    /// <returns>The level.</returns>
    /// <exception cref="KeyNotFoundException">
    /// The identifier is not from this shape. Reaching this is a defect in the caller, not a runtime
    /// condition: every identifier the host holds was resolved by the gate.
    /// </exception>
    public MediaLevel LevelOf(MediaLevelId id) => _levelsById[id];

    /// <summary>
    /// Determines whether a level exists in this shape.
    /// </summary>
    /// <param name="id">The identifier to test.</param>
    /// <returns><see langword="true"/> when the shape declares it.</returns>
    /// <remarks>
    /// The one place an unresolved identifier is legitimate is at the edge, where an identifier arrived from
    /// a request rather than from the shape. Everywhere else, use <see cref="LevelOf"/>.
    /// </remarks>
    public bool HasLevel(MediaLevelId id) => _levelsById.ContainsKey(id);

    /// <summary>
    /// Gets the chain from the root down to a level, inclusive.
    /// </summary>
    /// <param name="id">The level the chain ends at.</param>
    /// <returns>The chain, root first.</returns>
    public IReadOnlyList<MediaLevel> PathTo(MediaLevelId id) => _pathsByLevel[id];

    /// <summary>
    /// Gets the coordinate space a level's identity and completeness are measured in.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <returns>The canonical space, or <see langword="null"/> when the level carries no coordinates.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    public CoordinateSpace? CanonicalSpaceOf(MediaLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);

        foreach (var spaceId in level.CoordinateSpaceIds)
        {
            if (_spacesById.TryGetValue(spaceId, out var space) && space.IsCanonical)
            {
                return space;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a declared coordinate space.
    /// </summary>
    /// <param name="spaceId">The space's identifier.</param>
    /// <returns>The space, or <see langword="null"/> when the shape does not declare it.</returns>
    public CoordinateSpace? SpaceOf(string spaceId)
        => spaceId is not null && _spacesById.TryGetValue(spaceId, out var space) ? space : null;

    /// <summary>
    /// Gets the format family a file extension belongs to.
    /// </summary>
    /// <param name="extension">The extension, with or without its leading dot.</param>
    /// <returns>The family, or <see langword="null"/> when no family claims the extension.</returns>
    /// <remarks>
    /// Total because extension sets are validated pairwise disjoint: an extension belongs to at most one
    /// family, which is what makes a per-family quality ladder decidable at all.
    /// </remarks>
    public FormatFamily? FamilyForExtension(string extension)
        => string.IsNullOrWhiteSpace(extension)
            ? null
            : _familiesByExtension.GetValueOrDefault(Normalize(extension));

    /// <summary>
    /// Gets a declared search.
    /// </summary>
    /// <param name="searchKindId">The search's identifier.</param>
    /// <returns>The search.</returns>
    /// <exception cref="ArronixException">The shape declares no such search.</exception>
    public SearchKind RequireSearchKind(string searchKindId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchKindId);

        return _searchKindsById.TryGetValue(searchKindId, out var kind)
            ? kind
            : throw new ArronixException(
                CoreErrorCode.MediaKindNotFound,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Media kind '{Kind}' declares no search '{searchKindId}'."));
    }

    /// <summary>
    /// Gets the sequence axes declared anywhere in the shape.
    /// </summary>
    /// <returns>Every axis, in level order.</returns>
    public IReadOnlyList<SequenceAxis> AllSequenceAxes()
        => [.. Levels.SelectMany(level => level.SequenceAxes)];

    private static string Normalize(string extension)
        => extension.StartsWith('.') ? extension[1..] : extension;
}
