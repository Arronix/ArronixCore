using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Reads an entity type into the fields it declares, and refuses one that is not well formed.
/// </summary>
/// <remarks>
/// The source generator emits closed property readers whose names and types are checked by the compiler.
/// This class validates the resulting semantic projection at plugin admission because the host does not
/// trust an external assembly merely because a compatible generator normally builds it.
/// </remarks>
internal sealed class ItemTypeReader
{
    private ItemTypeReader(Type entityType, IReadOnlyList<DerivedField> fields)
    {
        EntityType = entityType;
        Fields = fields;
    }

    /// <summary>Gets the entity type read.</summary>
    internal Type EntityType { get; }

    /// <summary>Gets the fields, in declaration order.</summary>
    internal IReadOnlyList<DerivedField> Fields { get; }

    /// <summary>Gets the field carrying the entity's title.</summary>
    internal DerivedField Title => Fields.Single(candidate => candidate.Carries(FieldSemantics.Title));

    /// <summary>Gets the field stating the title's language, when the entity declares one.</summary>
    internal DerivedField? TitleLanguage =>
        Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.PropertyName, nameof(IMediaEntity.TitleLanguage), StringComparison.Ordinal));

    /// <summary>Gets the field carrying the entity's external identifiers, when it has one.</summary>
    internal DerivedField? ExternalIds =>
        Fields.FirstOrDefault(candidate => candidate.PropertyType == typeof(ExternalIdSet));

    /// <summary>Gets the field reporting the entity's condition, when it has one.</summary>
    internal DerivedField? Status =>
        Fields.FirstOrDefault(candidate => candidate.Carries(FieldSemantics.Status));

    /// <summary>
    /// Reads an entity type.
    /// </summary>
    /// <param name="shape">The generated entity shape.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The type is not a well-formed entity.</exception>
    internal static ItemTypeReader Read(CompiledEntityShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var fields = shape.Fields.Select(static field => new DerivedField(field)).ToArray();

        Verify(shape.EntityType, fields);

        return new ItemTypeReader(shape.EntityType, fields);
    }

    /// <summary>
    /// Reads a type that carries fields but is not an entity — a working surface's row.
    /// </summary>
    /// <param name="shape">The generated row shape.</param>
    /// <returns>The fields, in declaration order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The same field derivation without the entity rules, because a row has no identity of its own and no
    /// obligation to name itself: it is a set of columns, and a column is a field.
    /// </remarks>
    internal static IReadOnlyList<DerivedField> ReadRow(CompiledEntityShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return [.. shape.Fields.Select(static field => new DerivedField(field))];
    }

    private static void Verify(Type entityType, IReadOnlyList<DerivedField> fields)
    {
        if (typeof(IMediaEntity).IsAssignableFrom(entityType))
        {
            VerifyCompiledEntity(entityType, fields);
            VerifyOptionalSemantics(entityType, fields);
            VerifyNoDuplicateFields(entityType, fields);
            return;
        }

        var keys = fields
            .Where(static candidate => candidate.ExplicitIdentity)
            .ToArray();

        if (keys.Length != 1)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' declares {keys.Length} properties marked as its identity; exactly "
                + "one is required.",
                nameof(entityType));
        }

        if (keys[0].PropertyType != typeof(MediaItemId))
        {
            throw new ArgumentException(
                $"'{entityType.FullName}.{keys[0].PropertyName}' is marked as the identity but is of type "
                + $"'{keys[0].PropertyType.Name}'; the identity is a host-minted "
                + $"'{nameof(MediaItemId)}'.",
                nameof(entityType));
        }

        var titles = fields.Where(candidate => candidate.Carries(FieldSemantics.Title)).ToArray();

        if (titles.Length != 1)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' declares {titles.Length} properties marked as its title; exactly "
                + "one is required, because a consumer that could not name an entity could not list it.",
                nameof(entityType));
        }

        VerifyOptionalSemantics(entityType, fields);
        VerifyNoDuplicateFields(entityType, fields);
    }

    private static void VerifyCompiledEntity(Type entityType, IReadOnlyList<DerivedField> fields)
    {
        var required = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(IMediaEntity.ExternalIds)] = typeof(ExternalIdSet),
            [nameof(IMediaEntity.Title)] = typeof(string),
            [nameof(IMediaEntity.TitleLanguage)] = typeof(Language),
            [nameof(IMediaEntity.Overview)] = typeof(string),
            [nameof(IMediaEntity.Artwork)] = typeof(ArtworkSet)
        };

        foreach (var (name, expected) in required)
        {
            var field = fields.SingleOrDefault(candidate =>
                string.Equals(candidate.PropertyName, name, StringComparison.Ordinal));

            if (field is null || field.ElementType != expected)
            {
                throw new ArgumentException(
                    $"'{entityType.FullName}' does not expose the compiled media-entity member '{name}' "
                    + $"with value type '{expected.Name}'.",
                    nameof(entityType));
            }
        }
    }

    private static void VerifyOptionalSemantics(Type entityType, IReadOnlyList<DerivedField> fields)
    {
        var statuses = fields.Where(candidate => candidate.Carries(FieldSemantics.Status)).ToArray();

        var titleLanguages = fields
            .Where(candidate =>
                string.Equals(candidate.PropertyName, nameof(IMediaEntity.TitleLanguage), StringComparison.Ordinal))
            .ToArray();

        if (titleLanguages.Length > 1)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' declares {titleLanguages.Length} title-language properties; at "
                + "most one is allowed.",
                nameof(entityType));
        }

        if (titleLanguages.Length == 1 && titleLanguages[0].ElementType != typeof(Language))
        {
            throw new ArgumentException(
                $"'{entityType.FullName}.{titleLanguages[0].PropertyName}' is the title language "
                + $"but is not a '{nameof(Language)}'.",
                nameof(entityType));
        }

        if (statuses.Length > 1)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' declares {statuses.Length} properties marked as its status; at "
                + "most one is allowed.",
                nameof(entityType));
        }

        if (statuses.Length == 1 && !statuses[0].ElementType.IsEnum)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}.{statuses[0].PropertyName}' is marked as the status but is not an "
                + "enumeration; the states and their order are read from the enumeration's members.",
                nameof(entityType));
        }

    }

    private static void VerifyNoDuplicateFields(Type entityType, IReadOnlyList<DerivedField> fields)
    {
        var duplicate = fields
            .GroupBy(candidate => candidate.FieldId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' derives the field identifier '{duplicate.Key}' from more than one "
                + "property.",
                nameof(entityType));
        }
    }
}
