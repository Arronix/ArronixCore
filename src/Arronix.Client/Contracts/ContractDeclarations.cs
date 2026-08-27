using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Wire;

namespace Arronix.Client.Contracts;

/// <summary>
/// Reads the client contracts a payload declares, out of the exact bytes received.
/// </summary>
/// <remarks>
/// <para>
/// The browser's own reader, deliberately. The host reads the same blob when it admits an assembly and
/// publishes what it found; this reads it again from the bytes that arrived and requires the two to agree.
/// Sharing one implementation would make them agree by construction, which is the one thing this check must
/// not do — a published declaration is something to check against, never something to adopt.
/// </para>
/// <para>
/// Structured metadata reading only, over a byte array, before the runtime is handed anything. Nothing here
/// resolves a type, constructs an attribute or runs a class constructor; a declaration is readable this
/// early only because every fact it carries is a constructor argument, and constructor arguments live in
/// the custom attribute blob.
/// </para>
/// <para>
/// The rules are the host's rules, restated rather than referenced. A client that accepted a declaration on
/// weaker grounds than the host applied would be trusting the host's judgment while appearing to check it.
/// </para>
/// </remarks>
internal static class ContractDeclarationReader
{
    private const string DeclarationNamespace = "Arronix.Abstractions.Client";

    private const string DeclarationName = nameof(ClientContractEntryPointAttribute);

    private static readonly AssemblyName Contract =
        typeof(ClientContractEntryPointAttribute).Assembly.GetName();

    /// <summary>
    /// Reads every client contract declaration a payload's metadata carries.
    /// </summary>
    /// <param name="metadata">The reader over the exact bytes received.</param>
    /// <param name="declarations">What the bytes declare, ordered by entry point type name.</param>
    /// <param name="defect">Why a declaration could not be read, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every declaration the payload carries was readable.</returns>
    internal static bool TryRead(
        MetadataReader metadata,
        out ReadOnlyCollection<ClientContractDeclaration> declarations,
        out string? defect)
    {
        var found = new List<ClientContractDeclaration>();
        var defects = new List<string>();
        var declared = new Lazy<HashSet<string>>(() => TypeNames(metadata));

        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            Decode(metadata, handle, declared, found, defects);
        }

        // A consumer resolves a contract by the name a declaration carries, so two answering to one name
        // leave it no way to choose.
        defects.AddRange(Ambiguous(found, "entry point type", declaration => declaration.EntryPointType));
        defects.AddRange(Ambiguous(found, "entity type", declaration => declaration.EntityTypeName));

