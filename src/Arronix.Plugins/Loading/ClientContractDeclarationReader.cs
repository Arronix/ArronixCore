using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Arronix.Abstractions.Wire;

namespace Arronix.Plugins.Loading;

/// <summary>What an assembly's own bytes say about the client contracts it declares.</summary>
/// <param name="Declarations">The declarations, ordered by entry point type name.</param>
/// <param name="Defects">
/// Why a declaration this assembly carries could not be read. Non-empty withholds the package's client
/// facet and nothing else: the assembly is still an admissible shared contract, because what is malformed
/// is its description of a client surface rather than the assembly.
/// </param>
internal sealed record StagedClientContracts(
    ReadOnlyCollection<ClientContractDeclaration> Declarations,
    ReadOnlyCollection<string> Defects)
{
    /// <summary>Gets the reading of an assembly that declares nothing, which is valid.</summary>
    internal static StagedClientContracts None { get; } = new(new([]), new([]));
}

/// <summary>
/// Reads the client contracts an assembly declares, out of its own bytes.
/// </summary>
/// <remarks>
/// <para>
/// Structured metadata reading only. Nothing here resolves a type, runs a class constructor, constructs an
/// attribute or hands anything to the runtime, so a host publishes what a package declares without first
/// running the package.
/// </para>
/// <para>
/// It is possible only because every fact is a constructor argument, and constructor arguments live in the
/// custom attribute blob. A fact behind an overridden property would be executable code.
/// </para>
/// <para>
/// <b>The base type is checked by identity, not by name.</b> A declaration counts only when its type
/// definition directly inherits a type reference whose resolution scope is the exact
/// <c>Arronix.Abstractions</c> assembly reference this host runs. Namespace and type name cost a package
/// nothing to spell.
/// </para>
/// </remarks>
internal static class ClientContractDeclarationReader
{
    private const string DeclarationNamespace = "Arronix.Abstractions.Client";

    private const string DeclarationName = "ClientContractEntryPointAttribute";

