using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Arronix.Client.Tests.Contracts;

/// <summary>How a compiled contract fixture misbehaves once the runtime has it.</summary>
internal enum Misbehaviour
{
    /// <summary>None: a coherent declaration whose declared hashes are the ones it hashes to.</summary>
    None,

    /// <summary>Its constructor throws.</summary>
    ThrowingConstructor,

    /// <summary>Its serialization context getter throws.</summary>
    ThrowingContext,

    /// <summary>Its entity metadata getter throws.</summary>
    ThrowingEntityTypeInfo,

    /// <summary>Its schema getter throws.</summary>
    ThrowingSchema,

    /// <summary>Its schema is null, which is not a schema.</summary>
    NullSchema,

    /// <summary>Its entity metadata is not the metadata its own context holds.</summary>
    IncoherentRoot,

    /// <summary>Its entity metadata answers for a type other than its entity.</summary>
    ForeignRoot,

    /// <summary>Its context is declared by a genuinely separate assembly.</summary>
    ForeignContext,

    /// <summary>It derives from the declaration through an intermediate type.</summary>
    IndirectBase,

    /// <summary>Its declared hashes are not the ones it hashes to.</summary>
    DigestMismatch,

    /// <summary>Its entity metadata getter answers with a new object each time.</summary>
    UnstableEntityTypeInfo,

    /// <summary>Its schema nests deeper than anything can walk.</summary>
    DeepSchema,

    /// <summary>Its schema contains itself.</summary>
    CyclicSchema,

    /// <summary>Its schema reports more values than anything will hold, and produces none of them.</summary>
    WideSchema,

    /// <summary>
    /// Its schema is two lists, each within the node budget and together one over it.
    /// </summary>
    AggregateSchema,

    /// <summary>
    /// Its schema is coherent and its root list answers with a different field every time it is read.
    /// </summary>
    SteppingSchema,

    /// <summary>
    /// Its schema's root objects are stable, and their components and choices answer with something
    /// different every time they are read.
    /// </summary>
    SteppingNestedSchema,

    /// <summary>It reads a payload into its own entity and projects it, which is the control.</summary>
    PayloadReadable,

    /// <summary>It reads a payload into a type that is not the entity it declared.</summary>
    PayloadForeignType,

    /// <summary>It reads a payload into nothing.</summary>
    PayloadNullEntity,

    /// <summary>Its projection throws.</summary>
    PayloadThrowingProject,

    /// <summary>Its projection cancels something of its own, with nobody having asked it to.</summary>
    PayloadCancelingProject,

    /// <summary>Its projection names a type that is not the entity it declared.</summary>
    PayloadForeignProjectedType,
}

/// <summary>
/// Compiles contract assemblies whose declarations misbehave after they are loaded.
/// </summary>
/// <remarks>
/// Every fixture passes preflight — its bytes are exactly what they say they are — and fails, when it does,
/// only once the runtime holds it. A coherent fixture's declared hashes are computed by compiling it once,
/// reading them off a collectible copy, and compiling it again with the answer.
/// </remarks>
internal static class CompiledContract
{
    private const string Placeholder = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    /// <summary>The assembly name of the companion that declares a genuinely foreign context.</summary>
    internal const string AuxiliaryName = "Fixture.Client.Aux";

    /// <summary>One fixture: the payload, and the separate assembly it binds to when it has one.</summary>
    internal sealed record Fixture(byte[] Payload, byte[]? Auxiliary);

    /// <summary>Compiles one fixture.</summary>
    internal static Fixture Build(string assemblyName, Misbehaviour misbehaviour)
    {
        var auxiliary = misbehaviour == Misbehaviour.ForeignContext ? CompileAuxiliary() : null;
        var first = Compile(assemblyName, misbehaviour, Placeholder, Placeholder, auxiliary);

        if (!Hashable(misbehaviour))
        {
            return new Fixture(first, auxiliary);
        }

        var (serialization, projection) = Hashes(assemblyName, first, Walkable(misbehaviour));

        return new Fixture(
            misbehaviour == Misbehaviour.DigestMismatch
                ? Compile(assemblyName, misbehaviour, Flip(serialization), projection, auxiliary)
                : Compile(assemblyName, misbehaviour, serialization, projection, auxiliary),
            auxiliary);
    }

