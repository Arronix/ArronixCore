using System.Linq;
using System.Reflection;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0005
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Turns one property into the field descriptor every consumer already reads.
/// </summary>
/// <remarks>
/// <para>
/// Two sources and no third: the property's <i>type</i>, which gives the value shape, the operators, the
/// choices and the components; and its <i>attributes</i>, which give what the type cannot know — that this
/// text is a title, that this number is a size, that this field is worth showing first.
/// </para>
/// <para>
/// The consequence worth stating is what stops being written. A repeated tuple is one multivalued composite
/// field derived from its element type, not several parallel lists correlated by index whose correlation is
/// undeclarable; and the comparisons a filter admits follow from the type rather than from a hand-written
/// table that drifts the moment a property changes shape.
/// </para>
/// </remarks>
internal static class FieldDescriptorFactory
{
    /// <summary>
    /// Derives a field from a property, or reports that the property is not a field.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="field">The derived field when one was derived; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the property is a field.</returns>
    internal static bool TryDerive(PropertyInfo property, out DerivedField? field)
    {
        ArgumentNullException.ThrowIfNull(property);

        field = null;

        if (property.GetIndexParameters().Length > 0
            || property.GetMethod is null
            || !property.GetMethod.IsPublic
            || property.GetCustomAttribute<IgnoreAttribute>() is not null)
        {
            return false;
        }

        field = Derive(property);
        return true;
    }

    private static DerivedField Derive(PropertyInfo property)
    {
        var declared = property.PropertyType;
        var multivalued = TryUnwrapList(declared, out var listElement) || IsSetType(declared);
        var element = StripNullable(listElement ?? declared);
        var nullable = IsNullableType(declared) || (listElement is not null && IsNullableType(listElement));

        var display = property.GetCustomAttribute<DisplayAttribute>();
        var valueKind = ValueKindOf(element, property);
        var semantics = SemanticsOf(property, element, valueKind);

        var descriptor = new FieldDescriptor
        {
            FieldId = DerivedNames.Identifier(property.Name),
            Name = display?.Name ?? DerivedNames.Label(property.Name),
            Description = display?.Description,
            ValueKind = valueKind,
            Semantics = semantics,
            Prominence = property.GetCustomAttribute<ProminenceAttribute>()?.Prominence ?? Prominence.Detail,
            Multivalued = multivalued,
            Editable = property.GetCustomAttribute<EditableAttribute>() is not null
                && property.GetCustomAttribute<DerivedAttribute>() is null,
            Unit = property.GetCustomAttribute<UnitAttribute>()?.Unit,
            Choices = valueKind == FieldValueKind.Enumerated ? ChoicesOf(element) : [],
            Components = valueKind == FieldValueKind.Composite ? ComponentsOf(element) : []
        };

        return new DerivedField(
            property,
            descriptor,
            OperatorsOf(valueKind, multivalued, nullable),
            element,
            nullable,
            display?.Example,
            IsNameable(valueKind));
    }

    /// <summary>
    /// Maps a value shape onto the comparisons a filter may offer for it.
    /// </summary>
    /// <param name="kind">The value shape.</param>
    /// <param name="multivalued">Whether the field holds several values.</param>
    /// <param name="nullable">Whether the field can be absent.</param>
    /// <returns>The comparisons.</returns>
    /// <remarks>
    /// Derived rather than declared. The alternative — a per-field operator list — is a table nothing checks
    /// and which is wrong the moment somebody changes a property's type without remembering it exists.
    /// </remarks>
    internal static FilterOperators OperatorsOf(FieldValueKind kind, bool multivalued, bool nullable)
    {
        var operators = multivalued
            ? FilterOperators.In | FilterOperators.Contains
            : kind switch
            {
                FieldValueKind.Text or FieldValueKind.MultilineText or FieldValueKind.FilePath =>
                    FilterOperators.Contains | FilterOperators.Equals,

                FieldValueKind.Integer or FieldValueKind.Decimal or FieldValueKind.ByteSize
                    or FieldValueKind.Ratio or FieldValueKind.Count =>
                    FilterOperators.Equals | FilterOperators.In | FilterOperators.GreaterThan
                    | FilterOperators.LessThan | FilterOperators.Between,

                FieldValueKind.Date or FieldValueKind.Instant or FieldValueKind.Duration =>
                    FilterOperators.GreaterThan | FilterOperators.LessThan | FilterOperators.Between,

                FieldValueKind.Enumerated or FieldValueKind.Quality or FieldValueKind.Language =>
                    FilterOperators.Equals | FilterOperators.NotEquals | FilterOperators.In,

                FieldValueKind.Boolean => FilterOperators.Equals,

                _ => FilterOperators.Equals
            };

        return nullable ? operators | FilterOperators.IsNull : operators;
    }

