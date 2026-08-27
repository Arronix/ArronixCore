using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Arronix.Abstractions.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Arronix.Client.Tests.Contracts;

/// <summary>How a compiled contract fixture misbehaves once the runtime has it.</summary>
internal enum Misbehaviour
{
    /// <summary>None: a well-formed declaration with a one-field schema.</summary>
    None,

    /// <summary>Its constructor throws, so reading the declaration at all fails.</summary>
    ThrowingConstructor,

    /// <summary>Its schema getter throws.</summary>
    ThrowingSchema,

    /// <summary>Its schema is null, which is not a schema.</summary>
    NullSchema,

    /// <summary>Its schema is empty, which is a schema. Valid, and here to prove it stays valid.</summary>
    EmptySchema,
}

/// <summary>
/// Compiles contract assemblies whose declarations misbehave after they are loaded.
/// </summary>
/// <remarks>
/// Every fixture here passes preflight — the bytes are exactly what they say they are — and fails only once
/// the runtime has been handed them, which is the one place a browser cannot take anything back. Each is
/// compiled under its own assembly name because a load context cannot be unloaded.
/// </remarks>
internal static class CompiledContract
{
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    /// <summary>Compiles one fixture and returns its bytes.</summary>
    /// <param name="assemblyName">The assembly name, unique per fixture.</param>
    /// <param name="misbehaviour">What its declaration does once loaded.</param>
    /// <param name="declaring">
    /// Whether the assembly declares a client contract at all. False is a shared assembly that owns no item
    /// — a format's representation vocabulary is one — which is a valid payload rather than a defective one.
    /// </param>
    /// <returns>The compiled assembly image.</returns>
    public static byte[] Image(string assemblyName, Misbehaviour misbehaviour, bool declaring = true)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);

        var application = "[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]\n"
            + (declaring
                ? "[assembly: global::Fixture.Client.Entry(typeof(global::Fixture.Client.Entity), \""
                    + new string('A', 64) + "\", \"" + new string('B', 64) + "\")]"
                : string.Empty);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(Declaration(misbehaviour), parse, "Declaration.cs"),
                CSharpSyntaxTree.ParseText(application, parse, "Application.cs"),
            ],
            References,
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

        var schema = misbehaviour switch
        {
            Misbehaviour.ThrowingSchema =>
                "throw new System.InvalidOperationException(\"the schema refuses to be read\");",
            Misbehaviour.NullSchema => "null!;",
            Misbehaviour.EmptySchema => "[];",
            _ => """
                [
                    new global::Arronix.Abstractions.Shape.FieldDescriptor
                    {
                        FieldId = "title",
                        Name = "Title",
                        ValueKind = global::Arronix.Abstractions.Shape.FieldValueKind.Text,
                    },
                ];
                """,
        };

        return $$"""
            namespace Fixture.Client;

            public sealed class Entity { }

            public sealed class Entry : global::Arronix.Abstractions.Client.ClientContractEntryPointAttribute
            {
                public Entry(System.Type entityType, string one, string two) : base(entityType, one, two)
                {
                    {{constructorBody}}
                }

                public override System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Schema =>
                    {{schema}}

                public override object Deserialize(System.ReadOnlySpan<byte> utf8Json) =>
                    throw new System.NotSupportedException();

                public override byte[] Serialize(object entity) => throw new System.NotSupportedException();

                public override global::Arronix.Abstractions.Client.ProjectedEntity Project(object entity) =>
                    throw new System.NotSupportedException();
            }
            """;
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
