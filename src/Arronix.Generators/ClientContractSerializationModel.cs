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
/// Rendered in exactly the form <c>ClientContractDigest</c> renders the live metadata in, so the literal
/// hash this generator emits can be checked against an independent recomputation from the running
/// <c>JsonTypeInfo</c> graph. A model that produced its own rendering and hashed that would prove nothing.
/// </remarks>
internal static class ClientContractSerializationModel
{
    /// <summary>Every option value <c>JsonSerializerDefaults.Strict</c> selects, in rendering order.</summary>
    /// <remarks>
    /// A table rather than a derivation, because the meaning of Strict belongs to the framework. If a
    /// future framework changes it, the digest check fails and names the line, which is the correct
    /// outcome: what a payload means would have changed.
    /// </remarks>
    private const string StrictOptions =
        "options|caseInsensitive=false|unmapped=Disallow|duplicates=false|respectNullable=true"
        + "|respectRequiredCtorParameters=true|numbers=Strict|comments=Disallow|trailingCommas=false"
        + "|ignoreCondition=Never|includeFields=false";

    private static readonly string[] Sequences =
    [
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.List<T>",
    ];

    private static readonly string[] Scalars =
    [
        "System.String", "System.Boolean", "System.Char", "System.Byte", "System.SByte",
        "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
        "System.Single", "System.Double", "System.Decimal", "System.Object",
        "System.DateOnly", "System.TimeOnly", "System.DateTime", "System.DateTimeOffset",
        "System.TimeSpan", "System.Guid", "System.Uri", "System.Version",
    ];

    /// <summary>Renders the serialization graph one entity will be read and written through.</summary>
    /// <param name="root">The entity type.</param>
    /// <param name="derived">The wire names of members the contract computes rather than reads.</param>
    /// <param name="refusal">Why the graph could not be modeled, when it could not.</param>
    /// <returns>The canonical rendering, or <see langword="null"/> when it was refused.</returns>
    internal static string? Render(
        INamedTypeSymbol root,
        out IReadOnlyList<string> derived,
        out string? refusal)
    {
        refusal = null;
        var ignored = new SortedSet<string>(StringComparer.Ordinal);
        var live = new HashSet<string>(StringComparer.Ordinal);
        derived = Array.Empty<string>();

        var rendering = new StringBuilder(StrictOptions).Append('\n');
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { root };
        var pending = new Queue<ITypeSymbol>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            var reachable = RenderType(rendering, current, ignored, live, ref refusal);

            if (refusal is not null)
            {
                return null;
            }

            foreach (var next in reachable)
            {
                if (seen.Add(next))
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

    private static IReadOnlyList<ITypeSymbol> RenderType(
        StringBuilder rendering,
        ITypeSymbol type,
        ISet<string> ignoredNames,
        ISet<string> liveNames,
        ref string? refusal)
    {
        var element = ElementOf(type);
        var kind = Kind(type, element);

        rendering.Append("type=").Append(Text(Name(type))).Append("|kind=").Append(kind);

        if (element is not null)
        {
            rendering.Append("|element=").Append(Text(Name(element)));
        }

        rendering.Append('\n');

        var reachable = new List<ITypeSymbol>();

        if (kind == "Object" && type is INamedTypeSymbol named)
        {
            var constructor = Constructor(named, ref refusal);

            if (refusal is not null)
            {
                return reachable;
            }

            foreach (var property in DeclaredFirst(named))
            {
                if (!Modelable(property, ref refusal))
                {
                    return reachable;
                }

                rendering.Append("  member=").Append(Text(CamelCase(property.Name)));

                var ignore = property.GetAttributes().FirstOrDefault(
                    attribute => attribute.AttributeClass?.Name == "JsonIgnoreAttribute");

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

                reachable.Add(property.Type);
            }
        }

        if (element is not null)
        {
            reachable.Add(element);
        }

        return reachable;
    }

    /// <remarks>
    /// The framework's own preference order: a named constructor wins; otherwise a parameterless one, which
    /// leaves every member to its own setter; otherwise the single parameterized one.
    /// </remarks>
    private static IMethodSymbol? Constructor(INamedTypeSymbol type, ref string? refusal)
    {
        var accessible = type.InstanceConstructors
            .Where(candidate => candidate.DeclaredAccessibility == Accessibility.Public)
            .ToArray();

        var named = accessible.FirstOrDefault(candidate => candidate.GetAttributes()
            .Any(attribute => attribute.AttributeClass?.Name == "JsonConstructorAttribute"));

        if (named is not null)
        {
            return named;
        }

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

    private static ITypeSymbol? ElementOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            // An array of bytes is a base64 string, not a sequence of numbers.
            return array.ElementType.SpecialType == SpecialType.System_Byte ? null : array.ElementType;
        }

        if (type is not INamedTypeSymbol named || !named.IsGenericType)
        {
            return null;
        }

        var definition = named.OriginalDefinition.ToDisplayString();

        if (definition == "System.Nullable<T>")
        {
            return named.TypeArguments[0];
        }

        return Array.IndexOf(Sequences, definition) >= 0 ? named.TypeArguments[0] : null;
    }

    private static string Kind(ITypeSymbol type, ITypeSymbol? element)
    {
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            return "None";
        }

        if (type.TypeKind == TypeKind.Enum
            || Array.IndexOf(Scalars, Name(type)) >= 0
            || (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }))
        {
            return "None";
        }

