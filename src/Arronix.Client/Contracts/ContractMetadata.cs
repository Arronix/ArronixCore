using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Arronix.Client.Contracts;

/// <summary>
/// What a candidate assembly's own bytes say about it, read without loading them.
/// </summary>
/// <param name="Identity">
/// The complete CLR identity the bytes declare, rendered the way
/// <see cref="System.Reflection.AssemblyName.FullName"/> renders one.
/// </param>
/// <param name="ModuleVersionId">The module identifier the compiler stamped into this build.</param>
/// <param name="ContractReference">
/// The identity of the single universal-contract reference the bytes declare, or <see langword="null"/>
/// when they declare none or more than one.
/// </param>
internal sealed record ContractMetadata(string Identity, Guid ModuleVersionId, string? ContractReference);

/// <summary>
/// Reads an assembly's declared identity out of its bytes, before any of it is loaded.
/// </summary>
/// <remarks>
/// Metadata reading only: <see cref="PEReader"/> and <see cref="MetadataReader"/> are structured readers
/// over a byte array. Nothing here resolves a type, runs a class constructor, or hands anything to the
/// runtime, which is what lets the client decide whether a payload may be admitted before it is — and what
/// keeps the client trimming and ahead-of-time safe.
/// </remarks>
internal static class ContractMetadataReader
{
    /// <summary>
    /// Reads what a candidate assembly's bytes declare about themselves.
    /// </summary>
    /// <param name="content">The exact bytes received.</param>
    /// <param name="contractAssemblyName">The simple name of the universal contract assembly.</param>
    /// <param name="metadata">What the bytes declare, on success.</param>
    /// <param name="failure">Why the bytes could not be read, on failure.</param>
    /// <returns><see langword="true"/> when the bytes are a managed assembly this reader could describe.</returns>
    internal static bool TryRead(
        byte[] content,
        string contractAssemblyName,
        out ContractMetadata? metadata,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractAssemblyName);

        metadata = null;
        failure = null;

        try
        {
            using var reader = new PEReader(ImmutableArray.Create(content));

            if (!reader.HasMetadata)
            {
                failure = "The bytes received carry no managed metadata.";
                return false;
            }

            var tables = reader.GetMetadataReader();

            if (!tables.IsAssembly)
            {
                failure = "The bytes received are a module rather than an assembly.";
                return false;
            }

            var definition = tables.GetAssemblyDefinition();

            var references = tables.AssemblyReferences
                .Select(tables.GetAssemblyReference)
                .Where(reference => string.Equals(
                    tables.GetString(reference.Name),
                    contractAssemblyName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            metadata = new ContractMetadata(
                Render(
                    tables.GetString(definition.Name),
                    definition.Version,
                    definition.Culture.IsNil ? string.Empty : tables.GetString(definition.Culture),
                    tables.GetBlobBytes(definition.PublicKey),

                    // A definition always carries the full key; a reference may carry either.
                    isFullPublicKey: true),
                tables.GetGuid(tables.GetModuleDefinition().Mvid),

                // Exactly one, or none. Two references to one simple name cannot both be the contract this
                // client carries, and a client contract that references it zero times was not built against
                // this contract line at all.
                references.Length == 1
                    ? Render(
                        tables.GetString(references[0].Name),
                        references[0].Version,
                        references[0].Culture.IsNil ? string.Empty : tables.GetString(references[0].Culture),
                        tables.GetBlobBytes(references[0].PublicKeyOrToken),
                        references[0].Flags.HasFlag(AssemblyFlags.PublicKey))
                    : null);

            return true;
        }
        catch (BadImageFormatException error)
        {
            failure = $"The bytes received are not a readable assembly image: {error.Message}";
            return false;
        }
        catch (InvalidOperationException error)
        {
            failure = $"The bytes received could not be described: {error.Message}";
            return false;
        }
    }

    /// <summary>
    /// Renders one identity the way the runtime prints one, so the two sides compare as text.
    /// </summary>
    /// <remarks>
    /// The public key token is compared, never the full key: a definition carries the key and a reference
    /// may carry either, and the token is what the runtime actually binds on. Hexadecimal case is not
    /// significant and callers compare case-insensitively.
    /// </remarks>
    private static string Render(string name, Version version, string culture, byte[] publicKeyOrToken, bool isFullPublicKey)
    {
        var token = publicKeyOrToken.Length == 0
            ? string.Empty
            : Convert.ToHexString(isFullPublicKey ? TokenOf(publicKeyOrToken) : publicKeyOrToken);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name}, Version={version}, Culture={(culture.Length == 0 ? "neutral" : culture)}, PublicKeyToken={(token.Length == 0 ? "null" : token)}");
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
}