        declarations = new ReadOnlyCollection<ClientContractDeclaration>(
            [.. found.OrderBy(declaration => declaration.EntryPointType, StringComparer.Ordinal)]);
        defect = defects.Order(StringComparer.Ordinal).FirstOrDefault();
        return defect is null;
    }

    private static void Decode(
        MetadataReader metadata,
        CustomAttributeHandle handle,
        Lazy<HashSet<string>> declared,
        List<ClientContractDeclaration> found,
        List<string> defects)
    {
        var attribute = metadata.GetCustomAttribute(handle);

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

            if (!DerivesFromTheDeclaration(metadata, declaring))
            {
                return;
            }

            entryPoint = FullName(metadata, declaring);

            if (!IsInstanceConstructor(metadata, constructor))
            {
                defects.Add(
                    $"'{entryPoint}' applies a client contract declaration through something other than its "
                    + "own instance constructor.");
                return;
            }

            value = attribute.DecodeValue(DeclarationTypeProvider.Instance);
        }
        catch (Exception failure) when (Unreadable(failure))
        {
            defects.Add($"A client contract declaration could not be decoded: {failure.Message}");
            return;
        }

        var arguments = value.FixedArguments;

        if (arguments.Length != 3
            || !string.Equals(arguments[0].Type, "System.Type", StringComparison.Ordinal)
            || !string.Equals(arguments[1].Type, "System.String", StringComparison.Ordinal)
            || !string.Equals(arguments[2].Type, "System.String", StringComparison.Ordinal)
            || arguments[0].Value is not string entityType
            || arguments[1].Value is not string generatedMetadataHash
            || arguments[2].Value is not string projectionSchemaHash)
        {
            defects.Add(
                $"'{entryPoint}' declares a client contract whose constructor is not "
                + "(System.Type, string, string).");
            return;
        }

        if (!declared.Value.Contains(entityType))
        {
            defects.Add(
                $"'{entryPoint}' declares the entity type '{entityType}', which this assembly does not define.");
            return;
        }

        if (!IsHash(generatedMetadataHash) || !IsHash(projectionSchemaHash))
        {
            defects.Add($"'{entryPoint}' declares a hash that is not 64 upper-case hexadecimal characters.");
            return;
        }

        found.Add(new ClientContractDeclaration(
            entryPoint,
            entityType,
            generatedMetadataHash,
            projectionSchemaHash));
    }

    private static bool IsInstanceConstructor(MetadataReader metadata, MethodDefinition constructor)
        => !constructor.Attributes.HasFlag(MethodAttributes.Static)
            && constructor.Attributes.HasFlag(MethodAttributes.RTSpecialName)
            && metadata.GetString(constructor.Name).Equals(".ctor", StringComparison.Ordinal);

    private static bool IsHash(string value)
        => value.Length == 64
            && value.All(static character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

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
    /// By identity. A same-named type the payload defines itself resolves to a type definition rather than a
    /// reference, and a same-named type in a third assembly resolves through a different assembly reference.
    /// Both are refused, because a namespace and a type name cost a payload nothing to spell.
    /// </remarks>
    private static bool DerivesFromTheDeclaration(MetadataReader metadata, TypeDefinition declaring)
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

        return metadata.GetString(scope.Name).Equals(Contract.Name, StringComparison.OrdinalIgnoreCase)
            && scope.Version == Contract.Version
            && Culture(metadata, scope).Equals(
                Contract.CultureName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && Token(metadata, scope).SequenceEqual(Contract.GetPublicKeyToken() ?? []);
    }

    private static string Culture(MetadataReader metadata, AssemblyReference reference)
        => reference.Culture.IsNil ? string.Empty : metadata.GetString(reference.Culture);

    /// <summary>Reduces a reference's key blob to the eight bytes the runtime binds on.</summary>
    private static byte[] Token(MetadataReader metadata, AssemblyReference reference)
    {
        if (reference.PublicKeyOrToken.IsNil)
        {
            return [];
        }

        var blob = metadata.GetBlobBytes(reference.PublicKeyOrToken);

        if (!reference.Flags.HasFlag(AssemblyFlags.PublicKey))
        {
            return blob;
        }

        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(blob, hash);

        var token = new byte[8];

        for (var index = 0; index < token.Length; index++)
        {
            token[index] = hash[hash.Length - 1 - index];
        }

        return token;
    }

    private static HashSet<string> TypeNames(MetadataReader metadata)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.TypeDefinitions)
        {
            names.Add(FullName(metadata, metadata.GetTypeDefinition(handle)));
        }

        return names;
    }

    private static string FullName(MetadataReader metadata, TypeDefinition type)
    {
        var name = metadata.GetString(type.Name);

        if (!type.IsNested)
        {
            var space = metadata.GetString(type.Namespace);
            return space.Length == 0 ? name : space + "." + name;
        }

        return FullName(metadata, metadata.GetTypeDefinition(type.GetDeclaringType())) + "+" + name;
    }

    private static bool Unreadable(Exception failure)
        => failure is BadImageFormatException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException
            or OverflowException;

    /// <summary>Decodes a declaration carrying one type reference and two strings.</summary>
    private sealed class DeclarationTypeProvider : ICustomAttributeTypeProvider<string>
    {
        internal static DeclarationTypeProvider Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return FullName(reader, reader.GetTypeDefinition(handle));
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            ArgumentNullException.ThrowIfNull(reader);
            var reference = reader.GetTypeReference(handle);
            var space = reader.GetString(reference.Namespace);
            var name = reader.GetString(reference.Name);
            return space.Length == 0 ? name : space + "." + name;
        }

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetSystemType() => "System.Type";

        public bool IsSystemType(string type) => string.Equals(type, "System.Type", StringComparison.Ordinal);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
            => throw new BadImageFormatException(
                $"A client contract declaration carries an enumeration argument of type '{type}', and it takes none.");
    }
}