    /// <summary>
    /// Compiles the companion assembly whose context the foreign case returns.
    /// </summary>
    /// <remarks>
    /// A separate assembly, because the guard compares the context type's assembly against the payload's. A
    /// second namespace or a second file in one compilation is the same assembly and proves nothing.
    /// </remarks>
    private static byte[] CompileAuxiliary()
    {
        var compilation = CSharpCompilation.Create(
            AuxiliaryName,
            [
                CSharpSyntaxTree.ParseText(
                    """
                    namespace Fixture.Client.Aux;

                    public sealed class ForeignContext : System.Text.Json.Serialization.JsonSerializerContext
                    {
                        public static ForeignContext Instance { get; } = new();

                        private ForeignContext() : base(new System.Text.Json.JsonSerializerOptions()) { }

                        protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => Options;

                        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) => null;
                    }
                    """,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    "Auxiliary.cs"),
            ],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        return result.Success
            ? stream.ToArray()
            : throw new InvalidOperationException(
                "The auxiliary fixture did not compile:" + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    result.Diagnostics
                        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(static diagnostic => diagnostic.ToString())));
    }

    /// <summary>Whether a fixture is coherent enough for its serialization hash to be readable.</summary>
    /// <remarks>
    /// The schema-shape cases are included so their wire hash is the right one: their only defect is then
    /// the schema, and the guard they fail at is the only guard they could fail at.
    /// </remarks>
    private static bool Hashable(Misbehaviour misbehaviour)
        => misbehaviour is Misbehaviour.None or Misbehaviour.DigestMismatch
            or Misbehaviour.DeepSchema or Misbehaviour.CyclicSchema or Misbehaviour.WideSchema
            or Misbehaviour.AggregateSchema or Misbehaviour.SteppingSchema
            or Misbehaviour.SteppingNestedSchema or Misbehaviour.PayloadReadable
            or Misbehaviour.PayloadForeignType or Misbehaviour.PayloadNullEntity
            or Misbehaviour.PayloadThrowingProject or Misbehaviour.PayloadCancelingProject
            or Misbehaviour.PayloadForeignProjectedType;

    /// <summary>Whether a fixture's schema can be walked at all, and so hashed.</summary>
    private static bool Walkable(Misbehaviour misbehaviour)
        => misbehaviour is not (Misbehaviour.DeepSchema or Misbehaviour.CyclicSchema
            or Misbehaviour.WideSchema or Misbehaviour.AggregateSchema);