        return element is not null ? "Enumerable" : "Object";
    }

    /// <remarks>
    /// Read from the declaration rather than from the substitution. A member declared as a type parameter
    /// carries the nullability its constraints give it, not the closed type's: with nothing that rules out
    /// null it is oblivious, and the framework reads oblivious as nullable.
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
    /// Most-derived first, then each base in turn, each level in its own declaration order. Measured rather
    /// than assumed, and deliberately not the order Host's compiled shapes use: that one reads base first,
    /// because it is describing an entity to a reader, and this one has to be the order a payload's members
    /// are actually positioned in.
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
                    || property.Name == "EqualityContract"
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
    /// <remarks>
    /// Each refusal is a framework feature that changes what a payload means and that this model does not
    /// yet reproduce. Describing one wrongly would produce a hash that disagrees with the wire while
    /// looking like agreement, which is worse than refusing to publish the contract at all.
    /// </remarks>
    private static bool Modelable(IPropertySymbol property, ref string? refusal)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (IsSerializationAttribute(attribute) && attribute.AttributeClass?.Name != "JsonIgnoreAttribute")
            {
                refusal = $"'{property.Name}' carries [{Shorten(attribute.AttributeClass!.Name)}], which "
                    + "this model does not describe";
                return false;
            }
        }

        return Modelable(property.Type, property.Name, ref refusal);
    }

    private static bool Modelable(ITypeSymbol type, string member, ref string? refusal)
    {
        // An allow list, not a deny list. A framework attribute this model has never heard of changes what
        // a payload means in some way, and the safe reading of "never heard of" is "not described".
        foreach (var attribute in type.GetAttributes())
        {
            if (IsSerializationAttribute(attribute) && attribute.AttributeClass?.Name is not
                ("JsonSerializableAttribute" or "JsonSourceGenerationOptionsAttribute"))
            {
                refusal = $"'{type.ToDisplayString()}', reached through '{member}', carries "
                    + $"[{Shorten(attribute.AttributeClass!.Name)}], which this model does not describe";
                return false;
            }
        }

        if (IsDictionary(type))
        {
            refusal = $"'{type.ToDisplayString()}', reached through '{member}', is a dictionary, and "
                + "dictionary key handling is not modeled";
            return false;
        }

        // A collection the model does not recognize would be described as an object with the collection's
        // own members, which is not what the framework writes.
        if (ElementOf(type) is null && !IsScalar(type) && IsSequence(type))
        {
            refusal = $"'{type.ToDisplayString()}', reached through '{member}', is a collection this model "
                + "does not recognize, so the shape it would be given is not the shape it is written in";
            return false;
        }

        return true;
    }

    private static bool IsSerializationAttribute(AttributeData attribute) =>
        attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() == "System.Text.Json.Serialization";

    private static bool IsScalar(ITypeSymbol type) =>
        type.TypeKind == TypeKind.Enum
        || Array.IndexOf(Scalars, Name(type)) >= 0
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

    private static bool IsDictionary(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (IsDictionaryDefinition(named))
        {
            return true;
        }

        foreach (var contract in named.AllInterfaces)
        {
            if (IsDictionaryDefinition(contract))
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

    private static bool IsDictionaryDefinition(INamedTypeSymbol type) =>
        type.IsGenericType
        && type.OriginalDefinition.ToDisplayString() is
            "System.Collections.Generic.IDictionary<TKey, TValue>"
            or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";

    /// <summary>Applies the framework's camel-case policy to a member name.</summary>
    /// <remarks>
    /// A leading run of capitals is lowered as a run, so an acronym stays one word; the first capital that
    /// is followed by a lower-case letter starts the next word and is left alone.
    /// </remarks>
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
