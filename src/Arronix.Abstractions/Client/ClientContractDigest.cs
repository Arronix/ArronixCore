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
/// Taken over the running metadata so a generator's compile-time answer can be checked against it. Every
/// type is asked of the contract's own context and must come back as the same object, for that exact type,
/// on those exact options; options are never used to resolve, since they can fall back to a reflecting
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

        if (!ReferenceEquals(Metadata(context, root.Type), root))
        {
            throw new ArgumentException(
                $"The metadata for '{root.Type}' is not the metadata this context holds for it.",
                nameof(root));
        }

        var rendering = new StringBuilder();
        RenderOptions(rendering, context, context.Options);

        // One budget over everything this walk will touch, spent before anything iterates. Counting distinct
        // types alone would bound nothing: one type with a million members is one type.
        var remaining = ClientContractLimits.MaxNodes;
        var seen = new HashSet<Type> { root.Type };
        var pending = new Queue<(JsonTypeInfo Type, int Depth)>();
        pending.Enqueue((root, 1));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Dequeue();

            if (depth > ClientContractLimits.MaxDepth)
            {
                throw new NotSupportedException(
                    $"This contract's serialization graph nests deeper than {ClientContractLimits.MaxDepth} "
                    + "levels.");
            }

            // Read once, charged, then handed on: what is spent is exactly what the rendering walks, and
            // an enum's names are gathered once rather than once to count and again to render.
            var properties = current.Properties;
            var names = current.Type.IsEnum ? Enum.GetNames(current.Type) : [];

            Spend(ref remaining, 1, "serialization graph");
            Spend(ref remaining, properties.Count, "serialization graph");
            Spend(ref remaining, names.Length, "serialization graph");

            RenderType(rendering, context, current, properties, names);

            foreach (var next in Reachable(current, properties))
            {
                if (seen.Add(next))
                {
                    pending.Enqueue((Metadata(context, next), depth + 1));
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

        // Iterative, and bounded by the same budget the serialization walk spends. A schema is a shape the
        // contract's own code returns: it may nest without end, contain itself, or answer differently on a
        // second read, and this is the only walk over it.
        var remaining = ClientContractLimits.MaxNodes;
        var open = new HashSet<FieldDescriptor>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(FieldDescriptor Field, int Depth, bool Leaving)>();

        Schedule(pending, schema, 1, ref remaining);

        while (pending.Count > 0)
        {
            var (field, depth, leaving) = pending.Pop();

            if (leaving)
            {
                open.Remove(field);
                continue;
            }

            if (!open.Add(field))
            {
                throw new NotSupportedException("This contract's projection schema contains itself.");
            }

            if (depth > ClientContractLimits.MaxDepth)
            {
                throw new NotSupportedException(
                    $"This contract's projection schema nests deeper than {ClientContractLimits.MaxDepth} "
                    + "levels.");
            }

            RenderField(rendering, field, depth, ref remaining);
            pending.Push((field, depth, true));
            Schedule(pending, field.Components, depth + 1, ref remaining);
        }

        return rendering.ToString();
    }

    /// <summary>Hashes a projection schema.</summary>
    /// <param name="entityType">The entity the schema describes.</param>
    /// <param name="schema">The declared fields, in declaration order.</param>
    /// <returns>The SHA-256, upper-case hexadecimal.</returns>
    public static string OfProjection(Type entityType, IReadOnlyList<FieldDescriptor> schema) =>
        Hash(RenderProjection(entityType, schema));

    /// <summary>Reads the metadata a context holds for one type, and proves it is that type's.</summary>
    private static JsonTypeInfo Metadata(JsonSerializerContext context, Type type)
    {
        var found = context.GetTypeInfo(type)
            ?? throw new InvalidOperationException(
                $"This contract's serialization context holds no metadata for '{type}', so the graph it "
                + "describes is not the graph a compiler generated.");

        if (found.Type != type)
        {
            throw new InvalidOperationException(
                $"This contract's serialization context answered for '{found.Type}' when asked about "
                + $"'{type}', so what it describes is not what it was asked to describe.");
        }

        if (!ReferenceEquals(context.GetTypeInfo(type), found))
        {
            throw new InvalidOperationException(
                $"This contract's serialization context answers differently each time it is asked about "
                + $"'{type}', so no rendering of it describes what a reader will use.");
        }

        return found;
    }

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
        Refuse(!options.IsReadOnly, "these options", "are still open to change");
        Refuse(options.ReferenceHandler is not null, "these options", "preserve references");
        Refuse(IgnoresNullValues(options), "these options", "drop null values");
        Refuse(options.InferClosedTypePolymorphism, "these options", "infer polymorphism");
        Refuse(options.TypeClassifiers.Count != 0, "these options", "carry type classifiers");
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

        // A consistency check, not provenance: the property is settable until the metadata is read-only.
        // What is observable is that the context returns this exact object for this exact type, which
        // Metadata proves on the way in.
        Refuse(
            !ReferenceEquals(type.OriginatingResolver, context),
            name,
            "does not agree that this contract's context resolved it");
        // Assembly alone is not proof: a framework converter can be configured to write something else,
        // and a string-enum converter is an EnumConverter like any other. The enum's wire form is measured
        // below rather than inferred from the converter's identity.
        Refuse(
            type.Converter.GetType().Assembly != typeof(JsonSerializer).Assembly,
            name,
            "has a converter of its own");
        Refuse(type.KeyType is not null, name, "is keyed");
        Refuse(!ReferenceEquals(type.Options, context.Options), name, "was built for other options");
        Refuse(!type.IsReadOnly, name, "is still open to change");
        Refuse(type.TypeClassifier is not null, name, "is classified at run time");
        Refuse(type.UnionCases.Count != 0, name, "is a union");
        Refuse(type.UnionConstructor is not null, name, "is constructed as a union");
        Refuse(type.UnionDeconstructor is not null, name, "is deconstructed as a union");

        // A callback runs against the value on the way in or out and can change it, so a graph carrying one
        // does something no rendering of its members describes. Generated metadata has none unless the type
        // implements one of the framework's four callback contracts, which the generator refuses.
        Refuse(type.OnSerializing is not null, name, "runs a callback before it is written");
        Refuse(type.OnSerialized is not null, name, "runs a callback after it is written");
        Refuse(type.OnDeserializing is not null, name, "runs a callback before it is read");
        Refuse(type.OnDeserialized is not null, name, "runs a callback after it is read");
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
            Refuse(!ReferenceEquals(property.Options, context.Options), member, "was built for other options");

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

    /// <remarks>
    /// The obsolete flag is independent of <see cref="JsonSerializerOptions.DefaultIgnoreCondition"/> —
    /// measured, it leaves it at its value — so it is its own behavior and its own refusal.
    /// </remarks>
#pragma warning disable SYSLIB0020
    private static bool IgnoresNullValues(JsonSerializerOptions options) => options.IgnoreNullValues;
#pragma warning restore SYSLIB0020

    private static void RenderType(
        StringBuilder rendering,
        JsonSerializerContext context,
        JsonTypeInfo type,
        IList<JsonPropertyInfo> properties,
        string[] names)
    {
        Describable(context, type);

        rendering.Append("type=").Append(Text(Name(type.Type))).Append("|kind=").Append(type.Kind)
            .Append("|createObject=").Append(Flag(type.CreateObject is not null));

        if (type.ElementType is { } element)
        {
            rendering.Append("|element=").Append(Text(Name(element)));
        }

        if (type.Type.IsEnum)
        {
            RenderEnum(rendering, type, names);
        }

        rendering.Append('\n');

        // Member order is the order a reader positions members in, so it is rendered as it is.
        foreach (var property in properties)
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
                .Append("|setNullable=").Append(Flag(property.IsSetNullable));

            RenderParameter(rendering, property.AssociatedParameter);
            rendering.Append('\n');
        }
    }

    /// <summary>Renders the constructor parameter, if any, that fills a member.</summary>
    /// <remarks>
    /// A default decides what a member becomes when a payload omits it, so it is part of what a payload
    /// means. A required member with no constructor parameter still gets one, marked as a member
    /// initializer.
    /// </remarks>
    private static void RenderParameter(StringBuilder rendering, JsonParameterInfo? parameter)
    {
        if (parameter is null)
        {
            rendering.Append("|parameter=~");
            return;
        }

        rendering.Append("|parameter=").Append(Number(parameter.Position))
            .Append('|').Append(Text(parameter.Name))
            .Append('|').Append(Text(Name(parameter.ParameterType)))
            .Append("|memberInitializer=").Append(Flag(parameter.IsMemberInitializer))
            .Append("|nullable=").Append(Flag(parameter.IsNullable))
            .Append("|default=").Append(parameter.HasDefaultValue ? Literal(parameter.DefaultValue) : "~");
    }

    /// <summary>Renders a default value the way a compiler renders the same constant.</summary>
    private static string Literal(object? value) => value switch
    {
        null => "null",
        string text => Text(text),
        bool flag => Flag(flag),
        Enum => throw new NotSupportedException(
            "An enumerated default value is not one this rendering describes."),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException(
            $"A default value of type '{value.GetType()}' is not one this rendering describes."),
    };

    /// <summary>
    /// Renders an enumeration by what its own metadata writes, not by what its converter is.
    /// </summary>
    /// <remarks>
    /// A names-writing converter is an <c>EnumConverter</c> like any other, registers nothing on the
    /// options, and changes the payload, so nothing about the metadata's shape reveals it. Two values are
    /// written: a declared constant, chosen by ordinal name so the choice is stable, which a names mode
    /// renders as a string and a numeric mode as a number; and zero, which is a second reading whenever it
    /// is not itself declared. This detects the framework's supported converter modes; what an arbitrary
    /// delegate would do is pinned by the assembly's content, not here.
    /// </remarks>
    private static void RenderEnum(StringBuilder rendering, JsonTypeInfo type, string[] names)
    {
        rendering.Append("|underlying=").Append(Text(Name(type.Type.GetEnumUnderlyingType())));

        Array.Sort(names, StringComparer.Ordinal);

        rendering.Append("|named=").Append(names.Length == 0 ? "~" : Text(names[0]))
            .Append("|namedWire=")
            .Append(names.Length == 0 ? "~" : Text(Written(type, Enum.Parse(type.Type, names[0]))))
            .Append("|zeroWire=").Append(Text(Written(type, Enum.ToObject(type.Type, 0))));
    }

    private static string Written(JsonTypeInfo type, object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, type);
        }
        catch (NotSupportedException error)
        {
            throw new NotSupportedException(
                $"'{Name(type.Type)}' could not be written through its own metadata: {error.Message}");
        }
    }

    private static IEnumerable<Type> Reachable(JsonTypeInfo type, IList<JsonPropertyInfo> properties)
    {
        foreach (var property in properties)
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

    private static void RenderField(
        StringBuilder rendering,
        FieldDescriptor field,
        int depth,
        ref int remaining)
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

        // Choices spend the same budget as fields and components: they are values a contract describes, and
        // a list of them is as unbounded as any other it returns. Read once, counted once.
        var choices = field.Choices
            ?? throw new NotSupportedException("This contract's projection schema carries a null choice list.");

        var choiceCount = choices.Count;
        Spend(ref remaining, choiceCount, "projection schema");

        for (var index = 0; index < choiceCount; index++)
        {
            var choice = choices[index];
            rendering.Append(' ', (depth + 1) * 2)
                .Append("choice=").Append(Text(choice.Value)).Append('|').Append(Text(choice.Name)).Append('\n');
        }
    }

    /// <summary>Charges a level to the budget and pushes it, reading each entry exactly once.</summary>
    private static void Schedule(
        Stack<(FieldDescriptor Field, int Depth, bool Leaving)> pending,
        IReadOnlyList<FieldDescriptor>? fields,
        int depth,
        ref int remaining)
    {
        if (fields is null)
        {
            throw new NotSupportedException("This contract's projection schema carries a null field list.");
        }

        var count = fields.Count;
        Spend(ref remaining, count, "projection schema");

        for (var index = count - 1; index >= 0; index--)
        {
            // Read once: a second read of a contract's own list may answer differently, and what was
            // checked would not be what was walked.
            var field = fields[index]
                ?? throw new NotSupportedException("This contract's projection schema carries a null field.");

            pending.Push((field, depth, false));
        }
    }

    /// <summary>Spends part of a walk's budget, refusing a shape that asks for more than there is.</summary>
    private static void Spend(ref int remaining, int cost, string subject)
    {
        if (cost < 0 || cost > remaining)
        {
            throw new NotSupportedException(
                $"This contract's {subject} describes more than {ClientContractLimits.MaxNodes} values.");
        }

        remaining -= cost;
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