    /// <summary>Reads a compiled fixture's own hashes off a copy nothing else will ever see.</summary>
    private static (string Serialization, string Projection) Hashes(
        string assemblyName,
        byte[] image,
        bool walkable)
    {
        var context = new AssemblyLoadContext(assemblyName + ".hashes", isCollectible: true);

        try
        {
            var declaration = context
                .LoadFromStream(new MemoryStream(image, writable: false))
                .GetCustomAttributes<ClientContractEntryPointAttribute>()
                .Single();

            return (
                ClientContractDigest.OfSerialization(declaration.SerializationContext, declaration.EntityTypeInfo),
                walkable
                    ? ClientContractDigest.OfProjection(declaration.EntityType, declaration.Schema)
                    : Placeholder);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Flip(string hash) => (hash[0] == 'A' ? "B" : "A") + hash[1..];

    /// <summary>Compiles one lifecycle fixture and returns its bytes.</summary>
    /// <param name="assemblyName">The assembly name, unique per fixture.</param>
    /// <param name="misbehaviour">What its declaration does once loaded.</param>
    /// <param name="declaring">
    /// Whether the assembly declares a client contract at all. False represents a shared assembly that
    /// owns no media item, which is a valid payload rather than a defective declaration.
    /// </param>
    /// <returns>The compiled assembly image.</returns>
    internal static byte[] Image(string assemblyName, Misbehaviour misbehaviour, bool declaring = true)
        => declaring
            ? Build(assemblyName, misbehaviour).Payload
            : Compile(assemblyName, misbehaviour, Placeholder, Placeholder, null, declaring: false);

    private static byte[] Compile(
        string assemblyName,
        Misbehaviour misbehaviour,
        string serialization,
        string projection,
        byte[]? auxiliary,
        bool declaring = true)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);

        var application = "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]\n"
            + (declaring
                ? "[assembly: global::Fixture.Client.Entry(typeof(global::Fixture.Client.Entity), \""
                    + serialization + "\", \"" + projection + "\")]"
                : string.Empty);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(Declaration(misbehaviour), parse, "Declaration.cs"),
                CSharpSyntaxTree.ParseText(application, parse, "Application.cs"),
            ],
            auxiliary is null
                ? References
                : References.Add(MetadataReference.CreateFromImage(auxiliary)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"The '{assemblyName}' contract fixture did not compile:{Environment.NewLine}"
                + string.Join(
                    Environment.NewLine,
                    result.Diagnostics
                        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(static diagnostic => diagnostic.ToString())));
        }

        return stream.ToArray();
    }

    private static string Declaration(Misbehaviour misbehaviour)
    {
        var constructorBody = misbehaviour == Misbehaviour.ThrowingConstructor
            ? "throw new System.InvalidOperationException(\"the declaration refuses to describe itself\");"
            : string.Empty;

        var context = misbehaviour switch
        {
            Misbehaviour.ThrowingContext =>
                "throw new System.InvalidOperationException(\"the context refuses to be read\");",
            Misbehaviour.ForeignContext => "global::Fixture.Client.Aux.ForeignContext.Instance;",
            _ => "FixtureContext.Instance;",
        };

        var root = misbehaviour switch
        {
            Misbehaviour.ThrowingEntityTypeInfo =>
                "throw new System.InvalidOperationException(\"the entity metadata refuses to be read\");",
            Misbehaviour.IncoherentRoot => "FixtureContext.Detached;",
            Misbehaviour.ForeignRoot => "FixtureContext.Instance.OtherInfo;",
            Misbehaviour.UnstableEntityTypeInfo => "FixtureContext.Fresh();",
            _ => "FixtureContext.Instance.EntityInfo;",
        };

        var schema = misbehaviour switch
        {
            Misbehaviour.ThrowingSchema =>
                "throw new System.InvalidOperationException(\"the schema refuses to be read\");",
            Misbehaviour.NullSchema => "null!;",
            Misbehaviour.DeepSchema => "Schemas.Deep;",
            Misbehaviour.CyclicSchema => "Schemas.Cyclic;",
            Misbehaviour.WideSchema => "Schemas.Wide;",
            Misbehaviour.AggregateSchema => "Schemas.Aggregate;",
            Misbehaviour.SteppingSchema => "Schemas.Stepping;",
            Misbehaviour.SteppingNestedSchema => "Schemas.LiveNested;",
            _ => "[];",
        };

        // The payload cases read nothing out of the bytes: what is under test is what a declaration hands
        // back, and the real read is proved against the shipped movies contract and its own fixture.
        var read = misbehaviour switch
        {
            Misbehaviour.PayloadForeignType => "new Other();",
            Misbehaviour.PayloadNullEntity => "null!;",
            Misbehaviour.PayloadReadable or Misbehaviour.PayloadThrowingProject
                or Misbehaviour.PayloadCancelingProject or Misbehaviour.PayloadForeignProjectedType =>
                "new Entity();",
            _ => "throw new System.NotSupportedException();",
        };

        var project = misbehaviour switch
        {
            Misbehaviour.PayloadThrowingProject =>
                "throw new System.InvalidOperationException(\"the projection refuses to run\");",
            Misbehaviour.PayloadCancelingProject => "throw new System.OperationCanceledException();",
            Misbehaviour.PayloadForeignProjectedType =>
                "new global::Arronix.Abstractions.Client.ProjectedEntity(typeof(Other), []);",
            Misbehaviour.PayloadReadable =>
                "new global::Arronix.Abstractions.Client.ProjectedEntity(typeof(Entity), []);",
            _ => "throw new System.NotSupportedException();",
        };

        var baseType = misbehaviour == Misbehaviour.IndirectBase
            ? "global::Fixture.Client.Intermediate"
            : "global::Arronix.Abstractions.Client.ClientContractEntryPointAttribute";

        var intermediate = misbehaviour == Misbehaviour.IndirectBase
            ? """
                public abstract class Intermediate : global::Arronix.Abstractions.Client.ClientContractEntryPointAttribute
                {
                    protected Intermediate(System.Type entityType, string one, string two)
                        : base(entityType, one, two) { }
                }
                """
            : string.Empty;

        return $$"""
            namespace Fixture.Client;

            public sealed class Entity { }

            public sealed class Other { }

            internal sealed class FixtureContext : System.Text.Json.Serialization.JsonSerializerContext
            {
                private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo _entity;
                private readonly System.Text.Json.Serialization.Metadata.JsonTypeInfo _other;

                internal static FixtureContext Instance { get; } = new();

                private FixtureContext()
                    : base(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Strict)
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    })
                {
                    _entity = Make<Entity>(Options, this);
                    _other = Make<Other>(Options, this);
                }

                internal System.Text.Json.Serialization.Metadata.JsonTypeInfo EntityInfo => _entity;

                internal System.Text.Json.Serialization.Metadata.JsonTypeInfo OtherInfo => _other;

                /// <summary>Metadata for the entity that this context does not hold.</summary>
                internal static System.Text.Json.Serialization.Metadata.JsonTypeInfo Detached { get; } =
                    Make<Entity>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Strict)
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    });

                /// <summary>A new object for the same type on every call.</summary>
                internal static System.Text.Json.Serialization.Metadata.JsonTypeInfo Fresh() =>
                    Make<Entity>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Strict)
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    });

                protected override System.Text.Json.JsonSerializerOptions? GeneratedSerializerOptions => Options;

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(System.Type type) =>
                    type == typeof(Entity) ? _entity : type == typeof(Other) ? _other : null;

                private static System.Text.Json.Serialization.Metadata.JsonTypeInfo Make<T>(
                    System.Text.Json.JsonSerializerOptions options,
                    System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? resolver = null)
                {
                    var info = System.Text.Json.Serialization.Metadata.JsonTypeInfo.CreateJsonTypeInfo<T>(options);
                    info.CreateObject = () => System.Activator.CreateInstance<T>()!;
                    if (resolver is not null) { info.OriginatingResolver = resolver; }
                    info.MakeReadOnly();
                    return info;
                }
            }

            {{intermediate}}

            internal static class Schemas
            {
                private static global::Arronix.Abstractions.Shape.FieldDescriptor Field(
                    string id,
                    System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> parts) =>
                    new()
                    {
                        FieldId = id,
                        Name = id,
                        ValueKind = global::Arronix.Abstractions.Shape.FieldValueKind.Composite,
                        Components = parts,
                    };

                /// <summary>A chain far deeper than any real shape.</summary>
                internal static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Deep { get; } = Build();

                /// <summary>A field whose components answer with the field itself.</summary>
                internal static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Cyclic { get; } = Loop();

                /// <summary>A list that claims more entries than could be held and produces none.</summary>
                internal static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Wide { get; } = new Vast();

                /// <summary>A coherent schema whose root list answers differently every time it is read.</summary>
                internal static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Stepping { get; } = new Stepper();

                /// <summary>
                /// Stable root objects whose own lists answer differently every time they are read. The
                /// roots never move, so capturing them is not enough: what they name has to be captured too.
                /// </summary>
                internal static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> LiveNested { get; } =
                    new[]
                    {
                        new global::Arronix.Abstractions.Shape.FieldDescriptor
                        {
                            FieldId = "certification",
                            Name = "Certification",
                            ValueKind = global::Arronix.Abstractions.Shape.FieldValueKind.Composite,
                            Components = new SteppingComponents(),
                        },
                        new global::Arronix.Abstractions.Shape.FieldDescriptor
                        {
                            FieldId = "status",
                            Name = "Status",
                            ValueKind = global::Arronix.Abstractions.Shape.FieldValueKind.Enumerated,
                            Choices = new SteppingChoices(),
                        },
                    };

                /// <summary>One root, whose children are exactly the whole budget: together, one too many.</summary>
                internal static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Aggregate { get; } =
                    new[] { Field("root", new Budget()) };

                private static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Build()
                {
                    var current = Field("leaf", []);
                    for (var depth = 0; depth < 200; depth++)
                    {
                        current = Field("level" + depth, new[] { current });
                    }

                    return new[] { current };
                }

                private static System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Loop()
                {
                    var parts = new Knot();
                    var field = Field("knot", parts);
                    parts.Self = field;
                    return new[] { field };
                }

                private static global::Arronix.Abstractions.Shape.FieldDescriptor Leaf(string id) =>
                    new()
                    {
                        FieldId = id,
                        Name = id,
                        ValueKind = global::Arronix.Abstractions.Shape.FieldValueKind.Text,
                    };

                /// <summary>One component the first time it is read, and another every time after.</summary>
                private sealed class SteppingComponents : System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor>
                {
                    private static readonly global::Arronix.Abstractions.Shape.FieldDescriptor First = Leaf("region");

                    private static readonly global::Arronix.Abstractions.Shape.FieldDescriptor Rest = Leaf("swapped");

                    private int _reads;

                    public global::Arronix.Abstractions.Shape.FieldDescriptor this[int index] =>
                        _reads++ == 0 ? First : Rest;

                    public int Count => 1;

                    public System.Collections.Generic.IEnumerator<global::Arronix.Abstractions.Shape.FieldDescriptor> GetEnumerator()
                    {
                        yield return this[0];
                    }

                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                }

                /// <summary>One choice the first time it is read, and another every time after.</summary>
                private sealed class SteppingChoices : System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FacetValue>
                {
                    private int _reads;

                    public global::Arronix.Abstractions.Shape.FacetValue this[int index] =>
                        _reads++ == 0
                            ? new global::Arronix.Abstractions.Shape.FacetValue("released", "Released")
                            : new global::Arronix.Abstractions.Shape.FacetValue("smuggled", "Smuggled");

                    public int Count => 1;

                    public System.Collections.Generic.IEnumerator<global::Arronix.Abstractions.Shape.FacetValue> GetEnumerator()
                    {
                        yield return this[0];
                    }

                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                }

                /// <summary>
                /// One composite the first time it is read, and a composite whose component is named
                /// something else every time after. Both are describable; they are not the same schema.
                /// </summary>
                private sealed class Stepper : System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor>
                {
                    private static readonly global::Arronix.Abstractions.Shape.FieldDescriptor First =
                        Field("certification", new[] { Leaf("region") });

                    private static readonly global::Arronix.Abstractions.Shape.FieldDescriptor Rest =
                        Field("certification", new[] { Leaf("swapped") });

                    private int _reads;

                    public global::Arronix.Abstractions.Shape.FieldDescriptor this[int index] =>
                        _reads++ == 0 ? First : Rest;

                    public int Count => 1;

                    public System.Collections.Generic.IEnumerator<global::Arronix.Abstractions.Shape.FieldDescriptor> GetEnumerator()
                    {
                        yield return this[0];
                    }

                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                }

                /// <summary>Reports int.MaxValue entries; indexing one is a failure the walker must avoid.</summary>
                private sealed class Vast : System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor>
                {
                    public global::Arronix.Abstractions.Shape.FieldDescriptor this[int index] =>
                        throw new System.InvalidOperationException("the walker asked for an entry it was told not to expect");

                    public int Count => int.MaxValue;

                    public System.Collections.Generic.IEnumerator<global::Arronix.Abstractions.Shape.FieldDescriptor> GetEnumerator() =>
                        throw new System.InvalidOperationException("the walker enumerated a list it was told not to expect");

                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                }

                /// <summary>
                /// Exactly the node budget, every entry the same harmless leaf. Within any per-list cap.
                /// </summary>
                private sealed class Budget : System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor>
                {
                    private static readonly global::Arronix.Abstractions.Shape.FieldDescriptor Leaf = new()
                    {
                        FieldId = "leaf",
                        Name = "leaf",
                        ValueKind = global::Arronix.Abstractions.Shape.FieldValueKind.Text,
                    };

                    public global::Arronix.Abstractions.Shape.FieldDescriptor this[int index] => Leaf;

                    public int Count => __MAXNODES__;

                    public System.Collections.Generic.IEnumerator<global::Arronix.Abstractions.Shape.FieldDescriptor> GetEnumerator()
                    {
                        for (var index = 0; index < Count; index++)
                        {
                            yield return Leaf;
                        }
                    }

                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                }

                private sealed class Knot : System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor>
                {
                    internal global::Arronix.Abstractions.Shape.FieldDescriptor? Self { get; set; }

                    public global::Arronix.Abstractions.Shape.FieldDescriptor this[int index] => Self!;

                    public int Count => 1;

                    public System.Collections.Generic.IEnumerator<global::Arronix.Abstractions.Shape.FieldDescriptor> GetEnumerator()
                    {
                        yield return Self!;
                    }

                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                }
            }

            public sealed class Entry : {{baseType}}
            {
                public Entry(System.Type entityType, string one, string two) : base(entityType, one, two)
                {
                    {{constructorBody}}
                }

                public override System.Text.Json.Serialization.JsonSerializerContext SerializationContext =>
                    {{context}}

                public override System.Text.Json.Serialization.Metadata.JsonTypeInfo EntityTypeInfo =>
                    {{root}}

                public override System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Schema =>
                    {{schema}}

                public override object Deserialize(System.ReadOnlySpan<byte> utf8Json) =>
                    {{read}}

                public override byte[] Serialize(object entity) => throw new System.NotSupportedException();

                public override global::Arronix.Abstractions.Client.ProjectedEntity Project(object entity) =>
                    {{project}}
            }

            """.Replace("__MAXNODES__", ClientContractLimits.MaxNodes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not expose its trusted platform assemblies.");

        return
        [
            .. trusted
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Append(typeof(ClientContractEntryPointAttribute).Assembly.Location)
                .Distinct(StringComparer.Ordinal)
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ];
    }
}
