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
/// Every generator that describes an entity reads it from here. Two walks over the same properties are two
/// chances for a field to exist on one side and not the other, which shows up as a browser silently
/// missing a field rather than as an error.
/// </para>
/// <para>
/// What a shape means is read through <see cref="PlatformSymbols"/>, so a reading belongs to the
/// compilation whose contract assembly it resolved; the members that read syntax alone are static.
/// Filtering is the caller's: <see cref="PublicProperties"/> answers what a type declares, and whether an
/// <c>[Ignore]</c>d property belongs in a particular output is the output's question.
/// </para>
/// </remarks>
internal sealed class MediaShapeModel
{
    private readonly PlatformSymbols _platform;

    private MediaShapeModel(PlatformSymbols platform) => _platform = platform;

    /// <summary>Reads a compilation's platform types, or nothing when it has no Arronix contract.</summary>
    internal static MediaShapeModel? Create(Compilation compilation) =>
        PlatformSymbols.Resolve(compilation) is { } platform ? new MediaShapeModel(platform) : null;

    /// <inheritdoc cref="PlatformSymbols.Is" />
    internal bool Is(ITypeSymbol? type, PlatformSymbol symbol) => _platform.Is(type, symbol);

    /// <inheritdoc cref="PlatformSymbols.Implements" />
    internal bool Implements(ITypeSymbol type, PlatformSymbol symbol) => _platform.Implements(type, symbol);

    /// <inheritdoc cref="PlatformSymbols.Attribute" />
    internal AttributeData? Attribute(ISymbol member, PlatformSymbol symbol) => _platform.Attribute(member, symbol);

    /// <inheritdoc cref="PlatformSymbols.Has" />
    internal bool Has(ISymbol member, PlatformSymbol symbol) => _platform.Has(member, symbol);

    /// <inheritdoc cref="PlatformSymbols.ClosedBase" />
    internal INamedTypeSymbol? ClosedBase(INamedTypeSymbol type, PlatformSymbol symbol) =>
        _platform.ClosedBase(type, symbol);

    /// <summary>Determines whether a property carries a value a generated output must not describe.</summary>
    internal bool IsExcluded(IPropertySymbol property) => Has(property, PlatformSymbol.Ignore);

    /// <summary>Classifies the shape a property's values take, as a <c>FieldValueKind</c>.</summary>
    internal int ValueKind(ITypeSymbol type, IPropertySymbol property)
    {
        if (Has(property, PlatformSymbol.Size)) return 8;
        if (Has(property, PlatformSymbol.Count)) return 19;
        if (Has(property, PlatformSymbol.Ratio)) return 9;
        if (type.SpecialType == SpecialType.System_String) return Has(property, PlatformSymbol.Multiline) ? 1 : 0;
        if (type.TypeKind == TypeKind.Enum) return 11;
        if (Is(type, PlatformSymbol.ArtworkSet) || Is(type, PlatformSymbol.ArtworkImage)) return 18;
        if (Is(type, PlatformSymbol.ExternalIdSet) || Is(type, PlatformSymbol.ExternalId)) return 13;
        if (Is(type, PlatformSymbol.MediaItemId)) return 2;
        if (type.SpecialType is SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64) return 2;
        if (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal) return 3;
        if (type.SpecialType == SpecialType.System_Boolean) return 4;
        if (Is(type, PlatformSymbol.DateOnly)) return 5;
        if (type.SpecialType == SpecialType.System_DateTime || Is(type, PlatformSymbol.DateTimeOffset)) return 6;
        if (Is(type, PlatformSymbol.TimeSpan)) return 7;
        if (Is(type, PlatformSymbol.Uri)) return 14;
        if (Is(type, PlatformSymbol.PlatformPath)) return 15;
        if (Is(type, PlatformSymbol.Language)) return 16;
        if (Is(type, PlatformSymbol.QualityTier)) return 17;
        if (Is(type, PlatformSymbol.OrdinalPath)) return 10;
        if (Implements(type, PlatformSymbol.MediaEntity)) return 12;
        return 20;
    }

