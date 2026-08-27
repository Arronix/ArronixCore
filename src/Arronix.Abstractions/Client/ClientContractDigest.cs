using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Client;

/// <summary>
/// The canonical rendering, and hash, of a live serialization graph and a live projection schema.
/// </summary>
/// <remarks>
/// Taken over the running metadata so a generator's compile-time answer can be checked against it. Types
/// resolve through the contract's own context, never through options, which can fall back to a reflecting
/// resolver. State this rendering does not carry is refused rather than partly hashed.
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
    /// <exception cref="NotSupportedException">The metadata carries state this rendering cannot describe.</exception>
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
        RenderOptions(rendering, context, root.Options);

        var seen = new HashSet<Type> { root.Type };
        var pending = new Queue<JsonTypeInfo>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            RenderType(rendering, context, current);

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

    /// <summary>
    /// Renders the reader and typed-writer settings, having refused the ones this rendering cannot carry.
    /// </summary>
    /// <remarks>
    /// Whitespace and buffering are excluded on purpose: <c>WriteIndented</c>, <c>IndentCharacter</c>,
    /// <c>IndentSize</c>, <c>NewLine</c>, <c>DefaultBufferSize</c> and <c>Encoder</c> change the bytes a
    /// payload is written as, and every conforming reader recovers the same values from them.
    /// </remarks>
    private static void RenderOptions(StringBuilder rendering, JsonSerializerContext context, JsonSerializerOptions options)
    {
        Refuse(options.ReferenceHandler is not null, "these options", "preserve references");
        Refuse(options.Converters.Count != 0, "these options", "carry converters of their own");
        Refuse(options.DictionaryKeyPolicy is not null, "these options", "name a dictionary key policy");
        Refuse(
            !ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.CamelCase),
            "these options",
            "name a property naming policy other than camel case");

        // The contract's own context answers for this graph and nothing else answers for any of it.
        Refuse(
            !ReferenceEquals(options.TypeInfoResolver, context)
            || options.TypeInfoResolverChain.Count != 1
            || !ReferenceEquals(options.TypeInfoResolverChain[0], context),
            "these options",
            "resolve metadata through something other than this contract's own context");

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
            .Append("|maxDepth=").Append(Number(options.MaxDepth))
            .Append("|preferredObjectCreation=").Append(options.PreferredObjectCreationHandling)
            .Append("|unknownType=").Append(options.UnknownTypeHandling)
            .Append("|outOfOrderMetadata=").Append(Flag(options.AllowOutOfOrderMetadataProperties))
            .Append("|ignoreReadOnlyProperties=").Append(Flag(options.IgnoreReadOnlyProperties))
            .Append("|ignoreReadOnlyFields=").Append(Flag(options.IgnoreReadOnlyFields))
            .Append("|namingPolicy=camelCase")
            .Append('\n');
    }

    /// <summary>Refuses metadata whose behavior this rendering does not carry.</summary>
    /// <remarks>
    /// Measured against generated metadata, not assumed: every type there carries a converter and resolves
    /// through its own context, and an ignored member carries a <c>ShouldSerialize</c>.
    /// </remarks>
    private static void Describable(JsonSerializerContext context, JsonTypeInfo type)
    {
        var name = Name(type.Type);

        if (type.Kind == JsonTypeInfoKind.Dictionary)
        {
            throw new NotSupportedException($"'{name}' is a dictionary, and dictionary keys are not described.");
        }

        Refuse(
            !ReferenceEquals(type.OriginatingResolver, context),
            name,
            "was resolved by something other than this contract's own context");
        Refuse(
            type.Converter.GetType().Assembly != typeof(JsonSerializer).Assembly,
            name,
            "has a converter of its own");
        Refuse(type.KeyType is not null, name, "is keyed");
        Refuse(type.PolymorphismOptions is not null, name, "is polymorphic");
        Refuse(type.NumberHandling is not null, name, "states its own number handling");
        Refuse(type.UnmappedMemberHandling is not null, name, "states its own unmapped-member handling");
        Refuse(
            type.PreferredPropertyObjectCreationHandling is not null,
            name,
            "states its own object creation handling");

        foreach (var property in type.Properties)
        {
            var member = name + "." + property.Name;
            var ignored = property.Get is null && property.Set is null;

            Refuse(property.CustomConverter is not null, member, "has a converter of its own");
            Refuse(property.NumberHandling is not null, member, "states its own number handling");
            Refuse(property.ObjectCreationHandling is not null, member, "states its own object creation handling");
            Refuse(property.Order != 0, member, "states its own order");
            Refuse(property.IsExtensionData, member, "is extension data");

            // Generated metadata gives an ignored member one; on a member that is read or written it
            // decides at run time whether the member appears at all.
            Refuse(!ignored && property.ShouldSerialize is not null, member, "decides for itself whether to be written");
        }
    }

    private static void Refuse(bool unsupported, string subject, string what)
    {
        if (unsupported)
        {
            throw new NotSupportedException($"'{subject}' {what}, which this rendering does not describe.");
        }
    }

    private static void RenderType(StringBuilder rendering, JsonSerializerContext context, JsonTypeInfo type)
    {
        Describable(context, type);

        rendering.Append("type=").Append(Text(Name(type.Type))).Append("|kind=").Append(type.Kind)
            .Append("|createObject=").Append(Flag(type.CreateObject is not null));

        if (type.ElementType is { } element)
        {
            rendering.Append("|element=").Append(Text(Name(element)));
        }

        // An enumeration's wire form is a number in its underlying type.
        if (type.Type.IsEnum)
        {
            rendering.Append("|underlying=").Append(Text(Name(type.Type.GetEnumUnderlyingType())));
        }

        rendering.Append('\n');

        // Member order is the order a reader positions members in, so it is rendered as it is.
        foreach (var property in type.Properties)
        {
            rendering.Append("  member=").Append(Text(property.Name));

            // An ignored member contributes nothing, and its placeholder type varies with whether the real
            // one is reachable elsewhere. That it is ignored is the fact.
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
    /// Without assembly qualification, which <see cref="Type.FullName"/> adds for every generic argument
    /// and which would move the hash on a framework patch that changed nothing.
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

    /// <summary>Length-prefixes author-supplied text so no value can be read as the structure around it.</summary>
    /// <remarks>A null is its own mark, distinct from empty.</remarks>
    private static string Text(string? value) =>
        value is null ? "~" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Hash(string rendering) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rendering)));
}
