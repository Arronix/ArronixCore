using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Arronix.Generators.MediaShapeModel;

namespace Arronix.Generators;

/// <summary>
/// Emits the declared client contract entry point of a media item type.
/// </summary>
/// <remarks>
/// Emits three things: a one-way projection of a typed value into declared presentation data, an assembly
/// attribute whose constructor arguments carry every fact a consumer decides on, and the hashes over both.
/// Serialization itself is the framework's own generator, from a declared <c>[JsonSerializable]</c>
/// context. The shape is read through <see cref="MediaShapeModel"/>, which Host's compiled shapes also use;
/// where the two outputs differ they differ deliberately, and the differences are recorded in
/// <c>docs/research/g07/client-contract-declaration.md</c>.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ClientContractGenerator : IIncrementalGenerator
{
    private const string MediaItemName =
        "Arronix.Abstractions.Media.MediaItem<TItem, TReleaseTimeline, TReleaseStage>";

    private const int CompositeKind = 20;
    private const int ReferenceKind = 12;

    private static readonly DiagnosticDescriptor ShapeNotDescribable = new(
        "ARX1011",
        "A client contract shape cannot be described",
        "'{0}' cannot be published to a client: {1}",
        "Arronix.Authoring",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The declared entry point is generated from the item type's own shape and from the serialization metadata the framework will produce for it. A shape the generator cannot model is reported rather than described wrongly, because a hash that does not describe the real wire is worse than no hash.");

    private static readonly DiagnosticDescriptor SerializationContextMissing = new(
        "ARX1010",
        "A client-safe item type needs a declared serialization context",
        "Declare 'internal sealed partial class {1}ClientJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext' with [JsonSerializable(typeof({1}))] so '{0}' can be read in a browser",
        "Arronix.Authoring",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The trimming-safe serialization metadata is produced by the framework's own source generator, and one source generator cannot read another's output in the same compilation. Until that changes, the context is one declared line in the contract assembly and this diagnostic names it.");

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entries = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntax, _) => FindEntry(syntax))
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!);

        var contexts = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntax, _) => FindSerializationContext(syntax))
            .Where(static declared => declared is not null)
            .Select(static (declared, _) => declared!)
            .Collect();

        context.RegisterSourceOutput(entries.Combine(contexts), static (production, pair) =>
        {
            var (entry, declared) = pair;
            var serializer = Match(entry.Symbol, declared, out var ambiguous);

            if (ambiguous)
            {
                production.ReportDiagnostic(Diagnostic.Create(
                    ShapeNotDescribable,
                    entry.Location,
                    entry.Symbol.ToDisplayString(),
                    "more than one serialization context declares metadata for it, and they can declare "
                    + "different options"));

                return;
            }

            if (serializer is null)
            {
                production.ReportDiagnostic(Diagnostic.Create(
                    SerializationContextMissing,
                    entry.Location,
                    entry.Symbol.ToDisplayString(),
                    entry.Symbol.Name));

                return;
            }

            var emitted = Emit(entry, serializer, out var refusal);

            if (emitted is null)
            {
                production.ReportDiagnostic(Diagnostic.Create(
                    ShapeNotDescribable,
                    entry.Location,
                    entry.Symbol.ToDisplayString(),
                    refusal));

                return;
            }

            production.AddSource(entry.HintName, SourceText.From(emitted, Encoding.UTF8));
        });
    }

    /// <summary>
    /// Finds the serialization context a compilation declares for one entity, by what it declares rather
    /// than by what it is called.
    /// </summary>
    /// <remarks>
    /// Every framework type is resolved from the compilation and compared by symbol identity. A name
    /// comparison answers the same for a type somebody declared with the same name, and this generator
    /// decides what a browser is handed.
    /// </remarks>
    private static SerializationContext? FindSerializationContext(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol
            || FrameworkSymbols.Resolve(context.SemanticModel.Compilation) is not { } framework)
        {
            return null;
        }

        var isContext = false;
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, framework.SerializerContext))
            {
                isContext = true;
                break;
            }
        }

        if (!isContext)
        {
            return null;
        }

        var serialized = new List<INamedTypeSymbol>();
        AttributeData? options = null;
        string? unsupported = null;

        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, framework.Serializable)
                && attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is INamedTypeSymbol serializedType)
            {
                serialized.Add(serializedType);
                unsupported ??= UnsupportedTarget(attribute, serializedType);
            }
            else if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, framework.GenerationOptions))
            {
                options = attribute;
            }
        }

        return serialized.Count == 0
            ? null
            : new SerializationContext(symbol, serialized, framework, unsupported ?? Unsupported(options));
    }

    /// <summary>
    /// Names the first declared target option this model does not describe.
    /// </summary>
    /// <remarks>
    /// <c>GenerationMode</c> selects which halves the framework generates. The default inherits both, and
    /// metadata is what a reader needs, so any value carrying the metadata flag is fine and a
    /// serialization-only one is not. <c>TypeInfoPropertyName</c> renames the generated property, which this
    /// contract never reads: the root is asked for by type. Admitted deliberately, not by omission.
    /// </remarks>
    private static string? UnsupportedTarget(AttributeData target, INamedTypeSymbol serialized)
    {
        foreach (var argument in target.NamedArguments)
        {
            switch (argument.Key)
            {
                case "GenerationMode":
                    if (UnsupportedGenerationMode(argument.Value) is { } mode)
                    {
                        return $"its serialization context declares {mode} for "
                            + $"'{serialized.ToDisplayString()}', which produces no metadata to read with";
                    }

                    break;

                case "TypeInfoPropertyName":
                    break;

                default:
                    return $"its serialization context declares '{argument.Key}' on the target for "
                        + $"'{serialized.ToDisplayString()}', which this model does not describe";
            }
        }

        return null;
    }

    /// <summary>Names a generation mode that leaves a reader without metadata.</summary>
    /// <remarks>
    /// Read as flags, not as a member name. A combined value has no named field, so a comparison against
    /// one would let it through unexamined.
    /// </remarks>
    private static string? UnsupportedGenerationMode(TypedConstant argument)
    {
        if (argument.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration
            || argument.Value is not int declared)
        {
            return "a generation mode this model cannot read";
        }

        if (Constant(enumeration, "Metadata") is not { } metadata)
        {
            return "a generation mode whose metadata flag this model cannot find";
        }

        // Zero inherits the options-level default, which carries metadata.
        return declared == 0 || (declared & metadata) != 0
            ? null
            : "GenerationMode " + declared.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int? Constant(INamedTypeSymbol enumeration, string name)
    {
        foreach (var member in enumeration.GetMembers(name).OfType<IFieldSymbol>())
        {
            if (member.HasConstantValue && member.ConstantValue is int value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Names the first declared serialization option this model does not describe.
    /// </summary>
    /// <remarks>
    /// One exact set is modeled — strict defaults and the camel-case naming policy — and everything else is
    /// refused. Reading two options and ignoring the rest is the failure that matters here: a declaration
    /// that also set, say, a number-handling mode would still be published, under a hash that describes a
    /// wire it does not have.
    /// </remarks>
    private static string? Unsupported(AttributeData? options)
    {
        if (options is null)
        {
            return "its serialization context declares no [JsonSourceGenerationOptions]";
        }

        if (options.ConstructorArguments.Length > 1)
        {
            return "its serialization context declares options this model does not read";
        }

        if (options.ConstructorArguments.Length == 1
            && EnumArgument(options.ConstructorArguments[0]) != "Strict")
        {
            return "its serialization context does not declare JsonSerializerDefaults.Strict";
        }

        var strict = options.ConstructorArguments.Length == 1;
        var camelCase = false;

        foreach (var argument in options.NamedArguments)
        {
            switch (argument.Key)
            {
                case "Defaults":
                    if (EnumArgument(argument.Value) != "Strict")
                    {
                        return "its serialization context does not declare JsonSerializerDefaults.Strict";
                    }

                    strict = true;
                    break;

                case "PropertyNamingPolicy":
                    if (EnumArgument(argument.Value) != "CamelCase")
                    {
                        return "its serialization context does not declare the camel-case naming policy";
                    }

                    camelCase = true;
                    break;

                default:
                    return $"its serialization context declares '{argument.Key}', which this model does "
                        + "not describe";
            }
        }

        if (!strict)
        {
            return "its serialization context does not declare JsonSerializerDefaults.Strict";
        }

        return camelCase ? null : "its serialization context does not declare the camel-case naming policy";
    }

    /// <summary>Finds the one context that declares metadata for an entity.</summary>
    /// <remarks>
    /// Two contexts claiming one entity is refused rather than resolved. They can declare different
    /// options, so picking either would publish a hash describing a wire the other one writes.
    /// </remarks>
    private static SerializationContext? Match(
        INamedTypeSymbol entity,
        System.Collections.Immutable.ImmutableArray<SerializationContext> declared,
        out bool ambiguous)
    {
        SerializationContext? found = null;
        ambiguous = false;

        foreach (var candidate in declared)
        {
            foreach (var serialized in candidate.Serialized)
            {
                if (!SymbolEqualityComparer.Default.Equals(serialized, entity))
                {
                    continue;
                }

                if (found is not null && !SymbolEqualityComparer.Default.Equals(found.Symbol, candidate.Symbol))
                {
                    ambiguous = true;
                    return null;
                }

                found = candidate;
                break;
            }
        }

        return found;
    }

    private static Entry? FindEntry(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol
            || symbol.DeclaredAccessibility != Accessibility.Public
            || symbol.IsAbstract
            || symbol.IsGenericType)
        {
            return null;
        }

        var itemBase = symbol.BaseType;
        while (itemBase is not null && itemBase.OriginalDefinition.ToDisplayString() != MediaItemName)
        {
            itemBase = itemBase.BaseType;
        }

        return itemBase is null
            ? null
            : new Entry(
                symbol,
                declaration.Identifier.GetLocation(),
                symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
                Sanitize(symbol.ToDisplayString()) + ".ClientContract.g.cs");
    }

    private static string? Emit(Entry entry, SerializationContext serializer, out string? refusal)
    {
        refusal = null;

        if (serializer.Unsupported is { } unsupported)
        {
            refusal = unsupported;
            return null;
        }

        var graph = ClientContractSerializationModel.Render(
            entry.Symbol,
            serializer.Framework,
            out var derived,
            out refusal);

        if (graph is null)
        {
            return null;
        }

        var projection = new Projection();
        var schema = projection.Describe(entry.Symbol);

        var projectionHash = Hash(RenderSchema(entry.Symbol, schema));
        var metadataHash = Hash(graph);

        var contractName = entry.Symbol.Name + "ClientContract";
        var contextName = TypeName(serializer.Symbol);
        var attributeName = entry.Symbol.Name + "ClientContractEntryPointAttribute";
        var qualifiedAttribute = (entry.Namespace is null ? "global::" : "global::" + entry.Namespace + ".")
            + attributeName;

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        // Every value a decision is made on is a constructor argument, so it lives in the custom attribute
        // blob: readable from the exact bytes before anything is loaded, and readable by a host that holds
        // the assembly without calling into it.
        source.Append("[assembly: ").Append(qualifiedAttribute).Append("(typeof(")
            .Append(TypeName(entry.Symbol)).Append("), ")
            .Append(Literal(metadataHash)).Append(", ")
            .Append(Literal(projectionHash)).AppendLine(")]");
        source.AppendLine();

        if (entry.Namespace is not null)
        {
            source.Append("namespace ").Append(entry.Namespace).AppendLine(";");
            source.AppendLine();
        }

        EmitContract(source, contractName, contextName, entry.Symbol, schema, projection, derived);
        source.AppendLine();
        EmitAttribute(source, attributeName, contractName, contextName, entry.Symbol);

        return source.ToString();
    }

    private static void EmitContract(
        StringBuilder source,
        string contractName,
        string contextName,
        INamedTypeSymbol item,
        IReadOnlyList<Field> schema,
        Projection projection,
        IReadOnlyList<string> derived)
    {
        source.AppendLine("/// <summary>The generated client contract of one media item type.</summary>");
        source.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");

        // Internal. A contract assembly's public surface is its domain, and everything below is reached
        // through the declaration, by exact base type, from the one assembly both sides already share.
        source.Append("internal static class ").AppendLine(contractName);
        source.AppendLine("{");

        // Asked for by type rather than reached through a property the framework's generator happens to
        // name after it. That name is a convention, and a convention is a thing that can change.
        source.Append("    internal static readonly global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<")
            .Append(TypeName(item)).AppendLine("> Root = ResolveRoot();");
        source.AppendLine();
        source.Append("    private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<")
            .Append(TypeName(item)).AppendLine("> ResolveRoot()");
        source.Append("        => ").Append(contextName).Append(".Default.GetTypeInfo(typeof(").Append(TypeName(item))
            .AppendLine("))");
        source.Append("            as global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<")
            .Append(TypeName(item)).AppendLine(">");
        source.AppendLine("            ?? throw new global::System.InvalidOperationException(");
        source.Append("                \"'").Append(contextName).Append("' holds no metadata for '")
            .Append(item.ToDisplayString()).AppendLine("'.\");");
        source.AppendLine();
        source.AppendLine("    private static readonly global::System.Collections.ObjectModel.ReadOnlyCollection<global::Arronix.Abstractions.Shape.FieldDescriptor> Declared =");
        source.AppendLine("        new(new global::Arronix.Abstractions.Shape.FieldDescriptor[]");
        source.AppendLine("        {");

        foreach (var field in schema)
        {
            EmitDescriptor(source, field, "            ");
        }

        source.AppendLine("        });");
        source.AppendLine();
        source.AppendLine("    /// <summary>Gets what a projection from this entry point contains.</summary>");
        source.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Schema => Declared;");
        source.AppendLine();

        if (derived.Count > 0)
        {
            source.AppendLine("    private static readonly global::System.Collections.ObjectModel.ReadOnlyCollection<string> Derived =");
            source.AppendLine("        new(new string[]");
            source.AppendLine("        {");

            foreach (var name in derived)
            {
                source.Append("            ").Append(Literal(name)).AppendLine(",");
            }

            source.AppendLine("        });");
            source.AppendLine();
        }

        source.AppendLine("    /// <summary>Reads one serialized entity into this assembly's own typed value.</summary>");
        source.Append("    public static ").Append(TypeName(item)).AppendLine(" Read(global::System.ReadOnlySpan<byte> utf8Json)");
        source.AppendLine("    {");

        if (derived.Count > 0)
        {
            source.AppendLine("        RefuseDerivedMembers(utf8Json);");
        }

        source.AppendLine("        return global::System.Text.Json.JsonSerializer.Deserialize(utf8Json, Root)");
        source.Append("            ?? throw new global::System.Text.Json.JsonException(\"The payload is a null ")
            .Append(item.Name).AppendLine(".\");");
        source.AppendLine("    }");
        source.AppendLine();

        if (derived.Count > 0)
        {
            // A computed member is mapped rather than unknown, so the framework's unmapped-member rule
            // silently drops one a payload supplies. A sender that wrote it would believe it had been read.
            source.AppendLine("    private static void RefuseDerivedMembers(global::System.ReadOnlySpan<byte> utf8Json)");
            source.AppendLine("    {");
            source.AppendLine("        var reader = new global::System.Text.Json.Utf8JsonReader(utf8Json);");
            source.AppendLine();
            source.AppendLine("        while (reader.Read())");
            source.AppendLine("        {");
            source.AppendLine("            if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName)");
            source.AppendLine("            {");
            source.AppendLine("                continue;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            for (var index = 0; index < Derived.Count; index++)");
            source.AppendLine("            {");
            source.AppendLine("                if (reader.ValueTextEquals(Derived[index]))");
            source.AppendLine("                {");
            source.AppendLine("                    throw new global::System.Text.Json.JsonException(");
            source.AppendLine("                        \"'\" + Derived[index] + \"' is computed by this contract and cannot be supplied by a payload.\");");
            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine();
        }

        source.AppendLine("    /// <summary>Serializes one typed entity in exactly the shape the reader accepts.</summary>");
        source.Append("    public static byte[] Write(").Append(TypeName(item)).AppendLine(" value)");
        source.AppendLine("        => global::System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Root);");
        source.AppendLine();

        source.AppendLine("    /// <summary>Projects one typed entity into one-way presentation data.</summary>");
        source.Append("    public static global::Arronix.Abstractions.Client.ProjectedEntity Project(")
            .Append(TypeName(item)).AppendLine(" value)");
        source.AppendLine("    {");
        source.AppendLine("        if (value is null)");
        source.AppendLine("        {");
        source.AppendLine("            throw new global::System.ArgumentNullException(nameof(value));");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        var fields = new global::Arronix.Abstractions.Client.ProjectedField[Declared.Count];");

        for (var index = 0; index < schema.Count; index++)
        {
            source.Append("        fields[").Append(Number(index))
                .Append("] = new global::Arronix.Abstractions.Client.ProjectedField(Declared[")
                .Append(Number(index)).Append("], ")
                .Append(projection.ValueOf(schema[index], "value." + schema[index].Property.Name))
                .AppendLine(");");
        }

        source.AppendLine();
        source.Append("        return new global::Arronix.Abstractions.Client.ProjectedEntity(typeof(")
            .Append(TypeName(item)).AppendLine("), fields);");
        source.AppendLine("    }");

        projection.EmitHelpers(source);

        source.AppendLine("}");
    }

    private static void EmitAttribute(
        StringBuilder source,
        string attributeName,
        string contractName,
        string contextName,
        INamedTypeSymbol item)
    {
        source.AppendLine("/// <summary>This assembly's declared client contract entry point.</summary>");
        source.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");

        // Internal, and that is the point of reaching it by exact base type. A consumer finds the
        // declaration through the one assembly both sides already share, so the implementation never has to
        // be public, never has to be named, and never becomes surface anybody can compile against.
        source.Append("internal sealed class ").Append(attributeName)
            .AppendLine(" : global::Arronix.Abstractions.Client.ClientContractEntryPointAttribute");
        source.AppendLine("{");
        source.Append("    public ").Append(attributeName)
            .AppendLine("(global::System.Type entityType, string generatedMetadataHash, string projectionSchemaHash)");
        source.AppendLine("        : base(entityType, generatedMetadataHash, projectionSchemaHash)");
        source.AppendLine("    {");
        source.AppendLine("    }");
        source.AppendLine();
        source.Append("    public override global::System.Text.Json.Serialization.JsonSerializerContext SerializationContext => ")
            .Append(contextName).AppendLine(".Default;");
        source.AppendLine();
        source.Append("    public override global::System.Text.Json.Serialization.Metadata.JsonTypeInfo EntityTypeInfo => ")
            .Append(contractName).AppendLine(".Root;");
        source.AppendLine();
        source.Append("    public override global::System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Schema => ")
            .Append(contractName).AppendLine(".Schema;");
        source.AppendLine();
        source.AppendLine("    public override object Deserialize(global::System.ReadOnlySpan<byte> utf8Json)");
        source.Append("        => ").Append(contractName).AppendLine(".Read(utf8Json);");
        source.AppendLine();
        source.AppendLine("    public override byte[] Serialize(object entity)");
        source.Append("        => ").Append(contractName).AppendLine(".Write(Typed(entity));");
        source.AppendLine();
        source.AppendLine("    public override global::Arronix.Abstractions.Client.ProjectedEntity Project(object entity)");
        source.Append("        => ").Append(contractName).AppendLine(".Project(Typed(entity));");
        source.AppendLine();
        source.Append("    private ").Append(TypeName(item)).AppendLine(" Typed(object entity)");
        source.AppendLine("    {");
        source.AppendLine("        if (entity is null)");
        source.AppendLine("        {");
        source.AppendLine("            throw new global::System.ArgumentNullException(nameof(entity));");
        source.AppendLine("        }");
        source.AppendLine();
        source.Append("        if (entity is not ").Append(TypeName(item)).AppendLine(" typed)");
        source.AppendLine("        {");
        source.AppendLine("            throw new global::System.ArgumentException(");
        source.AppendLine("                \"The value is not a '\" + EntityType.FullName + \"'.\",");
        source.AppendLine("                nameof(entity));");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        return typed;");
        source.AppendLine("    }");
        source.AppendLine("}");
    }

    private static void EmitDescriptor(StringBuilder source, Field field, string indent)
    {
        source.Append(indent).AppendLine("new global::Arronix.Abstractions.Shape.FieldDescriptor");
        source.Append(indent).AppendLine("{");
        source.Append(indent).Append("    FieldId = ").Append(Literal(field.FieldId)).AppendLine(",");
        source.Append(indent).Append("    Name = ").Append(Literal(field.Name)).AppendLine(",");
        source.Append(indent).Append("    Description = ").Append(LiteralOrNull(field.Description)).AppendLine(",");
        source.Append(indent).Append("    ValueKind = (global::Arronix.Abstractions.Shape.FieldValueKind)")
            .Append(Number(field.Kind)).AppendLine(",");
        source.Append(indent).Append("    Semantics = (global::Arronix.Abstractions.Shape.FieldSemantics)")
            .Append(Number(field.Semantics)).AppendLine(",");
        source.Append(indent).Append("    Prominence = (global::Arronix.Abstractions.Shape.Prominence)")
            .Append(Number(field.Prominence)).AppendLine(",");
        source.Append(indent).Append("    Multivalued = ").Append(Bool(field.Multivalued)).AppendLine(",");
        source.Append(indent).Append("    Editable = ").Append(Bool(field.Editable)).AppendLine(",");
        source.Append(indent).Append("    Unit = ").Append(LiteralOrNull(field.Unit)).AppendLine(",");

        if (field.Choices.Count == 0)
        {
            source.Append(indent).AppendLine("    Choices = global::System.Array.Empty<global::Arronix.Abstractions.Shape.FacetValue>(),");
        }
        else
        {
            source.Append(indent).AppendLine("    Choices = new global::Arronix.Abstractions.Shape.FacetValue[]");
            source.Append(indent).AppendLine("    {");

            foreach (var choice in field.Choices)
            {
                source.Append(indent).Append("        new(").Append(Literal(choice.Key)).Append(", ")
                    .Append(Literal(choice.Value)).AppendLine("),");
            }

            source.Append(indent).AppendLine("    },");
        }

        if (field.Components.Count == 0)
        {
            source.Append(indent).AppendLine("    Components = global::System.Array.Empty<global::Arronix.Abstractions.Shape.FieldDescriptor>(),");
        }
        else
        {
            source.Append(indent).AppendLine("    Components = new global::Arronix.Abstractions.Shape.FieldDescriptor[]");
            source.Append(indent).AppendLine("    {");

            foreach (var component in field.Components)
            {
                EmitDescriptor(source, component, indent + "        ");
            }

            source.Append(indent).AppendLine("    },");
        }

        source.Append(indent).AppendLine("},");
    }

    /// <remarks>
    /// The same rendering <c>ClientContractDigest.RenderProjection</c> produces from a live schema, so the
    /// literal emitted here can be checked against an independent recomputation.
    /// </remarks>
    private static string RenderSchema(INamedTypeSymbol item, IReadOnlyList<Field> schema)
    {
        var rendering = new StringBuilder();
        rendering.Append("entity=")
            .Append(ClientContractSerializationModel.Text(ClientContractSerializationModel.Name(item)))
            .Append('\n');

        foreach (var field in schema)
        {
            RenderField(rendering, field, 1);
        }

        return rendering.ToString();
    }

    private static void RenderField(StringBuilder rendering, Field field, int depth)
    {
        rendering.Append(' ', depth * 2)
            .Append("field=").Append(ClientContractSerializationModel.Text(field.FieldId))
            .Append('|').Append(ClientContractSerializationModel.Text(field.Name))
            .Append('|').Append(ClientContractSerializationModel.Text(field.Description))
            .Append('|').Append(Number(field.Kind))
            .Append('|').Append(Number(field.Semantics))
            .Append('|').Append(Number(field.Prominence))
            .Append('|').Append(field.Multivalued ? "many" : "one")
            .Append('|').Append(field.Editable ? "editable" : "read-only")
            .Append('|').Append(ClientContractSerializationModel.Text(field.Unit))
            .Append('\n');

        foreach (var choice in field.Choices)
        {
            rendering.Append(' ', (depth + 1) * 2)
                .Append("choice=").Append(ClientContractSerializationModel.Text(choice.Key))
                .Append('|').Append(ClientContractSerializationModel.Text(choice.Value)).Append('\n');
        }

        foreach (var component in field.Components)
        {
            RenderField(rendering, component, depth + 1);
        }
    }

    private static string Hash(string rendering)
    {
        using var algorithm = SHA256.Create();
        var digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(rendering));
        var text = new StringBuilder(digest.Length * 2);

        foreach (var value in digest)
        {
            text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private sealed class Entry
    {
        internal Entry(INamedTypeSymbol symbol, Location location, string? namespaceName, string hintName)
        {
            Symbol = symbol;
            Location = location;
            Namespace = namespaceName;
            HintName = hintName;
        }

        internal INamedTypeSymbol Symbol { get; }

        internal Location Location { get; }

        internal string? Namespace { get; }

        internal string HintName { get; }
    }

    /// <summary>One declared serialization context and the types it declares metadata for.</summary>
    internal sealed class SerializationContext
    {
        internal SerializationContext(
            INamedTypeSymbol symbol,
            IReadOnlyList<INamedTypeSymbol> serialized,
            FrameworkSymbols framework,
            string? unsupported)
        {
            Symbol = symbol;
            Serialized = serialized;
            Framework = framework;
            Unsupported = unsupported;
        }

        internal INamedTypeSymbol Symbol { get; }

        internal IReadOnlyList<INamedTypeSymbol> Serialized { get; }

        internal FrameworkSymbols Framework { get; }

        /// <summary>Gets why this context's declared options are not modeled, or <see langword="null"/>.</summary>
        internal string? Unsupported { get; }
    }

    /// <summary>Reads the member name of an enumeration-valued attribute argument.</summary>
    private static string? EnumArgument(TypedConstant? argument)
    {
        if (argument is not { Type: INamedTypeSymbol { TypeKind: TypeKind.Enum } enumeration } value)
        {
            return null;
        }

        foreach (var member in enumeration.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.HasConstantValue && Equals(member.ConstantValue, value.Value))
            {
                return member.Name;
            }
        }

        return null;
    }

    private static string? NamedEnumArgument(AttributeData? attribute, string name)
    {
        if (attribute is null)
        {
            return null;
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return EnumArgument(argument.Value);
            }
        }

        return null;
    }

    /// <summary>One projected field, as a descriptor and as a value expression.</summary>
    internal sealed class Field
    {
        internal Field(IPropertySymbol property, ITypeSymbol element, int kind, Access access)
        {
            Property = property;
            Element = element;
            Kind = kind;
            Reach = access;
        }

        internal IPropertySymbol Property { get; }

        internal ITypeSymbol Element { get; }

        internal int Kind { get; }

        internal Access Reach { get; }

        internal string FieldId { get; set; } = string.Empty;

        internal string Name { get; set; } = string.Empty;

        internal string? Description { get; set; }

        internal string? Unit { get; set; }

        internal int Semantics { get; set; }

        internal int Prominence { get; set; } = 2;

        internal bool Multivalued { get; set; }

        internal bool Editable { get; set; }

        internal bool Nullable { get; set; }

        internal List<KeyValuePair<string, string>> Choices { get; } = new();

        internal List<Field> Components { get; } = new();
    }

    /// <summary>How a projected field's elements are reached from the declared property value.</summary>
    internal enum Access
    {
        /// <summary>The property value is the element.</summary>
        Direct = 0,

        /// <summary>The property value is a list of elements.</summary>
        List = 1,

        /// <summary>The property value is an artwork set, whose images are the elements.</summary>
        ArtworkSet = 2,

        /// <summary>The property value is an external identifier set, whose values are the elements.</summary>
        ExternalIdSet = 3
    }

    /// <summary>
    /// The projection half: which composite and list helpers the emitted contract needs, and what each
    /// field's value expression is.
    /// </summary>
    private sealed class Projection
    {
        private readonly Dictionary<string, int> _compositeIndex = new(StringComparer.Ordinal);
        private readonly List<KeyValuePair<ITypeSymbol, List<Field>>> _composites = new();
        private readonly Dictionary<string, int> _listIndex = new(StringComparer.Ordinal);
        private readonly List<Field> _lists = new();
        private readonly Dictionary<string, int> _enumIndex = new(StringComparer.Ordinal);
        private readonly List<ITypeSymbol> _enums = new();
        private readonly HashSet<string> _scalars = new(StringComparer.Ordinal);

        internal IReadOnlyList<Field> Describe(INamedTypeSymbol entity)
        {
            var fields = new List<Field>();

            foreach (var property in PublicProperties(entity))
            {
                if (!IsExcluded(property))
                {
                    fields.Add(Describe(property, topLevel: true, new List<ITypeSymbol> { entity }));
                }
            }

            return fields;
        }

        private Field Describe(IPropertySymbol property, bool topLevel, List<ITypeSymbol> ancestors)
        {
            var declared = property.Type;
            var listElement = UnwrapList(declared);
            var bare = StripNullable(declared);
            var element = StripNullable(listElement ?? declared);
            var declaredKind = ValueKind(element, property);

            // Host projects a nested entity as a reference to the durable identity it assigned at
            // materialization. A browser projecting a payload holds no such identity, so the entity is
            // projected as its own values kept together instead.
            var kind = declaredKind == ReferenceKind ? CompositeKind : declaredKind;

            var access = listElement is not null
                ? Access.List
                : Is(bare, "Arronix.Abstractions.Media.ArtworkSet")
                    ? Access.ArtworkSet
                    : Is(bare, "Arronix.Abstractions.Media.ExternalIdSet")
                        ? Access.ExternalIdSet
                        : Access.Direct;

            var display = Attribute(property, "DisplayAttribute");

            var field = new Field(property, element, kind, access)
            {
                FieldId = Identifier(property.Name),
                Name = NamedString(display, "Name") ?? Label(property.Name),
                Description = NamedString(display, "Description"),
                Unit = ConstructorString(Attribute(property, "UnitAttribute")),
                Multivalued = access != Access.Direct,
                Editable = topLevel && Has(property, "EditableAttribute") && !Has(property, "DerivedAttribute"),
                Nullable = IsNullable(declared) || (listElement is not null && IsNullable(listElement)),
                Semantics = topLevel ? MediaShapeModel.Semantics(property, declaredKind, element) : 0,
                Prominence = topLevel ? MediaShapeModel.Prominence(property) : 1,
            };

            if (kind == 11 && element is INamedTypeSymbol enumeration)
            {
                foreach (var member in enumeration.GetMembers().OfType<IFieldSymbol>())
                {
                    if (member.HasConstantValue)
                    {
                        field.Choices.Add(new KeyValuePair<string, string>(
                            Identifier(member.Name),
                            Label(member.Name)));
                    }
                }

                Register(_enumIndex, _enums, TypeName(element), element);
            }

            if (kind == CompositeKind)
            {
                field.Components.AddRange(Composite(element, ancestors));
            }

            Plan(field);
            return field;
        }

        private IReadOnlyList<Field> Composite(ITypeSymbol element, List<ITypeSymbol> ancestors)
        {
            var key = TypeName(element);

            if (_compositeIndex.TryGetValue(key, out var existing))
            {
                return _composites[existing].Value;
            }

            // A value graph that contains itself has no finite projection. Nothing in the common item
            // shape does; a media kind that introduced one would find its own component list empty here,
            // so the boundary is stated rather than discovered.
            foreach (var ancestor in ancestors)
            {
                if (SymbolEqualityComparer.Default.Equals(ancestor, element))
                {
                    return Array.Empty<Field>();
                }
            }

            if (element is not INamedTypeSymbol named)
            {
                return Array.Empty<Field>();
            }

            var components = new List<Field>();
            _compositeIndex[key] = _composites.Count;
            _composites.Add(new KeyValuePair<ITypeSymbol, List<Field>>(element, components));

            var next = new List<ITypeSymbol>(ancestors) { element };

            foreach (var property in PublicProperties(named))
            {
                if (!IsExcluded(property))
                {
                    components.Add(Describe(property, topLevel: false, next));
                }
            }

            return components;
        }

        private void Plan(Field field)
        {
            switch (field.Reach)
            {
                case Access.ArtworkSet:
                    _scalars.Add("ArtworkValue");
                    _scalars.Add("ProjectArtworkSet");
                    break;

                case Access.ExternalIdSet:
                    _scalars.Add("ExternalValue");
                    _scalars.Add("ProjectExternalIdSet");
                    break;

                case Access.List:
                    _scalars.Add(ScalarName(field.Kind));
                    Register(_listIndex, _lists, ListKey(field), field);
                    break;

                default:
                    _scalars.Add(ScalarName(field.Kind));
                    break;
            }
        }

        private static string ListKey(Field field) => TypeName(field.Element) + "|" + Number(field.Kind);

        private static void Register<T>(Dictionary<string, int> index, List<T> values, string key, T value)
        {
            if (!index.ContainsKey(key))
            {
                index[key] = values.Count;
                values.Add(value);
            }
        }

        internal string ValueOf(Field field, string access) => field.Reach switch
        {
            Access.ArtworkSet => "ProjectArtworkSet(" + access + ")",
            Access.ExternalIdSet => "ProjectExternalIdSet(" + access + ")",
            Access.List => "ProjectList" + Number(_listIndex[ListKey(field)]) + "(" + access + ")",
            _ => Scalar(field.Kind, field.Element, access)
        };

        private string Scalar(int kind, ITypeSymbol element, string access) => kind switch
        {
            2 or 8 or 19 => ScalarName(kind) + "((long?)" + access + ")",
            3 or 9 => ScalarName(kind) + "((double?)" + access + ")",
            11 => "EnumeratedValue(TextOf" + Number(_enumIndex[TypeName(element)]) + "(" + access + "))",
            CompositeKind => "Composite" + Number(_compositeIndex[TypeName(element)]) + "(" + access + ")",
            _ => ScalarName(kind) + "(" + access + ")"
        };

        private static string ScalarName(int kind) => kind switch
        {
            0 => "TextValue",
            1 => "MultilineValue",
            2 => "IntegerValue",
            3 => "DecimalValue",
            4 => "BooleanValue",
            5 => "DateValue",
            6 => "InstantValue",
            7 => "DurationValue",
            8 => "ByteSizeValue",
            9 => "RatioValue",
            11 => "EnumeratedValue",
            13 => "ExternalValue",
            14 => "LinkValue",
            16 => "LanguageValue",
            18 => "ArtworkValue",
            19 => "CountValue",
            _ => "TextValue"
        };

        internal void EmitHelpers(StringBuilder source)
        {
            EmitScalarHelpers(source);
            EmitEnumHelpers(source);
            EmitCompositeHelpers(source);
            EmitListHelpers(source);
        }

        private void EmitScalarHelpers(StringBuilder source)
        {
            var declarations = new (string Name, string Parameter, string Kind, string Value)[]
            {
                ("TextValue", "string?", "Text", "OfText(value)"),
                ("MultilineValue", "string?", "MultilineText", "OfMultilineText(value)"),
                ("IntegerValue", "long?", "Integer", "OfInteger(value.Value)"),
                ("DecimalValue", "double?", "Decimal", "OfDecimal(value.Value)"),
                ("BooleanValue", "bool?", "Boolean", "OfBoolean(value.Value)"),
                ("DateValue", "global::System.DateOnly?", "Date", "OfDate(value.Value)"),
                ("InstantValue", "global::System.DateTimeOffset?", "Instant", "OfInstant(value.Value)"),
                ("DurationValue", "global::System.TimeSpan?", "Duration", "OfDuration(value.Value)"),
                ("ByteSizeValue", "long?", "ByteSize", "OfByteSize(value.Value)"),
                ("RatioValue", "double?", "Ratio", "OfRatio(value.Value)"),
                ("CountValue", "long?", "Count", "OfCount(value.Value)"),
                ("EnumeratedValue", "string?", "Enumerated", "OfEnumerated(value)"),
                ("LinkValue", "global::System.Uri?", "Link", "OfLink(value)"),
                ("ArtworkValue", "global::Arronix.Abstractions.Media.ArtworkImage?", "Artwork", "OfArtwork(value)"),
                ("LanguageValue", "global::Arronix.Abstractions.DTOs.Language?", "Language", "OfLanguage(value)"),
                ("ExternalValue", "global::Arronix.Abstractions.Shape.ExternalId?", "ExternalIdentifier", "OfExternalIdentifier(value.Value)"),
            };

            foreach (var declaration in declarations)
            {
                if (!_scalars.Contains(declaration.Name))
                {
                    continue;
                }

                source.AppendLine();
                source.Append("    private static global::Arronix.Abstractions.Shape.FieldValue ")
                    .Append(declaration.Name).Append('(').Append(declaration.Parameter).AppendLine(" value)");
                source.Append("        => value is null")
                    .AppendLine();
                source.Append("            ? global::Arronix.Abstractions.Shape.FieldValue.Absent(global::Arronix.Abstractions.Shape.FieldValueKind.")
                    .Append(declaration.Kind).AppendLine(")");
                source.Append("            : global::Arronix.Abstractions.Shape.FieldValue.")
                    .Append(declaration.Value).AppendLine(";");
            }

            if (_scalars.Contains("ProjectArtworkSet"))
            {
                source.AppendLine();
                source.AppendLine("    private static global::Arronix.Abstractions.Shape.FieldValue ProjectArtworkSet(global::Arronix.Abstractions.Media.ArtworkSet? value)");
                source.AppendLine("    {");
                source.AppendLine("        if (value is null)");
                source.AppendLine("        {");
                source.AppendLine("            return global::Arronix.Abstractions.Shape.FieldValue.Absent(global::Arronix.Abstractions.Shape.FieldValueKind.Artwork);");
                source.AppendLine("        }");
                source.AppendLine();
                source.AppendLine("        var items = new global::Arronix.Abstractions.Shape.FieldValue[value.Images.Count];");
                source.AppendLine("        for (var index = 0; index < items.Length; index++)");
                source.AppendLine("        {");
                source.AppendLine("            items[index] = ArtworkValue(value.Images[index]);");
                source.AppendLine("        }");
                source.AppendLine();
                source.AppendLine("        return global::Arronix.Abstractions.Shape.FieldValue.OfItems(global::Arronix.Abstractions.Shape.FieldValueKind.Artwork, items);");
                source.AppendLine("    }");
            }

            if (_scalars.Contains("ProjectExternalIdSet"))
            {
                source.AppendLine();
                source.AppendLine("    private static global::Arronix.Abstractions.Shape.FieldValue ProjectExternalIdSet(global::Arronix.Abstractions.Media.ExternalIdSet? value)");
                source.AppendLine("    {");
                source.AppendLine("        if (value is null)");
                source.AppendLine("        {");
                source.AppendLine("            return global::Arronix.Abstractions.Shape.FieldValue.Absent(global::Arronix.Abstractions.Shape.FieldValueKind.ExternalIdentifier);");
                source.AppendLine("        }");
                source.AppendLine();
                source.AppendLine("        var items = new global::Arronix.Abstractions.Shape.FieldValue[value.Values.Count];");
                source.AppendLine("        for (var index = 0; index < items.Length; index++)");
                source.AppendLine("        {");
                source.AppendLine("            items[index] = ExternalValue(value.Values[index]);");
                source.AppendLine("        }");
                source.AppendLine();
                source.AppendLine("        return global::Arronix.Abstractions.Shape.FieldValue.OfItems(global::Arronix.Abstractions.Shape.FieldValueKind.ExternalIdentifier, items);");
                source.AppendLine("    }");
            }
        }

        private void EmitEnumHelpers(StringBuilder source)
        {
            for (var index = 0; index < _enums.Count; index++)
            {
                var enumeration = (INamedTypeSymbol)_enums[index];
                source.AppendLine();
                source.Append("    private static string? TextOf").Append(Number(index))
                    .Append('(').Append(TypeName(enumeration)).AppendLine("? value)");
                source.AppendLine("    {");
                source.AppendLine("        switch (value)");
                source.AppendLine("        {");

                foreach (var member in enumeration.GetMembers().OfType<IFieldSymbol>())
                {
                    if (member.HasConstantValue)
                    {
                        source.Append("            case ").Append(TypeName(enumeration)).Append('.')
                            .Append(member.Name).AppendLine(":");
                        source.Append("                return ").Append(Literal(Identifier(member.Name))).AppendLine(";");
                    }
                }

                source.AppendLine("            default:");
                source.AppendLine("                return null;");
                source.AppendLine("        }");
                source.AppendLine("    }");
            }
        }

        private void EmitCompositeHelpers(StringBuilder source)
        {
            for (var index = 0; index < _composites.Count; index++)
            {
                var composite = _composites[index];
                source.AppendLine();
                source.Append("    private static global::Arronix.Abstractions.Shape.FieldValue Composite")
                    .Append(Number(index)).Append('(').Append(TypeName(composite.Key)).AppendLine("? value)");
                source.AppendLine("    {");
                source.AppendLine("        if (value is null)");
                source.AppendLine("        {");
                source.AppendLine("            return global::Arronix.Abstractions.Shape.FieldValue.Absent(global::Arronix.Abstractions.Shape.FieldValueKind.Composite);");
                source.AppendLine("        }");
                source.AppendLine();
                source.Append("        return global::Arronix.Abstractions.Shape.FieldValue.OfComposite(new global::Arronix.Abstractions.Shape.FieldValue[]")
                    .AppendLine();
                source.AppendLine("        {");

                var reader = composite.Key.IsValueType ? "value.Value." : "value.";

                foreach (var component in composite.Value)
                {
                    source.Append("            ").Append(ValueOf(component, reader + component.Property.Name)).AppendLine(",");
                }

                source.AppendLine("        });");
                source.AppendLine("    }");
            }
        }

        private void EmitListHelpers(StringBuilder source)
        {
            for (var index = 0; index < _lists.Count; index++)
            {
                var field = _lists[index];
                var element = TypeName(field.Element);
                var nullableElement = IsNullable(UnwrapList(field.Property.Type)!) ? element + "?" : element;

                source.AppendLine();
                source.Append("    private static global::Arronix.Abstractions.Shape.FieldValue ProjectList")
                    .Append(Number(index))
                    .Append("(global::System.Collections.Generic.IReadOnlyList<").Append(nullableElement).AppendLine(">? values)");
                source.AppendLine("    {");
                source.AppendLine("        if (values is null)");
                source.AppendLine("        {");
                source.Append("            return global::Arronix.Abstractions.Shape.FieldValue.Absent((global::Arronix.Abstractions.Shape.FieldValueKind)")
                    .Append(Number(field.Kind)).AppendLine(");");
                source.AppendLine("        }");
                source.AppendLine();
                source.AppendLine("        var items = new global::Arronix.Abstractions.Shape.FieldValue[values.Count];");
                source.AppendLine("        for (var index = 0; index < items.Length; index++)");
                source.AppendLine("        {");
                source.Append("            items[index] = ").Append(Scalar(field.Kind, field.Element, "values[index]")).AppendLine(";");
                source.AppendLine("        }");
                source.AppendLine();
                source.Append("        return global::Arronix.Abstractions.Shape.FieldValue.OfItems((global::Arronix.Abstractions.Shape.FieldValueKind)")
                    .Append(Number(field.Kind)).AppendLine(", items);");
                source.AppendLine("    }");
            }
        }
    }
}
