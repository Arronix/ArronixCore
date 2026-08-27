using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Client;

/// <summary>
/// The canonical rendering, and hash, of what a client contract entry point actually holds.
/// </summary>
/// <remarks>
/// <para>
/// Taken over the live <see cref="JsonTypeInfo"/> graph and the live <see cref="FieldDescriptor"/> schema,
/// never over a description of them. A hash computed from the same model that produced the value it is
/// checking proves nothing; this exists so a generator's compile-time answer can be checked against what
/// the runtime actually does.
/// </para>
/// <para>
/// Every type is resolved through the contract's own <see cref="JsonSerializerContext"/> and never through
/// <see cref="JsonSerializerOptions"/>. The options can fall back to a reflection-based resolver, which
/// would quietly describe a graph the compiler never generated — and a client that cannot be trimmed is
/// exactly what the generated metadata exists to prevent. A type the context has no metadata for is a
/// failure, not a gap to fill in.
/// </para>
/// </remarks>
public static class ClientContractDigest
{
    /// <summary>Renders the serialization graph one entity is read and written through.</summary>
    /// <param name="context">The contract's own generated serialization metadata.</param>
    /// <param name="root">The entity's own metadata, which must belong to <paramref name="context"/>.</param>
    /// <returns>The canonical rendering.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is not this context's metadata.</exception>
    /// <exception cref="InvalidOperationException">The context has no metadata for a reachable type.</exception>
    public static string RenderSerialization(JsonSerializerContext context, JsonTypeInfo root)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);

        if (!ReferenceEquals(context.GetTypeInfo(root.Type), root))
        {
            throw new ArgumentException(
                $"The metadata for '{root.Type}' is not the metadata this context holds for it.",
                nameof(root));
        }

        var rendering = new StringBuilder();
        RenderOptions(rendering, root.Options);

        var seen = new HashSet<Type> { root.Type };
        var pending = new Queue<JsonTypeInfo>();
        pending.Enqueue(root);

        // Breadth first from the root, in member order. The traversal is what makes the rendering
        // reproducible: a type is described once, at the point it is first reachable.
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            RenderType(rendering, current);

            foreach (var next in Reachable(current))
            {
                if (seen.Add(next))
                {
                    pending.Enqueue(Metadata(context, next));
                }
            }
        }

        return rendering.ToString();
    }

    /// <summary>Hashes the serialization graph one entity is read and written through.</summary>
    /// <param name="context">The contract's own generated serialization metadata.</param>
    /// <param name="root">The entity's own metadata.</param>
    /// <returns>The SHA-256, upper-case hexadecimal.</returns>
    public static string OfSerialization(JsonSerializerContext context, JsonTypeInfo root) =>
        Hash(RenderSerialization(context, root));

    /// <summary>Renders a projection schema.</summary>
    /// <param name="entityType">The entity the schema describes.</param>
    /// <param name="schema">The declared fields, in declaration order.</param>
    /// <returns>The canonical rendering.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static string RenderProjection(Type entityType, IReadOnlyList<FieldDescriptor> schema)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(schema);

        var rendering = new StringBuilder();
        rendering.Append("entity=").Append(Text(Name(entityType))).Append('\n');

        foreach (var field in schema)
        {
            RenderField(rendering, field, 1);
        }

        return rendering.ToString();
    }

    /// <summary>Hashes a projection schema.</summary>
    /// <param name="entityType">The entity the schema describes.</param>
    /// <param name="schema">The declared fields, in declaration order.</param>
    /// <returns>The SHA-256, upper-case hexadecimal.</returns>
    public static string OfProjection(Type entityType, IReadOnlyList<FieldDescriptor> schema) =>
        Hash(RenderProjection(entityType, schema));

    private static JsonTypeInfo Metadata(JsonSerializerContext context, Type type) =>
        context.GetTypeInfo(type)
        ?? throw new InvalidOperationException(
            $"This contract's serialization context holds no metadata for '{type}', so the graph it "
            + "describes is not the graph a compiler generated.");

    private static void RenderOptions(StringBuilder rendering, JsonSerializerOptions options) =>
        rendering.Append("options")
            .Append("|caseInsensitive=").Append(Flag(options.PropertyNameCaseInsensitive))
            .Append("|unmapped=").Append(options.UnmappedMemberHandling)
            .Append("|duplicates=").Append(Flag(options.AllowDuplicateProperties))
            .Append("|respectNullable=").Append(Flag(options.RespectNullableAnnotations))
            .Append("|respectRequiredCtorParameters=").Append(Flag(options.RespectRequiredConstructorParameters))
            .Append("|numbers=").Append(options.NumberHandling)
            .Append("|comments=").Append(options.ReadCommentHandling)
            .Append("|trailingCommas=").Append(Flag(options.AllowTrailingCommas))
            .Append("|ignoreCondition=").Append(options.DefaultIgnoreCondition)
            .Append("|includeFields=").Append(Flag(options.IncludeFields))
            .Append('\n');

    private static void RenderType(StringBuilder rendering, JsonTypeInfo type)
    {
        rendering.Append("type=").Append(Text(Name(type.Type))).Append("|kind=").Append(type.Kind);

        if (type.ElementType is { } element)
        {
            rendering.Append("|element=").Append(Text(Name(element)));
        }

        // An enumeration's wire form is a number in its underlying type, so widening one changes what a
        // payload carries even though nothing about the member moved.
        if (type.Type.IsEnum)
        {
            rendering.Append("|underlying=").Append(Text(Name(type.Type.GetEnumUnderlyingType())));
        }

        rendering.Append('\n');

        // Member order is the order a reader positions members in and a writer emits them, so it is
        // rendered as it is rather than sorted.
        foreach (var property in type.Properties)
        {
            rendering.Append("  member=").Append(Text(property.Name));

            // An ignored member contributes nothing to the wire, and what the framework leaves in its
            // place varies with whether its type is reachable elsewhere. That it is ignored is the fact;
            // the placeholder is an implementation detail.
            if (property.Get is null && property.Set is null)
            {
                rendering.Append("|ignored\n");
                continue;
            }

            rendering.Append('|').Append(Text(Name(property.PropertyType)))
                .Append("|read=").Append(Flag(property.Set is not null))
                .Append("|write=").Append(Flag(property.Get is not null))
                .Append("|required=").Append(Flag(property.IsRequired))
                .Append("|getNullable=").Append(Flag(property.IsGetNullable))
                .Append("|setNullable=").Append(Flag(property.IsSetNullable))
                .Append('\n');
        }
    }

    private static IEnumerable<Type> Reachable(JsonTypeInfo type)
    {
        foreach (var property in type.Properties)
        {
            if (property.Get is not null || property.Set is not null)
            {
                yield return property.PropertyType;
            }
        }

        if (type.ElementType is { } element)
        {
            yield return element;
        }
    }

    /// <summary>Renders a type the way both a running program and a compiler can spell it.</summary>
    /// <remarks>
    /// Assembly qualification is left out deliberately. <see cref="Type.FullName"/> writes the version and
    /// public key token of every generic argument's assembly, which would move this digest on a framework
    /// patch that changed nothing about what a payload means.
    /// </remarks>
    private static string Name(Type type)
    {
        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[]";
        }

        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        var definition = (type.GetGenericTypeDefinition().FullName ?? type.Name).Replace('+', '.');
        var arity = definition.IndexOf('`', StringComparison.Ordinal);

        if (arity >= 0)
        {
            definition = definition[..arity];
        }

        var arguments = type.GetGenericArguments();
        var rendering = new StringBuilder(definition).Append('<');

        for (var index = 0; index < arguments.Length; index++)
        {
            if (index > 0)
            {
                rendering.Append(',');
            }

            rendering.Append(Name(arguments[index]));
        }

        return rendering.Append('>').ToString();
    }

    private static void RenderField(StringBuilder rendering, FieldDescriptor field, int depth)
    {
        rendering.Append(' ', depth * 2)
            .Append("field=").Append(Text(field.FieldId))
            .Append('|').Append(Text(field.Name))
            .Append('|').Append(Text(field.Description))
            .Append('|').Append(Number((int)field.ValueKind))
            .Append('|').Append(Number((int)field.Semantics))
            .Append('|').Append(Number((int)field.Prominence))
            .Append('|').Append(field.Multivalued ? "many" : "one")
            .Append('|').Append(field.Editable ? "editable" : "read-only")
            .Append('|').Append(Text(field.Unit))
            .Append('\n');

        foreach (var choice in field.Choices)
        {
            rendering.Append(' ', (depth + 1) * 2)
                .Append("choice=").Append(Text(choice.Value)).Append('|').Append(Text(choice.Name)).Append('\n');
        }

        foreach (var component in field.Components)
        {
            RenderField(rendering, component, depth + 1);
        }
    }

    /// <summary>Encodes free text so that no value can be mistaken for the structure around it.</summary>
    /// <remarks>
    /// A field's name, description or choice text is author-supplied and may contain the separator or a
    /// line break. Concatenated raw, two different schemas render identically and hash alike — a rename
    /// that also moved a separator would be invisible. Length-prefixing removes the ambiguity: the reader
    /// of a rendering never has to find where a value ends. A null is its own mark, distinct from empty.
    /// </remarks>
    private static string Text(string? value) =>
        value is null ? "~" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Hash(string rendering) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rendering)));
}
