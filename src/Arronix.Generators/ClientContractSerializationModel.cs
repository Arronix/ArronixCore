using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static Arronix.Generators.MediaShapeModel;

namespace Arronix.Generators;

/// <summary>
/// The compile-time model of what the framework's serializer will do with one item graph.
/// </summary>
/// <remarks>
/// Rendered in the form <c>ClientContractDigest</c> renders live metadata in, so the emitted hash can be
/// checked against an independent recomputation. Shapes the model cannot reproduce are refused.
/// </remarks>
internal static class ClientContractSerializationModel
{
    /// <summary>Every option value <c>JsonSerializerDefaults.Strict</c> selects, in rendering order.</summary>
    /// <remarks>A table, not a derivation: a framework that changed Strict fails the digest check by design.</remarks>
    private const string StrictOptions =
        "options|caseInsensitive=false|unmapped=Disallow|duplicates=false|respectNullable=true"
        + "|respectRequiredCtorParameters=true|numbers=Strict|comments=Disallow|trailingCommas=false"
        + "|ignoreCondition=Never|includeFields=false";

    /// <summary>Renders the serialization graph one entity will be read and written through.</summary>
    /// <param name="root">The entity type.</param>
    /// <param name="framework">The framework serialization types, resolved from the compilation.</param>
    /// <param name="shape">The CLR shape reading, which resolved the platform and framework types.</param>
    /// <param name="derived">The wire names of members the contract computes rather than reads.</param>
    /// <param name="refusal">Why the graph could not be modeled, when it could not.</param>
    /// <returns>The canonical rendering, or <see langword="null"/> when it was refused.</returns>
    internal static string? Render(
        INamedTypeSymbol root,
        FrameworkSymbols framework,
        MediaShapeModel shape,
        out IReadOnlyList<string> derived,
        out string? refusal)
    {
        refusal = null;
        var ignored = new SortedSet<string>(StringComparer.Ordinal);
        var live = new HashSet<string>(StringComparer.Ordinal);
        derived = Array.Empty<string>();

        var rendering = new StringBuilder(StrictOptions).Append('\n');
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { root };
        var pending = new Queue<Reached>();
        pending.Enqueue(new Reached(root, "the entity itself"));

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            // Every type the graph reaches is validated where it is described, not only where a member
            // declares it. A collection's element, a nullable's underlying value and the root itself are
            // all reached without any member naming them directly.
            if (!Modelable(current.Type, current.Through, framework, shape, ref refusal))
            {
                return null;
            }

            var reachable = RenderType(rendering, current.Type, framework, shape, ignored, live, ref refusal);

            if (refusal is not null)
            {
                return null;
            }

            foreach (var next in reachable)
            {
                if (seen.Add(next.Type))
                {
                    pending.Enqueue(next);
                }
            }
        }

        // A derived member's name is refused wherever it appears, so it must not also be a member some
        // other type in this graph legitimately carries. Sharing one would make a valid payload unreadable.
        // The intersection is taken before anything is removed from either set: subtracting first empties
        // the very set the collision would have been found in.
        var collisions = new SortedSet<string>(ignored, StringComparer.Ordinal);
        collisions.IntersectWith(live);

        if (collisions.Count > 0)
        {
            refusal = "'" + string.Join("', '", collisions)
                + "' is both a computed member and a member some other type in the graph carries";
            return null;
        }

