using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Arronix.Abstractions.Client;

namespace Arronix.Plugin.Movies.Tests.ClientContract;

/// <summary>
/// The declaration a client-safe assembly publishes, read from its bytes and read after it is loaded.
/// </summary>
/// <remarks>
/// <para>
/// The reason every fact is a constructor argument rather than an overridden property is here. A browser
/// decides whether it may project a payload <i>before</i> the runtime is handed the assembly, and a host
/// publishes what it admitted without calling into the package. Both are only possible for values that
/// live in the custom attribute blob: an overridden property is executable code, so reading it means
/// having already loaded the assembly it was supposed to vouch for.
/// </para>
/// <para>
/// So the same facts are decoded from the raw bytes with a structured metadata reader, and compared with
/// what the runtime produced after the assembly was loaded. The entity is carried as a type reference
/// rather than as a name beside a name: the reader decodes the reference, the runtime resolves it, and the
/// two are then the same fact read twice rather than two facts that can disagree.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ClientContractDeclarationTests
{
    private const string DeclarationNamespace = "Arronix.Abstractions.Client";
    private const string DeclarationName = "ClientContractEntryPointAttribute";

    private static Assembly ContractAssembly => typeof(Movie).Assembly;

    /// <summary>
    /// Gets the complete CLR identity of the universal contract assembly this test process is running.
    /// </summary>
    /// <remarks>
    /// Read from the assembly the declaration's base type is actually declared in, rather than written
    /// down, so the value compared against is the one a consumer would bind to.
    /// </remarks>
    private static string DeclarationAssemblyIdentity { get; } =
        typeof(ClientContractEntryPointAttribute).Assembly.GetName().FullName;

    private static Declaration FromBytes { get; } =
        ReadFromBytes(File.ReadAllBytes(ContractAssembly.Location)).Single();

    private static ClientContractEntryPointAttribute Loaded { get; } =
        ContractAssembly.GetCustomAttributes<ClientContractEntryPointAttribute>().Single();

    [Test]
    public void TheDeclarationIsDecodableFromTheBytesWithoutLoadingThem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FromBytes.EntityTypeName, Is.EqualTo("Arronix.Media.Movies.Movie"));
            Assert.That(FromBytes.GeneratedMetadataHash, Does.Match("^[0-9A-F]{64}$"));
            Assert.That(FromBytes.ProjectionSchemaHash, Does.Match("^[0-9A-F]{64}$"));
            Assert.That(FromBytes.GeneratedMetadataHash, Is.Not.EqualTo(FromBytes.ProjectionSchemaHash));
        });
    }

    /// <remarks>
    /// One hop, deliberately. The reader identifies the declaration by walking the attribute's constructor
    /// to the type that declares it and comparing that type's own base to the universal contract's
    /// declaration. A deeper hierarchy would mean resolving a type the reader has not been given, which is
    /// the thing a preflight must not do.
    /// </remarks>
    [Test]
    public void TheGeneratedTypeDerivesDirectlyFromTheSharedDeclaration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FromBytes.BaseNamespace, Is.EqualTo(DeclarationNamespace));
            Assert.That(FromBytes.BaseName, Is.EqualTo(DeclarationName));
            Assert.That(FromBytes.BaseIsTypeReference, Is.True, "the base lives in another assembly");
            Assert.That(Loaded.GetType().BaseType, Is.EqualTo(typeof(ClientContractEntryPointAttribute)));
        });
    }

    /// <remarks>
    /// <para>
    /// A namespace and a name are not an identity. Anyone can declare
    /// <c>Arronix.Abstractions.Client.ClientContractEntryPointAttribute</c>, and a payload whose
    /// declaration derived from a private copy of it would satisfy a name comparison exactly while binding
    /// to a type the host never admitted — which is the failure a shared contract assembly exists to
    /// prevent, arriving through the one door a preflight opens.
    /// </para>
    /// <para>
    /// So the base type reference is followed to its resolution scope, that scope is required to be an
    /// assembly reference rather than a type in the payload itself or in one of its modules, and the
    /// complete identity it names — simple name, version, culture and public key token — is required to be
    /// this client's own universal contract identity. This is the byte-level form of G07.1's post-load
    /// rule that the loaded assembly's contract reference must resolve, by object identity, to the client's
    /// own <c>Arronix.Abstractions</c>.
    /// </para>
    /// </remarks>
    [Test]
    public void TheSharedDeclarationIsResolvedThroughTheExactUniversalContractIdentity()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                FromBytes.BaseResolutionScopeKind,
                Is.EqualTo(HandleKind.AssemblyReference),
                "the base type must come from a referenced assembly, not from the payload itself");

            Assert.That(
                FromBytes.BaseAssemblyIdentity,
                Is.EqualTo(DeclarationAssemblyIdentity),
                "the declaration must derive from this client's own universal contract, not from a "
                + "same-named type in some other assembly");

            Assert.That(
                FromBytes.BaseAssemblyIdentity,
                Does.StartWith("Arronix.Abstractions, Version="),
                "the identity is complete rather than a simple name");

            // The post-load half of the same rule: what the bytes named is what the runtime bound.
            Assert.That(
                Loaded.GetType().BaseType!.Assembly,
                Is.SameAs(typeof(ClientContractEntryPointAttribute).Assembly));
        });
    }

    [Test]
    public void TheDecodedTypeReferenceIsTheTypeTheRuntimeResolvedAfterLoading()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FromBytes.EntityTypeName, Is.EqualTo(Loaded.EntityType.FullName));
            Assert.That(Loaded.EntityType, Is.SameAs(typeof(Movie)));
            Assert.That(Loaded.EntityType.Assembly, Is.SameAs(ContractAssembly));
        });
    }

    /// <remarks>
    /// The implementation is internal, and reaching it by exact base type is what makes that possible. A
    /// consumer finds the declaration through the one assembly both sides already share, so the generated
    /// class never has to be public, never has to be named in text, and never becomes surface anybody can
    /// compile against.
    /// </remarks>
    [Test]
    public void TheGeneratedDeclarationIsInternalAndStillFoundByItsExactBaseType()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Loaded.GetType().IsPublic, Is.False, "the implementation is hidden");
            Assert.That(Loaded.GetType().FullName, Is.EqualTo(FromBytes.EntryTypeName));
            Assert.That(
                ContractAssembly.GetExportedTypes().Select(type => type.FullName),
                Is.EqualTo(new[]
                {
                    "Arronix.Media.Movies.Movie",
                    "Arronix.Media.Movies.MovieReleaseStage",
                    "Arronix.Media.Movies.MovieReleaseTimeline",
                }),
                "the contract assembly's public surface is its domain and nothing else");
        });
    }

    [Test]
    public void AHostReadsTheSameFactsWithoutConstructingTheDeclaration()
    {
        var data = ContractAssembly.GetCustomAttributesData()
            .Where(candidate => candidate.AttributeType.BaseType == typeof(ClientContractEntryPointAttribute))
            .ToArray();

        Assert.That(data, Has.Length.EqualTo(1));

        var arguments = data[0].ConstructorArguments;

        Assert.Multiple(() =>
        {
            Assert.That(((Type)arguments[0].Value!).FullName, Is.EqualTo(FromBytes.EntityTypeName));
            Assert.That((string)arguments[1].Value!, Is.EqualTo(FromBytes.GeneratedMetadataHash));
            Assert.That((string)arguments[2].Value!, Is.EqualTo(FromBytes.ProjectionSchemaHash));
            Assert.That(data[0].AttributeType.FullName, Is.EqualTo(FromBytes.EntryTypeName));
        });
    }

    /// <remarks>
    /// Deserialization and projection are separate calls, and that separation is the claim. A single
    /// bytes-to-fields call proves only that fields came out; it is satisfied equally well by a shortcut
    /// that never constructs anything. Asking the returned value what it is, and getting the exact type the
    /// blob referenced from the assembly that declared the entry point, is what proves one existed.
    /// </remarks>
    [Test]
    public void DeserializingProducesAValueOfTheDeclaredTypeFromTheDeclaringAssembly()
    {
        var payload = Loaded.Serialize(MovieClientContractTests.Complete());

        var value = Loaded.Deserialize(payload);
        var runtimeType = value.GetType();

        Assert.Multiple(() =>
        {
            Assert.That(runtimeType, Is.SameAs(Loaded.EntityType));
            Assert.That(runtimeType.FullName, Is.EqualTo(FromBytes.EntityTypeName));
            Assert.That(runtimeType.Assembly, Is.SameAs(ContractAssembly));
            Assert.That(Loaded.Project(value).EntityType, Is.SameAs(runtimeType));
        });
    }

    [Test]
    public void TheDeclaredSchemaIsReadableBeforeAnyPayloadIs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Loaded.Schema, Is.Not.Empty);
            Assert.That(Loaded.Schema.Select(field => field.FieldId), Is.Unique);
        });
    }

    /// <summary>
    /// Reads every client contract declaration out of an assembly's bytes.
    /// </summary>
    /// <remarks>
    /// Structured metadata reading only: <see cref="PEReader"/> and <see cref="MetadataReader"/> over a byte
    /// array. Nothing here resolves a type, runs a class constructor, or hands anything to the runtime.
    /// </remarks>
    private static IReadOnlyList<Declaration> ReadFromBytes(byte[] content)
    {
        using var image = new PEReader(ImmutableArray.Create(content));
        var tables = image.GetMetadataReader();
        var found = new List<Declaration>();

        foreach (var handle in tables.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = tables.GetCustomAttribute(handle);

            if (attribute.Constructor.Kind != HandleKind.MethodDefinition)
            {
                continue;
            }

            var constructor = tables.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
            var declaring = tables.GetTypeDefinition(constructor.GetDeclaringType());

            if (declaring.BaseType.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var baseType = tables.GetTypeReference((TypeReferenceHandle)declaring.BaseType);
            var baseNamespace = tables.GetString(baseType.Namespace);
            var baseName = tables.GetString(baseType.Name);

            if (baseNamespace != DeclarationNamespace || baseName != DeclarationName)
            {
                continue;
            }

            // A namespace and a name are not an identity. Follow the reference to the assembly it names
            // and read that assembly's complete identity, so a same-named declaration carried by the
            // payload itself, by one of its modules, or by any other assembly is a different answer rather
            // than the same one.
            var scopeKind = baseType.ResolutionScope.Kind;
            var scopeIdentity = scopeKind == HandleKind.AssemblyReference
                ? Identity(tables, tables.GetAssemblyReference((AssemblyReferenceHandle)baseType.ResolutionScope))
                : null;

            var arguments = attribute.DecodeValue(BlobTypeProvider.Instance).FixedArguments;

            found.Add(new Declaration(
                tables.GetString(declaring.Namespace) + "." + tables.GetString(declaring.Name),
                baseNamespace,
                baseName,
                BaseIsTypeReference: true,
                scopeKind,
                scopeIdentity,
                (string)arguments[0].Value!,
                (string)arguments[1].Value!,
                (string)arguments[2].Value!));
        }

        return found;
    }

    private sealed record Declaration(
        string EntryTypeName,
        string BaseNamespace,
        string BaseName,
        bool BaseIsTypeReference,
        HandleKind BaseResolutionScopeKind,
        string? BaseAssemblyIdentity,
        string EntityTypeName,
        string GeneratedMetadataHash,
        string ProjectionSchemaHash);

    /// <summary>
    /// Renders a referenced assembly's complete identity the way the runtime prints one.
    /// </summary>
    /// <remarks>
    /// The public key token is what the runtime binds on, and a reference may carry either the token or
    /// the full key, so a full key is reduced to its token before the two are compared as text.
    /// </remarks>
    private static string Identity(MetadataReader tables, AssemblyReference reference)
    {
        var culture = reference.Culture.IsNil ? string.Empty : tables.GetString(reference.Culture);
        var keyOrToken = tables.GetBlobBytes(reference.PublicKeyOrToken);

        var token = keyOrToken.Length == 0
            ? string.Empty
            : Convert.ToHexString(reference.Flags.HasFlag(AssemblyFlags.PublicKey) ? TokenOf(keyOrToken) : keyOrToken);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{tables.GetString(reference.Name)}, Version={reference.Version}, Culture={(culture.Length == 0 ? "neutral" : culture)}, PublicKeyToken={(token.Length == 0 ? "null" : token)}");
    }

    /// <summary>Reduces a full public key to the eight-byte token the runtime binds on.</summary>
    private static byte[] TokenOf(byte[] publicKey)
    {
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(publicKey, hash);

        var token = new byte[8];

        for (var index = 0; index < token.Length; index++)
        {
            token[index] = hash[hash.Length - 1 - index];
        }

        return token;
    }

    /// <summary>
    /// The smallest type provider that decodes a declaration carrying one type reference and two strings.
    /// </summary>
    /// <remarks>
    /// Names are namespace-qualified because that is how the decoder recognizes the <c>System.Type</c>
    /// parameter in the constructor signature; a bare name would make the type argument decode as something
    /// else entirely.
    /// </remarks>
    private sealed class BlobTypeProvider : ICustomAttributeTypeProvider<string>
    {
        internal static BlobTypeProvider Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var definition = reader.GetTypeDefinition(handle);
            return reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            return reader.GetString(reference.Namespace) + "." + reader.GetString(reference.Name);
        }

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetSystemType() => "System.Type";

        public bool IsSystemType(string type) => type == "System.Type";

        // The blob stores a type argument as its serialized name. For a type in the same assembly as the
        // declaration that is the bare full name, which is exactly what a consumer compares.
        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
    }
}
