using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Arronix.Abstractions.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// Compiles assemblies carrying client contract declarations, including ones no author would write.
/// </summary>
/// <remarks>
/// <para>
/// Compiled rather than emitted, and that is a measurement rather than a preference.
/// <c>PersistedAssemblyBuilder</c> cannot reference a constructor it defined itself from an assembly-level
/// attribute: the token it records resolves to no row in the method table, so the attribute is unreadable
/// for a reason that has nothing to do with what it says. A fixture that fails to parse proves nothing
/// about a rule that decides what a parsed declaration is allowed to claim.
/// </para>
/// <para>
/// Every fixture here is therefore valid metadata a compiler produced. That is the whole point: each is a
/// different way of not being the shape the platform defined while looking exactly like it, and a reader
/// written against the generator's output accepts all of them.
/// </para>
/// </remarks>
internal static class CompiledDeclaration
{
    /// <summary>The entity type every fixture assembly defines.</summary>
    public const string EntityTypeName = "Fixture.Declared.Entity";

    /// <summary>A hash of the shape the protocol requires.</summary>
    public const string ValidHash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    /// <summary>An entity expression naming a type the fixture assembly does not define.</summary>
    public const string ForeignEntity = "typeof(string)";

    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    /// <summary>How a declaration's base type is reached, which is what identity checking is about.</summary>
    internal enum Base
    {
        /// <summary>The platform's declaration, referenced from this host's own contract assembly.</summary>
        Platform,

        /// <summary>A same-named attribute the compiled assembly declares itself.</summary>
        LocallyDeclared,
    }

    /// <summary>What a declaration's constructor takes, which decides whether its blob can be trusted.</summary>
    internal enum Signature
    {
        /// <summary>The declared shape: an entity type and two hashes.</summary>
        TypeAndTwoStrings,

        /// <summary>Three strings, which decodes cleanly and type-checks nothing.</summary>
        ThreeStrings,
    }

    /// <summary>One declaration to compile into an assembly.</summary>
    /// <param name="TypeName">The declaration attribute's own simple name, under <c>Fixture.Declared</c>.</param>
    /// <param name="Entity">The expression naming the entity type, as C# source.</param>
    /// <param name="MetadataHash">The generated-metadata hash the blob carries.</param>
    /// <param name="ProjectionHash">The projection-schema hash the blob carries.</param>
    /// <param name="Takes">What its constructor takes.</param>
    internal sealed record Declared(
        string TypeName,
        string Entity = "typeof(" + EntityTypeName + ")",
        string MetadataHash = ValidHash,
        string ProjectionHash = ValidHash,
        Signature Takes = Signature.TypeAndTwoStrings);

    /// <summary>Compiles an assembly carrying the declarations described.</summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="assemblyName">The assembly name, which is also its file name.</param>
    /// <param name="from">How the declarations reach their base type.</param>
    /// <param name="declarations">The declarations to compile.</param>
    /// <returns>The full path of the written assembly.</returns>
    public static string Write(
        string folder,
        string assemblyName,
        Base from,
        params Declared[] declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var parse = new CSharpParseOptions(LanguageVersion.Latest);

        // Two trees, because assembly attributes must precede every other element in their own file. The
        // types they name are compiled from the other one.
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [
                CSharpSyntaxTree.ParseText(Declarations(from, declarations), parse, "Declarations.cs"),
                CSharpSyntaxTree.ParseText(Applications(declarations), parse, "Applications.cs"),
            ],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, assemblyName + ".dll");
        var result = compilation.Emit(path);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"The '{assemblyName}' declaration fixture did not compile:{Environment.NewLine}"
                + string.Join(
                    Environment.NewLine,
                    result.Diagnostics
                        .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(static diagnostic => diagnostic.ToString())));
        }

        return path;
    }

    private static string Applications(IReadOnlyList<Declared> declarations)
    {
        var source = new StringBuilder();

        // A shared contract binds by exact CLR identity, so admission refuses one declaring no version. A
        // fixture without this is refused before any declaration is looked at.
        source.AppendLine("[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]");

        foreach (var declaration in declarations)
        {
            source.Append("[assembly: global::Fixture.Declared.").Append(declaration.TypeName).Append('(')
                .Append(declaration.Takes == Signature.TypeAndTwoStrings
                    ? declaration.Entity
                    : "\"" + EntityTypeName + "\"")
                .Append(", \"").Append(declaration.MetadataHash)
                .Append("\", \"").Append(declaration.ProjectionHash).AppendLine("\")]");
        }

        return source.ToString();
    }

    private static string Declarations(Base from, IReadOnlyList<Declared> declarations)
    {
        var source = new StringBuilder();
        source.AppendLine("namespace Fixture.Declared { public sealed class Entity { } }");

        if (from == Base.LocallyDeclared)
        {
            // The impostor. A namespace and a type name cost nothing to spell, and C# resolves the source
            // declaration ahead of the referenced one, so everything below derives from this instead.
            source.AppendLine("""
                namespace Arronix.Abstractions.Client
                {
                    public class ClientContractEntryPointAttribute : System.Attribute
                    {
                        protected ClientContractEntryPointAttribute(System.Type entityType, string one, string two)
                        {
                            _ = entityType;
                            _ = one;
                            _ = two;
                        }
                    }
                }
                """);
        }

        foreach (var declaration in declarations)
        {
            source.Append("namespace Fixture.Declared { public sealed class ").Append(declaration.TypeName)
                .AppendLine(" : global::Arronix.Abstractions.Client.ClientContractEntryPointAttribute {");

            source.Append("  public ").Append(declaration.TypeName)
                .AppendLine(declaration.Takes == Signature.TypeAndTwoStrings
                    ? "(System.Type entityType, string one, string two) : base(entityType, one, two) { }"
                    : "(string entityType, string one, string two) : base(typeof(object), one, two) { _ = entityType; }");

            if (from == Base.Platform)
            {
                // Never called. The declaration under test is metadata; the behaviour behind it is a
                // separate question this fixture deliberately does not answer.
                source.AppendLine("""
                      public override System.Text.Json.Serialization.JsonSerializerContext SerializationContext
                          => throw new System.NotSupportedException();
                      public override System.Text.Json.Serialization.Metadata.JsonTypeInfo EntityTypeInfo
                          => throw new System.NotSupportedException();
                      public override System.Collections.Generic.IReadOnlyList<global::Arronix.Abstractions.Shape.FieldDescriptor> Schema
                          => throw new System.NotSupportedException();
                      public override object Deserialize(System.ReadOnlySpan<byte> utf8Json)
                          => throw new System.NotSupportedException();
                      public override byte[] Serialize(object entity) => throw new System.NotSupportedException();
                      public override global::Arronix.Abstractions.Client.ProjectedEntity Project(object entity)
                          => throw new System.NotSupportedException();
                    """);
            }

            source.AppendLine("} }");
        }

        return source.ToString();
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