    /// <summary>Reads what a property means beyond its shape, as <c>FieldSemantics</c> flags.</summary>
    internal int Semantics(IPropertySymbol property, int kind, ITypeSymbol element)
    {
        var result = 0;
        if (Has(property, PlatformSymbol.Identity)) result |= 1;
        if (Has(property, PlatformSymbol.Title)) result |= 2;
        if (Has(property, PlatformSymbol.Sortable)) result |= 8;
        if (Has(property, PlatformSymbol.Filterable)) result |= 16;
        if (Has(property, PlatformSymbol.Groupable)) result |= 32;
        if (Has(property, PlatformSymbol.Searchable)) result |= 64;
        if (Has(property, PlatformSymbol.Progress)) result |= 128;
        if (Has(property, PlatformSymbol.Status)) result |= 256;
        if (Has(property, PlatformSymbol.Timestamp)) result |= 512;
        if (Has(property, PlatformSymbol.Size)) result |= 1024;
        if (Has(property, PlatformSymbol.Artwork)) result |= 2048;
        if (Has(property, PlatformSymbol.Disambiguation)) result |= 4096;

        // These read the property's own name, and deliberately: the members named are the common shape's,
        // reached only through a type that is the platform's own entity contract.
        if (Implements(property.ContainingType, PlatformSymbol.MediaEntity))
        {
            if (property.Name == "Key") result |= 1;
            if (property.Name == "Title") result |= 2 | 8;
            if (property.Name == "Artwork") result |= 2048;
        }

        if (Implements(property.ContainingType, PlatformSymbol.MediaEntityItem))
        {
            if (property.Name == "Status") result |= 256 | 8 | 16 | 32;
            if (property.Name == "CatalogState" || property.Name == "Collections") result |= 16 | 32;
        }

        if (kind == 13) result |= 1;
        if (kind == 18 || Is(element, PlatformSymbol.ArtworkSet)) result |= 2048;
        return result;
    }

    /// <summary>Reads how important a property is, as a <c>Prominence</c> value.</summary>
    internal int Prominence(IPropertySymbol property)
    {
        var attribute = Attribute(property, PlatformSymbol.Prominence);
        if (attribute?.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is int value)
        {
            return value;
        }

        if (property.Name == "Status" || property.Name == "Collections") return 1;
        if (property.Name == "CatalogState") return 3;
        return 2;
    }

    /// <summary>Reads a type's public instance properties, base class first, overrides collapsed.</summary>
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

    /// <summary>Gets the element type of a read-only sequence, or <see langword="null"/>.</summary>
    /// <remarks>The sequences the language itself names, so no lookalike can answer for one.</remarks>
    internal static ITypeSymbol? UnwrapList(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named
        && named.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IReadOnlyList_T
            or SpecialType.System_Collections_Generic_IReadOnlyCollection_T
            or SpecialType.System_Collections_Generic_IEnumerable_T
            ? named.TypeArguments[0]
            : null;

    /// <summary>Removes a nullable value type's wrapper.</summary>
    internal static ITypeSymbol StripNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    /// <summary>Determines whether a declaration admits absence.</summary>
    internal static bool IsNullable(ITypeSymbol type) =>
        type.IsReferenceType || type.NullableAnnotation == NullableAnnotation.Annotated
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>Reads one named string argument of an attribute.</summary>
    internal static string? NamedString(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    /// <summary>Reads the first positional string argument of an attribute.</summary>
    internal static string? ConstructorString(AttributeData? attribute) =>
        attribute?.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;

    /// <summary>Renders a type, globally qualified, the way generated code must spell it.</summary>
    internal static string TypeName(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.None)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>Renders a member name as the identifier consumers reference it by.</summary>
    internal static string Identifier(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    /// <summary>Renders a member name as a display label.</summary>
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

    /// <summary>Renders a string as a C# literal.</summary>
    internal static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Renders a string as a C# literal, or the null literal.</summary>
    internal static string LiteralOrNull(string? value) => value is null ? "null" : Literal(value);

    /// <summary>Renders a boolean as a C# literal.</summary>
    internal static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Renders an integer invariantly.</summary>
    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reduces a display name to characters a file or member name admits.</summary>
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