    private static FieldValueKind ValueKindOf(Type type, PropertyInfo property)
    {
        if (property.GetCustomAttribute<SizeAttribute>() is not null)
        {
            return FieldValueKind.ByteSize;
        }

        if (property.GetCustomAttribute<CountAttribute>() is not null)
        {
            return FieldValueKind.Count;
        }

        if (property.GetCustomAttribute<RatioAttribute>() is not null)
        {
            return FieldValueKind.Ratio;
        }

        if (type == typeof(string))
        {
            return property.GetCustomAttribute<MultilineAttribute>() is not null
                ? FieldValueKind.MultilineText
                : FieldValueKind.Text;
        }

        if (type.IsEnum)
        {
            return FieldValueKind.Enumerated;
        }

        if (type == typeof(ArtworkSet) || type == typeof(ArtworkImage))
        {
            return FieldValueKind.Artwork;
        }

        if (type == typeof(ExternalIdSet) || type == typeof(ExternalId))
        {
            return FieldValueKind.ExternalIdentifier;
        }

        if (type == typeof(MediaItemId))
        {
            return FieldValueKind.Integer;
        }

        return type switch
        {
            _ when type == typeof(int) || type == typeof(long) || type == typeof(short) => FieldValueKind.Integer,
            _ when type == typeof(double) || type == typeof(decimal) || type == typeof(float) => FieldValueKind.Decimal,
            _ when type == typeof(bool) => FieldValueKind.Boolean,
            _ when type == typeof(DateOnly) => FieldValueKind.Date,
            _ when type == typeof(DateTimeOffset) || type == typeof(DateTime) => FieldValueKind.Instant,
            _ when type == typeof(TimeSpan) => FieldValueKind.Duration,
            _ when type == typeof(Uri) => FieldValueKind.Link,
            _ when type == typeof(PlatformPath) => FieldValueKind.FilePath,
            _ when type == typeof(Language) => FieldValueKind.Language,
            _ when type == typeof(QualityTier) => FieldValueKind.Quality,
            _ when type == typeof(OrdinalPath) => FieldValueKind.Ordinal,
            _ when IsEntity(type) => FieldValueKind.Reference,
            _ => FieldValueKind.Composite
        };
    }

    private static FieldSemantics SemanticsOf(PropertyInfo property, Type element, FieldValueKind kind)
    {
        var semantics = FieldSemantics.None;

        foreach (var (attribute, meaning) in SemanticAttributes)
        {
            if (property.IsDefined(attribute, inherit: false))
            {
                semantics |= meaning;
            }
        }

        // Identity is derived, not written twice: the key carries it, and so does any external-identifier
        // set, because both of them are how a person or a catalog names the same entity.
        if (property.GetCustomAttribute<IdentityAttribute>() is not null
            || kind == FieldValueKind.ExternalIdentifier)
        {
            semantics |= FieldSemantics.Identity;
        }

        // A title is always sortable. Every surveyed kind's default listing is ordered by title, and a kind
        // that had to remember to say so would eventually forget — leaving a library that cannot be ordered
        // by the one field every item is guaranteed to have.
        if ((semantics & FieldSemantics.Title) != FieldSemantics.None)
        {
            semantics |= FieldSemantics.Sortable;
        }

        if (element == typeof(ArtworkSet) || element == typeof(ArtworkImage))
        {
            semantics |= FieldSemantics.Artwork;
        }

        return semantics;
    }

    private static IReadOnlyList<FacetValue> ChoicesOf(Type enumType) =>
        [.. EnumOrder.Names(enumType)
            .Select(name => new FacetValue(DerivedNames.Identifier(name), DerivedNames.Label(name)))];

    private static IReadOnlyList<FieldDescriptor> ComponentsOf(Type composite)
    {
        // One level deep, deliberately. A composite whose components are themselves composites is a
        // hierarchy, and a hierarchy of fields is a level — which is a different declaration.
        var components = new List<FieldDescriptor>();

        foreach (var property in composite.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0
                || property.GetCustomAttribute<IgnoreAttribute>() is not null
                || string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
            {
                continue;
            }

            var multivalued = TryUnwrapList(property.PropertyType, out var listElement);
            var element = StripNullable(listElement ?? property.PropertyType);
            var kind = ValueKindOf(element, property);

            components.Add(new FieldDescriptor
            {
                FieldId = DerivedNames.Identifier(property.Name),
                Name = property.GetCustomAttribute<DisplayAttribute>()?.Name ?? DerivedNames.Label(property.Name),
                ValueKind = kind == FieldValueKind.Composite ? FieldValueKind.Text : kind,
                Multivalued = multivalued,
                Choices = kind == FieldValueKind.Enumerated ? ChoicesOf(element) : []
            });
        }

        return components;
    }

    private static bool IsNameable(FieldValueKind kind) =>
        kind is not (FieldValueKind.Artwork or FieldValueKind.Reference or FieldValueKind.Composite);

    private static bool IsEntity(Type type) =>
        typeof(IMediaItem).IsAssignableFrom(type)
        || type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IMediaGroup<>));

    private static bool IsSetType(Type type) => type == typeof(ArtworkSet) || type == typeof(ExternalIdSet);

    private static bool IsNullableType(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static Type StripNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool TryUnwrapList(Type type, out Type? element)
    {
        element = null;

        if (type == typeof(string) || !type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();

        if (definition != typeof(IReadOnlyList<>)
            && definition != typeof(IReadOnlyCollection<>)
            && definition != typeof(IEnumerable<>))
        {
            return false;
        }

        element = type.GetGenericArguments()[0];
        return true;
    }

    private static readonly (Type Attribute, FieldSemantics Meaning)[] SemanticAttributes =
    [
        (typeof(TitleAttribute), FieldSemantics.Title),
        (typeof(SearchableAttribute), FieldSemantics.Searchable),
        (typeof(SortableAttribute), FieldSemantics.Sortable),
        (typeof(FilterableAttribute), FieldSemantics.Filterable),
        (typeof(GroupableAttribute), FieldSemantics.Groupable),
        (typeof(DisambiguationAttribute), FieldSemantics.Disambiguation),
        (typeof(StatusAttribute), FieldSemantics.Status),
        (typeof(TimestampAttribute), FieldSemantics.Timestamp),
        (typeof(ArtworkAttribute), FieldSemantics.Artwork),
        (typeof(SizeAttribute), FieldSemantics.Size),
        (typeof(ProgressAttribute), FieldSemantics.Progress)
    ];
}
