using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Arronix.Generators;

/// <summary>
/// The one reading of a CLR media shape: which properties an entity has, what each one is, and what it
/// means.
/// </summary>
/// <remarks>
/// <para>
/// Every generator that describes an entity reads it from here. What each generator does with the reading
/// differs — Host receives compiled getters over a value it holds, a browser receives a one-way projection
/// of a value it does not — but the reading itself is one semantic source. Two walks over the same
/// properties are two chances for a field to exist on one side and not the other, and that difference
/// shows up as a browser silently missing a field rather than as an error.
/// </para>
/// <para>
/// Filtering is left to the caller. <see cref="PublicProperties"/> answers what a type declares; whether an
/// <c>[Ignore]</c>d property or a record's equality contract belongs in a particular output is the output's
/// question.
/// </para>
/// </remarks>
internal static class MediaShapeModel
{
    /// <summary>Reads a type's public instance properties, base class first, overrides collapsed.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The properties, in declaration order from the root of the hierarchy down.</returns>
    internal static IReadOnlyList<IPropertySymbol> PublicProperties(INamedTypeSymbol type)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            hierarchy.Push(current);
        }

        var result = new List<IPropertySymbol>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        while (hierarchy.Count > 0)
        {
            foreach (var property in hierarchy.Pop().GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.Parameters.Length != 0
                    || property.DeclaredAccessibility != Accessibility.Public
                    || property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (positions.TryGetValue(property.Name, out var position))
                {
                    result[position] = property;
                }
                else
                {
                    positions[property.Name] = result.Count;
                    result.Add(property);
                }
            }
        }

        return result;
    }

    /// <summary>Determines whether a property carries a value a generated output must not describe.</summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> when it is excluded from every description.</returns>
    internal static bool IsExcluded(IPropertySymbol property) =>
        Has(property, "IgnoreAttribute") || property.Name == "EqualityContract";

    /// <summary>Gets the element type of a read-only sequence, or <see langword="null"/>.</summary>
    /// <param name="type">The declared type.</param>
    /// <returns>The element type when the declaration is a read-only sequence.</returns>
    internal static ITypeSymbol? UnwrapList(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType) return null;
        var definition = named.OriginalDefinition.ToDisplayString();
        return definition is "System.Collections.Generic.IReadOnlyList<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>"
            or "System.Collections.Generic.IEnumerable<T>"
            ? named.TypeArguments[0]
            : null;
    }

    /// <summary>Removes a nullable value type's wrapper.</summary>
    /// <param name="type">The declared type.</param>
    /// <returns>The underlying type.</returns>
    internal static ITypeSymbol StripNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    /// <summary>Determines whether a declaration admits absence.</summary>
    /// <param name="type">The declared type.</param>
    /// <returns><see langword="true"/> when the declaration is nullable.</returns>
    internal static bool IsNullable(ITypeSymbol type) =>
        type.IsReferenceType || type.NullableAnnotation == NullableAnnotation.Annotated
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>Determines whether a type is, or implements, a named contract.</summary>
    /// <param name="type">The type.</param>
    /// <param name="interfaceName">The fully qualified contract name.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    internal static bool Implements(ITypeSymbol type, string interfaceName) =>
        Is(type, interfaceName) || type.AllInterfaces.Any(candidate => Is(candidate, interfaceName));

    /// <summary>Determines whether a type is exactly a named type.</summary>
    /// <param name="type">The type.</param>
    /// <param name="fullName">The fully qualified name.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool Is(ITypeSymbol type, string fullName) =>
        type.WithNullableAnnotation(NullableAnnotation.None)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + fullName;

    /// <summary>Reads one attribute of a property by its short name.</summary>
    /// <param name="property">The property.</param>
    /// <param name="shortName">The attribute class name.</param>
    /// <returns>The attribute, or <see langword="null"/>.</returns>
    internal static AttributeData? Attribute(IPropertySymbol property, string shortName) =>
        property.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.Name == shortName);

    /// <summary>Determines whether a property carries an attribute.</summary>
    /// <param name="property">The property.</param>
    /// <param name="shortName">The attribute class name.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    internal static bool Has(IPropertySymbol property, string shortName) => Attribute(property, shortName) is not null;

    /// <summary>Reads one named string argument of an attribute.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="name">The argument name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    internal static string? NamedString(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    /// <summary>Reads the first positional string argument of an attribute.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    internal static string? ConstructorString(AttributeData? attribute) =>
        attribute?.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;

    /// <summary>Renders a type the way generated code must spell it.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The globally qualified name.</returns>
    internal static string TypeName(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.None)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>Renders a member name as the identifier consumers reference it by.</summary>
    /// <param name="name">The member name.</param>
    /// <returns>The identifier.</returns>
    internal static string Identifier(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    /// <summary>Renders a member name as a display label.</summary>
    /// <param name="name">The member name.</param>
    /// <returns>The label.</returns>
    internal static string Label(string name)
    {
        var result = new StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var startsWord = index > 0 && char.IsUpper(name[index]) && !char.IsUpper(name[index - 1]);
            if (startsWord) result.Append(' ');
            result.Append(startsWord ? char.ToLowerInvariant(name[index]) : name[index]);
        }
        return result.ToString();
    }

    /// <summary>Classifies the shape a property's values take.</summary>
    /// <param name="type">The element type.</param>
    /// <param name="property">The declaring property.</param>
    /// <returns>The <c>FieldValueKind</c> value.</returns>
    internal static int ValueKind(ITypeSymbol type, IPropertySymbol property)
    {
        if (Has(property, "SizeAttribute")) return 8;
        if (Has(property, "CountAttribute")) return 19;
        if (Has(property, "RatioAttribute")) return 9;
        if (type.SpecialType == SpecialType.System_String) return Has(property, "MultilineAttribute") ? 1 : 0;
        if (type.TypeKind == TypeKind.Enum) return 11;
        if (Is(type, "Arronix.Abstractions.Media.ArtworkSet") || Is(type, "Arronix.Abstractions.Media.ArtworkImage")) return 18;
        if (Is(type, "Arronix.Abstractions.Media.ExternalIdSet") || Is(type, "Arronix.Abstractions.Shape.ExternalId")) return 13;
        if (Is(type, "Arronix.Abstractions.Identity.MediaItemId")) return 2;
        if (type.SpecialType is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64) return 2;
        if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal) return 3;
        if (type.SpecialType == SpecialType.System_Boolean) return 4;
        if (Is(type, "System.DateOnly")) return 5;
        if (Is(type, "System.DateTime") || Is(type, "System.DateTimeOffset")) return 6;
        if (Is(type, "System.TimeSpan")) return 7;
        if (Is(type, "System.Uri")) return 14;
        if (Is(type, "Arronix.Abstractions.FileSystem.PlatformPath")) return 15;
        if (Is(type, "Arronix.Abstractions.DTOs.Language")) return 16;
        if (Is(type, "Arronix.Abstractions.DTOs.QualityTier")) return 17;
        if (Is(type, "Arronix.Abstractions.Shape.OrdinalPath")) return 10;
        if (Implements(type, "Arronix.Abstractions.Media.IMediaEntity")) return 12;
        return 20;
    }

    /// <summary>Reads what a property means to the platform beyond the shape of its values.</summary>
    /// <param name="property">The property.</param>
    /// <param name="kind">Its value shape.</param>
    /// <param name="element">Its element type.</param>
    /// <returns>The <c>FieldSemantics</c> flags.</returns>
    internal static int Semantics(IPropertySymbol property, int kind, ITypeSymbol element)
    {
        var result = 0;
        if (Has(property, "IdentityAttribute")) result |= 1;
        if (Has(property, "TitleAttribute")) result |= 2;
        if (Has(property, "SortableAttribute")) result |= 8;
        if (Has(property, "FilterableAttribute")) result |= 16;
        if (Has(property, "GroupableAttribute")) result |= 32;
        if (Has(property, "SearchableAttribute")) result |= 64;
        if (Has(property, "ProgressAttribute")) result |= 128;
        if (Has(property, "StatusAttribute")) result |= 256;
        if (Has(property, "TimestampAttribute")) result |= 512;
        if (Has(property, "SizeAttribute")) result |= 1024;
        if (Has(property, "ArtworkAttribute")) result |= 2048;
        if (Has(property, "DisambiguationAttribute")) result |= 4096;

        if (Implements(property.ContainingType, "Arronix.Abstractions.Media.IMediaEntity"))
        {
            if (property.Name == "Key") result |= 1;
            if (property.Name == "Title") result |= 2 | 8;
            if (property.Name == "Artwork") result |= 2048;
        }

        if (Implements(property.ContainingType, "Arronix.Abstractions.Media.IMediaItem"))
        {
            if (property.Name == "Status") result |= 256 | 8 | 16 | 32;
            if (property.Name == "CatalogState" || property.Name == "Collections") result |= 16 | 32;
        }

        if (kind == 13) result |= 1;
        if (kind == 18 || Is(element, "Arronix.Abstractions.Media.ArtworkSet")) result |= 2048;
        return result;
    }

    /// <summary>Reads how important a property is.</summary>
    /// <param name="property">The property.</param>
    /// <returns>The <c>Prominence</c> value.</returns>
    internal static int Prominence(IPropertySymbol property)
    {
        var attribute = Attribute(property, "ProminenceAttribute");
        if (attribute?.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int value)
        {
            return value;
        }

        if (property.Name == "Status" || property.Name == "Collections") return 1;
        if (property.Name == "CatalogState") return 3;
        return 2;
    }

    /// <summary>Renders a string as a C# literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The literal.</returns>
    internal static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Renders a string as a C# literal, or the null literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The literal.</returns>
    internal static string LiteralOrNull(string? value) => value is null ? "null" : Literal(value);

    /// <summary>Renders a boolean as a C# literal.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The literal.</returns>
    internal static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Renders an integer invariantly.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rendering.</returns>
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reduces a display name to characters a file or member name admits.</summary>
    /// <param name="value">The name.</param>
    /// <returns>The reduced name.</returns>
    internal static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            result.Append(char.IsLetterOrDigit(character) ? character : '_');
        }
        return result.ToString();
    }
}
