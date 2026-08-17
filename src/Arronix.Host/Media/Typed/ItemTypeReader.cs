using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Reads an entity type into the fields it declares, and refuses one that is not well formed.
/// </summary>
/// <remarks>
/// The well-formedness rules are enforced twice on purpose. An analyzer catches them at the author's
/// keyboard, which is where a compile error belongs; this catches them at load, because a host that trusted
/// a plugin to have been built with the analyzer switched on would be trusting the plugin. The analyzer is
/// the good error message; this is the gate.
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

    /// <summary>Gets the field carrying the entity's key.</summary>
    internal DerivedField Key =>
        Fields.Single(candidate => candidate.Property.GetCustomAttribute<IdentityAttribute>() is not null);

    /// <summary>Gets the field carrying the entity's external identifiers, when it has one.</summary>
    internal DerivedField? ExternalIds =>
        Fields.FirstOrDefault(candidate => candidate.Property.PropertyType == typeof(ExternalIdSet));

    /// <summary>Gets the field reporting the entity's condition, when it has one.</summary>
    internal DerivedField? Status =>
        Fields.FirstOrDefault(candidate => candidate.Carries(FieldSemantics.Status));

    /// <summary>
    /// Reads an entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The type is not a well-formed entity.</exception>
    internal static ItemTypeReader Read(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var fields = new List<DerivedField>();

        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            {
                continue;
            }

            if (FieldDescriptorFactory.TryDerive(property, out var derived) && derived is not null)
            {
                fields.Add(derived);
            }
        }

        Verify(entityType, fields);

        return new ItemTypeReader(entityType, fields);
    }

    /// <summary>
    /// Reads a type that carries fields but is not an entity — a working surface's row.
    /// </summary>
    /// <param name="rowType">The row type.</param>
    /// <returns>The fields, in declaration order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rowType"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The same field derivation without the entity rules, because a row has no identity of its own and no
    /// obligation to name itself: it is a set of columns, and a column is a field.
    /// </remarks>
    internal static IReadOnlyList<DerivedField> ReadRow(Type rowType)
    {
        ArgumentNullException.ThrowIfNull(rowType);

        var fields = new List<DerivedField>();

        foreach (var property in rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            {
                continue;
            }

            if (FieldDescriptorFactory.TryDerive(property, out var derived) && derived is not null)
            {
                fields.Add(derived);
            }
        }

        return fields;
    }

    private static void Verify(Type entityType, IReadOnlyList<DerivedField> fields)
    {
        var keys = fields
            .Where(candidate => candidate.Property.GetCustomAttribute<IdentityAttribute>() is not null)
            .ToArray();

        if (keys.Length != 1)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' declares {keys.Length} properties marked as its identity; exactly "
                + "one is required.",
                nameof(entityType));
        }

        if (keys[0].Property.PropertyType != typeof(MediaItemId))
        {
            throw new ArgumentException(
                $"'{entityType.FullName}.{keys[0].Property.Name}' is marked as the identity but is of type "
                + $"'{keys[0].Property.PropertyType.Name}'; the identity is a host-minted "
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

        var statuses = fields.Where(candidate => candidate.Carries(FieldSemantics.Status)).ToArray();

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
                $"'{entityType.FullName}.{statuses[0].Property.Name}' is marked as the status but is not an "
                + "enumeration; the states and their order are read from the enumeration's members.",
                nameof(entityType));
        }

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