    /// <summary>
    /// Reads every client contract declaration an assembly's metadata carries.
    /// </summary>
    /// <param name="metadata">The reader over the exact staged bytes.</param>
    /// <param name="contract">The identity of the universal contract assembly this host runs.</param>
    /// <returns>The declarations, and why any could not be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A malformed blob is a defect rather than an exception. Staging decides whether a file is an
    /// admissible assembly; this decides whether that assembly can also be offered to a browser, and one
    /// unreadable declaration must not cost a host the shared contract every dependant binds to.
    /// </remarks>
    internal static StagedClientContracts Read(MetadataReader metadata, AssemblyIdentity contract)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var found = new List<ClientContractDeclaration>();
        var defects = new List<string>();

        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            Decode(metadata, handle, contract, found, defects);
        }

        // Ambiguity is a defect, not a preference. A consumer resolves a contract by the type the
        // declaration names, so two declarations answering to one name leave it no way to choose.
        defects.AddRange(Ambiguous(found, "entry point type", declaration => declaration.EntryPointType));
        defects.AddRange(Ambiguous(found, "entity type", declaration => declaration.EntityTypeName));

        return found.Count == 0 && defects.Count == 0
            ? StagedClientContracts.None
            : new StagedClientContracts(
                new ReadOnlyCollection<ClientContractDeclaration>(
                    [.. found.OrderBy(declaration => declaration.EntryPointType, StringComparer.Ordinal)]),
                new ReadOnlyCollection<string>([.. defects.Order(StringComparer.Ordinal)]));
    }

    private static void Decode(
        MetadataReader metadata,
        CustomAttributeHandle handle,
        AssemblyIdentity contract,
        List<ClientContractDeclaration> found,
        List<string> defects)
    {
        var attribute = metadata.GetCustomAttribute(handle);

        // A declaration is a type this assembly defines, so its constructor is a method definition.
        if (attribute.Constructor.Kind != HandleKind.MethodDefinition)
        {
            return;
        }

        string entryPoint;
        CustomAttributeValue<string> value;

        try
        {
            var constructor = metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
            var declaring = metadata.GetTypeDefinition(constructor.GetDeclaringType());

            if (!DerivesFromTheDeclaration(metadata, declaring, contract))
            {
                return;
            }

            entryPoint = Name(metadata, declaring);
            value = attribute.DecodeValue(DeclarationTypeProvider.Instance);
        }
        catch (Exception failure) when (Unreadable(failure))
        {
            defects.Add($"A client contract declaration could not be decoded: {failure.Message}");
            return;
        }

        var arguments = value.FixedArguments;

        if (arguments.Length != 3
            || arguments[0].Value is not string entityType
            || arguments[1].Value is not string generatedMetadataHash
            || arguments[2].Value is not string projectionSchemaHash)
        {
            defects.Add(
                $"'{entryPoint}' derives the client contract declaration and does not carry the entity type "
                + "and the two hashes its constructor takes.");
            return;
        }

        found.Add(new ClientContractDeclaration(
            entryPoint,
            entityType,
            generatedMetadataHash,
            projectionSchemaHash));
    }

    private static IEnumerable<string> Ambiguous(
        IReadOnlyList<ClientContractDeclaration> found,
        string what,
        Func<ClientContractDeclaration, string> key)
        => found
            .GroupBy(key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(group => $"This assembly declares {group.Count()} client contracts for {what} '{group.Key}'.");

    /// <summary>Determines whether a type definition directly inherits this platform's declaration.</summary>
    /// <remarks>
    /// A same-named type the assembly defines itself resolves to a type definition rather than a reference;
    /// a same-named type in a third assembly resolves through a different assembly reference. Both are
    /// refused here.
    /// </remarks>
    private static bool DerivesFromTheDeclaration(
        MetadataReader metadata,
        TypeDefinition declaring,
        AssemblyIdentity contract)
    {
        if (declaring.BaseType.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        var baseType = metadata.GetTypeReference((TypeReferenceHandle)declaring.BaseType);

        if (!metadata.GetString(baseType.Namespace).Equals(DeclarationNamespace, StringComparison.Ordinal)
            || !metadata.GetString(baseType.Name).Equals(DeclarationName, StringComparison.Ordinal)
            || baseType.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        var scope = metadata.GetAssemblyReference((AssemblyReferenceHandle)baseType.ResolutionScope);

        return StagedAssembly.SameIdentity(
            AssemblyIdentity.Create(
                metadata.GetString(scope.Name),
                scope.Version,
                scope.Culture.IsNil ? string.Empty : metadata.GetString(scope.Culture),
                scope.PublicKeyOrToken.IsNil ? [] : metadata.GetBlobBytes(scope.PublicKeyOrToken),
                scope.Flags.HasFlag(AssemblyFlags.PublicKey)),
            contract);
    }

    /// <summary>The ways malformed metadata reports itself, all of which mean the same thing here.</summary>
    private static bool Unreadable(Exception failure)
        => failure is BadImageFormatException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException;

    private static string Name(MetadataReader metadata, TypeDefinition type)
    {
        var space = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return space.Length == 0 ? name : space + "." + name;
    }

    /// <summary>Decodes a declaration carrying one type reference and two strings.</summary>
    /// <remarks>
    /// Names are namespace-qualified because that is how the decoder recognizes the <see cref="Type"/>
    /// parameter in the constructor signature. The type argument arrives as its serialized name, which for
    /// a type in the declaring assembly is the bare full name.
    /// </remarks>
    private sealed class DeclarationTypeProvider : ICustomAttributeTypeProvider<string>
    {
        internal static DeclarationTypeProvider Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            ArgumentNullException.ThrowIfNull(reader);
            var definition = reader.GetTypeDefinition(handle);
            return reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            ArgumentNullException.ThrowIfNull(reader);
            var reference = reader.GetTypeReference(handle);
            return reader.GetString(reference.Namespace) + "." + reader.GetString(reference.Name);
        }

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetSystemType() => "System.Type";

        public bool IsSystemType(string type) => string.Equals(type, "System.Type", StringComparison.Ordinal);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
    }
}