        derived = ignored.ToArray();
        return rendering.ToString();
    }

    private static IReadOnlyList<Reached> RenderType(
        StringBuilder rendering,
        ITypeSymbol type,
        FrameworkSymbols framework,
        MediaShapeModel shape,
        ISet<string> ignoredNames,
        ISet<string> liveNames,
        ref string? refusal)
    {
        var reachable = new List<Reached>();
        var element = ElementOf(type, shape);
        var kind = Kind(type, element, shape);

        rendering.Append("type=").Append(Text(Name(type))).Append("|kind=").Append(kind);

        if (element is not null)
        {
            rendering.Append("|element=").Append(Text(Name(element)));
        }

        // An enumeration's wire form is a number in its underlying type, so widening one is a change to
        // what a payload carries even though nothing about the member moved.
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
        {
            rendering.Append("|underlying=").Append(Text(Name(underlying)));
        }

        rendering.Append('\n');

        if (kind == "Object" && type is INamedTypeSymbol named)
        {
            var constructor = Constructor(named, framework, ref refusal);

            if (refusal is not null)
            {
                return reachable;
            }

            foreach (var property in DeclaredFirst(named))
            {
                if (!Modelable(property, framework, shape, ref refusal))
                {
                    return reachable;
                }

                rendering.Append("  member=").Append(Text(CamelCase(property.Name)));

                var ignore = property.GetAttributes().FirstOrDefault(
                    attribute => FrameworkSymbols.Is(attribute, framework.Ignore));

                if (ignore is not null)
                {
                    if (ignore.NamedArguments.Any(argument => argument.Key == "Condition"))
                    {
                        refusal = $"'{property.Name}' carries a conditional [JsonIgnore], which is not modeled";
                        return reachable;
                    }

                    ignoredNames.Add(CamelCase(property.Name));
                    rendering.Append("|ignored\n");
                    continue;
                }

                liveNames.Add(CamelCase(property.Name));

                var parameter = constructor?.Parameters.FirstOrDefault(
                    candidate => string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                var nullable = Nullable(property);

                rendering.Append('|').Append(Text(Name(property.Type)))
                    .Append("|read=").Append(Bool(property.SetMethod is { DeclaredAccessibility: Accessibility.Public }))
                    .Append("|write=").Append(Bool(property.GetMethod is { DeclaredAccessibility: Accessibility.Public }))
                    .Append("|required=").Append(Bool(property.IsRequired || parameter is { HasExplicitDefaultValue: false }))
                    .Append("|getNullable=").Append(Bool(nullable))
                    .Append("|setNullable=").Append(Bool(nullable))
                    .Append('\n');

                reachable.Add(new Reached(property.Type, $"'{property.Name}' on '{type.ToDisplayString()}'"));
            }
        }

        if (element is not null)
        {
            reachable.Add(new Reached(element, $"the elements of '{type.ToDisplayString()}'"));
        }

        return reachable;
    }

    /// <summary>One type in the graph, and how it was first reached.</summary>
    private sealed class Reached
    {
        internal Reached(ITypeSymbol type, string through)
        {
            Type = type;
            Through = through;
        }

        internal ITypeSymbol Type { get; }

        internal string Through { get; }
    }

    /// <remarks>
    /// The framework's order: a named constructor wins, then a parameterless one, then the single
    /// parameterized one.
    /// </remarks>
    private static IMethodSymbol? Constructor(INamedTypeSymbol type, FrameworkSymbols framework, ref string? refusal)
    {
        // The framework honours a named constructor whatever its accessibility, so this model reads it the
        // same way rather than looking only at the public ones and silently choosing something else.
        var named = type.InstanceConstructors
            .Where(candidate => candidate.GetAttributes()
                .Any(attribute => FrameworkSymbols.Is(attribute, framework.Constructor)))
            .ToArray();

        if (named.Length > 1)
        {
            refusal = $"'{type.ToDisplayString()}' names more than one constructor for a deserializer";
            return null;
        }

        if (named.Length == 1)
        {
            return named[0];
        }

        var accessible = type.InstanceConstructors
            .Where(candidate => candidate.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        if (accessible.Any(candidate => candidate.Parameters.Length == 0))
        {
            return null;
        }

        var parameterized = accessible.Where(candidate => candidate.Parameters.Length > 0).ToArray();

        if (parameterized.Length == 1)
        {
            return parameterized[0];
        }

        refusal = $"'{type.ToDisplayString()}' has {parameterized.Length} public constructors and names none "
            + "of them, so which one rebuilds it is not stated";

        return null;
    }

    private static ITypeSymbol? ElementOf(ITypeSymbol type, MediaShapeModel shape)
    {
        if (type is IArrayTypeSymbol array)
        {
            // An array of bytes is a base64 string, not a sequence of numbers.
            return array.ElementType.SpecialType == SpecialType.System_Byte ? null : array.ElementType;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return null;
        }

        return named.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T
            or SpecialType.System_Collections_Generic_IReadOnlyList_T
            or SpecialType.System_Collections_Generic_IReadOnlyCollection_T
            or SpecialType.System_Collections_Generic_IEnumerable_T
            or SpecialType.System_Collections_Generic_IList_T
            or SpecialType.System_Collections_Generic_ICollection_T
            || shape.Is(named, PlatformSymbol.List)
            ? named.TypeArguments[0]
            : null;
    }

    private static string Kind(ITypeSymbol type, ITypeSymbol? element, MediaShapeModel shape) =>
        IsScalar(type, shape) ? "None" : element is not null ? "Enumerable" : "Object";

    /// <remarks>
    /// From the declaration, not the substitution: a member declared as a type parameter has the nullability
    /// its constraints give it, and nothing ruling out null is oblivious, which reads as nullable.
    /// </remarks>
    private static bool Nullable(IPropertySymbol property)
    {
        if (property.OriginalDefinition.Type is ITypeParameterSymbol parameter)
        {
            // A constraint that rules out null is an annotation; a shape constraint is not. Only the
            // second leaves the member oblivious, and oblivious is read as nullable.
            if (parameter.HasNotNullConstraint || parameter.HasValueTypeConstraint)
            {
                return false;
            }

            return !parameter.HasReferenceTypeConstraint
                || parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated;
        }

        var type = property.Type;

        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
            || (type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated);
    }

    /// <summary>Reads a type's public instance properties the way the framework orders them.</summary>
    /// <remarks>
    /// Most-derived first, then each base in turn. Measured, and deliberately not the base-first order
    /// Host's compiled shapes use.
    /// </remarks>
    private static IReadOnlyList<IPropertySymbol> DeclaredFirst(INamedTypeSymbol type)
    {
        var result = new List<IPropertySymbol>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.Parameters.Length != 0
                    || property.DeclaredAccessibility != Accessibility.Public
                    || property.GetMethod?.DeclaredAccessibility != Accessibility.Public
                    || !taken.Add(property.Name))
                {
                    continue;
                }

                result.Add(property);
            }
        }

        return result;
    }

    /// <summary>
    /// Determines whether one member's declaration is inside what this model describes.
    /// </summary>
    /// <remarks>Each refusal is a framework feature that changes a payload's meaning and is not modeled.</remarks>
    private static bool Modelable(
        IPropertySymbol property,
        FrameworkSymbols framework,
        MediaShapeModel shape,
        ref string? refusal)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (framework.IsSerializationAttribute(attribute) && !FrameworkSymbols.Is(attribute, framework.Ignore))
            {
                refusal = $"'{property.Name}' carries [{Shorten(attribute.AttributeClass!.Name)}], which "
                    + "this model does not describe";
                return false;
            }
        }

        return Modelable(property.Type, property.Name, framework, shape, ref refusal);
    }

    private static bool Modelable(
        ITypeSymbol type,
        string member,
        FrameworkSymbols framework,
        MediaShapeModel shape,
        ref string? refusal)
    {
        // An allow list, not a deny list. A framework attribute this model has never heard of changes what
        // a payload means in some way, and the safe reading of "never heard of" is "not described".
        foreach (var attribute in type.GetAttributes())
        {
            if (framework.IsSerializationAttribute(attribute)
                && !FrameworkSymbols.Is(attribute, framework.Serializable)
                && !FrameworkSymbols.Is(attribute, framework.GenerationOptions))
            {
                refusal = $"'{type.ToDisplayString()}', reached through '{member}', carries "
                    + $"[{Shorten(attribute.AttributeClass!.Name)}], which this model does not describe";
                return false;
            }
        }

        // Before the shape rules below: a dictionary is also an interface, and naming it a dictionary sends
        // an author somewhere useful.
        if (IsDictionary(type, shape))
        {
            refusal = $"'{type.ToDisplayString()}', reached through '{member}', is a dictionary, and "
                + "dictionary key handling is not modeled";
            return false;
        }

        if (!Describable(type, shape, ref refusal))
        {
            return false;
        }

        // A collection the model does not recognize would be described as an object with the collection's
        // own members, which is not what the framework writes.
        if (ElementOf(type, shape) is null && !IsScalar(type, shape) && IsSequence(type))
        {
            refusal = $"'{type.ToDisplayString()}', reached through '{member}', is a collection this model "
                + "does not recognize, so the shape it would be given is not the shape it is written in";
            return false;
        }

        return HidesNoSerializedMember(type, member, framework, ref refusal);
    }

    /// <summary>
    /// Determines whether a type puts anything on the wire through a member this model does not read.
    /// </summary>
    /// <remarks>
    /// Measured: a public field or an internal property carrying <c>[JsonInclude]</c> is serialized even
    /// with <c>IncludeFields</c> off, so either would reach the wire without entering the digest.
    /// </remarks>
    private static bool HidesNoSerializedMember(
        ITypeSymbol type,
        string member,
        FrameworkSymbols framework,
        ref string? refusal)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var declared in current.GetMembers())
            {
                if (declared.IsStatic || declared.IsImplicitlyDeclared || !Marked(declared, framework))
                {
                    continue;
                }

                if (declared is IFieldSymbol)
                {
                    refusal = $"'{current.ToDisplayString()}.{declared.Name}', reached through '{member}', is "
                        + "a field the framework serializes, and this model describes properties";
                    return false;
                }

                if (declared is IPropertySymbol property
                    && (property.DeclaredAccessibility != Accessibility.Public
                        || property.GetMethod?.DeclaredAccessibility != Accessibility.Public))
                {
                    refusal = $"'{current.ToDisplayString()}.{declared.Name}', reached through '{member}', is "
                        + "serialized without being publicly readable, so this model would not describe it";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Marked(ISymbol member, FrameworkSymbols framework)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (framework.IsSerializationAttribute(attribute))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a shape is one this model has a described wire form for.
    /// </summary>
    /// <remarks>Shapes whose wire form the declaration does not state, so the model would have to guess.</remarks>
    private static bool Describable(ITypeSymbol type, MediaShapeModel shape, ref string? refusal)
    {
        if (type.SpecialType == SpecialType.System_Object)
        {
            refusal = "an untyped value is on the wire, and what a payload may carry for it is not stated";
            return false;
        }

        // The framework writes one array shape: a single-dimensional, zero-based array. A rank-one array
        // with a non-zero lower bound is not that one, and neither is a multidimensional array.
        if (type is IArrayTypeSymbol { IsSZArray: false } array)
        {
            refusal = $"'{type.ToDisplayString()}' is "
                + (array.Rank > 1 ? "a multidimensional array" : "an array that is not zero-based")
                + ", which has no wire form";
            return false;
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.ContainingType is { } containing && (named.IsGenericType || containing.IsGenericType))
            {
                refusal = $"'{type.ToDisplayString()}' is a generic nested inside another type, whose type "
                    + "arguments a compiler and a runtime spell differently";
                return false;
            }

            if ((named.TypeKind == TypeKind.Interface || named.IsAbstract)
                && !IsScalar(named, shape) && ElementOf(named, shape) is null)
            {
                refusal = $"'{type.ToDisplayString()}' is "
                    + (named.TypeKind == TypeKind.Interface ? "an interface" : "abstract")
                    + ", so what a payload carries for it is decided by something this declaration does not say";
                return false;
            }
        }

        return true;
    }

    /// <remarks>The shapes the framework writes as one value, so the model walks no members of them.</remarks>
    private static bool IsScalar(ITypeSymbol type, MediaShapeModel shape) =>
        type.TypeKind == TypeKind.Enum
        || type.SpecialType is SpecialType.System_String or SpecialType.System_Boolean
            or SpecialType.System_Char or SpecialType.System_Byte or SpecialType.System_SByte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
            or SpecialType.System_DateTime
        || shape.Is(type, PlatformSymbol.DateOnly)
        || shape.Is(type, PlatformSymbol.TimeOnly)
        || shape.Is(type, PlatformSymbol.DateTimeOffset)
        || shape.Is(type, PlatformSymbol.TimeSpan)
        || shape.Is(type, PlatformSymbol.Guid)
        || shape.Is(type, PlatformSymbol.Uri)
        || shape.Is(type, PlatformSymbol.Version)
        || type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte }
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static bool IsSequence(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type.SpecialType == SpecialType.System_Collections_IEnumerable)
        {
            return true;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (contract.SpecialType == SpecialType.System_Collections_IEnumerable)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDictionary(ITypeSymbol type, MediaShapeModel shape)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (IsDictionaryDefinition(named, shape))
        {
            return true;
        }

        foreach (var contract in named.AllInterfaces)
        {
            if (IsDictionaryDefinition(contract, shape))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Encodes free text so that no value can be mistaken for the structure around it.</summary>
    internal static string Text(string? value) =>
        value is null ? "~" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;

    private static string Shorten(string attributeName) =>
        attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
            : attributeName;

    private static bool IsDictionaryDefinition(INamedTypeSymbol type, MediaShapeModel shape) =>
        shape.Is(type, PlatformSymbol.Dictionary) || shape.Is(type, PlatformSymbol.ReadOnlyDictionary);

    /// <summary>Applies the framework's camel-case policy to a member name.</summary>
    /// <remarks>A leading run of capitals is lowered as a run, so an acronym stays one word.</remarks>
    internal static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var characters = name.ToCharArray();

        for (var index = 0; index < characters.Length; index++)
        {
            if (!char.IsUpper(characters[index]))
            {
                break;
            }

            if (index > 0 && index + 1 < characters.Length && !char.IsUpper(characters[index + 1]))
            {
                break;
            }

            characters[index] = char.ToLowerInvariant(characters[index]);
        }

        return new string(characters);
    }

    /// <summary>Renders a type the way <c>ClientContractDigest</c> renders one at run time.</summary>
    internal static string Name(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return Name(array.ElementType) + "[]";
        }

        if (type is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return Qualified(type);
        }

        var rendering = new StringBuilder(Qualified(named.OriginalDefinition)).Append('<');

        for (var index = 0; index < named.TypeArguments.Length; index++)
        {
            if (index > 0)
            {
                rendering.Append(',');
            }

            rendering.Append(Name(named.TypeArguments[index]));
        }

        return rendering.Append('>').ToString();
    }

    private static readonly SymbolDisplayFormat Unqualified = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);

    private static string Qualified(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(Unqualified);
}
