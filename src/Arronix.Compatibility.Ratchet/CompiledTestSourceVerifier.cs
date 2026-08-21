using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Proves that an executed test assembly was compiled from a pinned repository source file.</summary>
public static class CompiledTestSourceVerifier
{
    private static readonly Guid Sha256DocumentHashAlgorithm =
        new("8829d00f-11b8-4213-878b-770e8597ac16");

    private static readonly Guid EmbeddedSourceCustomDebugInformation =
        new("0e8a571b-6926-466e-b4ad-8ab04611f5fe");

    /// <summary>
    /// Verifies that exactly one method declared by a fixture has visible sequence points in the pinned
    /// source document recorded by the assembly's matching Portable PDB.
    /// </summary>
    public static CompiledTestSourceVerification VerifyMethod(
        string assemblyPath,
        string fixtureType,
        string methodName,
        string repositoryRoot,
        string sourceFile,
        string expectedSourceDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        var source = PrepareSource(repositoryRoot, sourceFile, expectedSourceDigest);
        if (source.Failure is not null)
        {
            return source.Failure;
        }

        using var artifacts = OpenArtifacts(assemblyPath);
        if (artifacts.Failure is not null)
        {
            return artifacts.Failure;
        }

        var document = FindAndVerifyDocument(artifacts.PdbMetadata, source);
        if (document.Failure is not null)
        {
            return document.Failure;
        }

        var matchingTypes = artifacts.PeMetadata.TypeDefinitions
            .Where(handle => string.Equals(
                GetClrTypeName(artifacts.PeMetadata, handle),
                fixtureType,
                StringComparison.Ordinal))
            .ToArray();

        if (matchingTypes.Length == 0)
        {
            return Failure(
                "compiled-source.fixture-missing",
                $"The executed assembly does not declare fixture type '{fixtureType}'.");
        }

        if (matchingTypes.Length != 1)
        {
            return Failure(
                "compiled-source.fixture-ambiguous",
                $"The executed assembly declares fixture type '{fixtureType}' more than once.");
        }

        var matchingMethods = artifacts.PeMetadata
            .GetTypeDefinition(matchingTypes[0])
            .GetMethods()
            .Where(handle => string.Equals(
                artifacts.PeMetadata.GetString(artifacts.PeMetadata.GetMethodDefinition(handle).Name),
                methodName,
                StringComparison.Ordinal))
            .ToArray();

        if (matchingMethods.Length == 0)
        {
            return Failure(
                "compiled-source.method-missing",
                $"Fixture '{fixtureType}' does not declare method '{methodName}'.");
        }

        if (matchingMethods.Length != 1)
        {
            return Failure(
                "compiled-source.method-ambiguous",
                $"Fixture '{fixtureType}' declares more than one method named '{methodName}'.");
        }

        var methodDocuments = FindVisibleMethodDocuments(artifacts.PdbMetadata, matchingMethods[0]);
        if (methodDocuments.Count == 0)
        {
            return Failure(
                "compiled-source.sequence-points-missing",
                $"Method '{fixtureType}.{methodName}' has no visible Portable PDB sequence points.");
        }

        if (methodDocuments.Count != 1 || !methodDocuments.Contains(document.Handle))
        {
            var observed = string.Join(
                ", ",
                methodDocuments
                    .Select(handle => NormalizeDocumentPath(
                        artifacts.PdbMetadata.GetString(artifacts.PdbMetadata.GetDocument(handle).Name)))
                    .Order(StringComparer.Ordinal));
            return Failure(
                "compiled-source.method-document-mismatch",
                $"Method '{fixtureType}.{methodName}' maps to [{observed}], not exclusively " +
                $"to '{source.MappedDocumentPath}'.");
        }

        return Success(
            $"Method '{fixtureType}.{methodName}' is compiled from '{source.SourceFile}' with its pinned SHA-256 digest.");
    }

    /// <summary>
    /// Verifies that the assembly's matching Portable PDB contains a pinned support document, without
    /// requiring that document to own a test method's sequence points.
    /// </summary>
    public static CompiledTestSourceVerification VerifyDocument(
        string assemblyPath,
        string repositoryRoot,
        string sourceFile,
        string expectedSourceDigest)
    {
        var source = PrepareSource(repositoryRoot, sourceFile, expectedSourceDigest);
        if (source.Failure is not null)
        {
            return source.Failure;
        }

        using var artifacts = OpenArtifacts(assemblyPath);
        if (artifacts.Failure is not null)
        {
            return artifacts.Failure;
        }

        var document = FindAndVerifyDocument(artifacts.PdbMetadata, source);
        return document.Failure
            ?? Success(
                $"The matching Portable PDB contains '{source.SourceFile}' with its pinned SHA-256 digest.");
    }

    private static PreparedSource PrepareSource(
        string repositoryRoot,
        string sourceFile,
        string expectedSourceDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSourceDigest);

        if (!TryParseSha256(expectedSourceDigest, out var expectedHash))
        {
            throw new ArgumentException(
                "The expected source digest must be 'sha256:' followed by 64 hexadecimal characters.",
                nameof(expectedSourceDigest));
        }

        if (Path.IsPathRooted(sourceFile))
        {
            throw new ArgumentException("The source file must be repository-relative.", nameof(sourceFile));
        }

        var normalizedSourceFile = NormalizeDocumentPath(sourceFile);
        if (normalizedSourceFile.StartsWith("./", StringComparison.Ordinal))
        {
            normalizedSourceFile = normalizedSourceFile[2..];
        }

        var segments = normalizedSourceFile.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(static segment => segment is "." or "..")
            || !string.Equals(string.Join('/', segments), normalizedSourceFile, StringComparison.Ordinal))
        {
            throw new ArgumentException("The source file must be a canonical repository-relative path.", nameof(sourceFile));
        }

        if (!normalizedSourceFile.StartsWith("src/", StringComparison.Ordinal)
            || normalizedSourceFile.Length == "src/".Length)
        {
            return PreparedSource.Failed(
                normalizedSourceFile,
                Failure(
                    "compiled-source.source-path-unmapped",
                    $"Source '{normalizedSourceFile}' is not beneath the repository's 'src/' PathMap root."));
        }

        var fullRepositoryRoot = Path.GetFullPath(repositoryRoot);
        var fullSourcePath = Path.GetFullPath(
            normalizedSourceFile.Replace('/', Path.DirectorySeparatorChar),
            fullRepositoryRoot);
        var relativeCheck = NormalizeDocumentPath(Path.GetRelativePath(fullRepositoryRoot, fullSourcePath));
        if (relativeCheck.StartsWith("../", StringComparison.Ordinal) || relativeCheck == "..")
        {
            throw new ArgumentException("The source file escapes the repository root.", nameof(sourceFile));
        }

        if (!File.Exists(fullSourcePath))
        {
            return PreparedSource.Failed(
                normalizedSourceFile,
                Failure(
                    "compiled-source.source-missing",
                    $"Pinned source file '{normalizedSourceFile}' does not exist."));
        }

        byte[] actualHash;
        long sourceLength;
        try
        {
            using var sourceStream = File.OpenRead(fullSourcePath);
            sourceLength = sourceStream.Length;
            actualHash = SHA256.HashData(sourceStream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PreparedSource.Failed(
                normalizedSourceFile,
                Failure(
                    "compiled-source.source-unreadable",
                    $"Pinned source file '{normalizedSourceFile}' could not be read: {exception.Message}"));
        }

        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            return PreparedSource.Failed(
                normalizedSourceFile,
                Failure(
                    "compiled-source.source-digest-mismatch",
                    $"Pinned source file '{normalizedSourceFile}' does not have the expected SHA-256 digest."));
        }

        return new PreparedSource(
            normalizedSourceFile,
            "./" + normalizedSourceFile["src/".Length..],
            expectedHash,
            sourceLength,
            null);
    }

    private static OpenedArtifacts OpenArtifacts(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullAssemblyPath))
        {
            return OpenedArtifacts.Failed(
                Failure(
                    "compiled-source.assembly-missing",
                    $"Executed assembly '{fullAssemblyPath}' does not exist."));
        }

        FileStream? peStream = null;
        PEReader? peReader = null;
        MetadataReaderProvider? pdbProvider = null;
        try
        {
            peStream = File.OpenRead(fullAssemblyPath);
            peReader = new PEReader(peStream);
            if (!peReader.HasMetadata)
            {
                peReader.Dispose();
                peStream.Dispose();
                return OpenedArtifacts.Failed(
                    Failure(
                        "compiled-source.assembly-metadata-missing",
                        $"Executed assembly '{fullAssemblyPath}' has no CLR metadata."));
            }

            if (!peReader.TryOpenAssociatedPortablePdb(
                    fullAssemblyPath,
                    static path => File.Exists(path) ? File.OpenRead(path) : null,
                    out pdbProvider,
                    out _))
            {
                peReader.Dispose();
                peStream.Dispose();
                return OpenedArtifacts.Failed(
                    Failure(
                        "compiled-source.pdb-missing",
                        $"Executed assembly '{fullAssemblyPath}' has no matching associated Portable PDB."));
            }

            return new OpenedArtifacts(
                peStream,
                peReader,
                pdbProvider,
                peReader.GetMetadataReader(),
                pdbProvider!.GetMetadataReader(),
                null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException)
        {
            pdbProvider?.Dispose();
            peReader?.Dispose();
            peStream?.Dispose();
            return OpenedArtifacts.Failed(
                Failure(
                    "compiled-source.artifact-invalid",
                    $"The executed assembly or its associated Portable PDB is unreadable or invalid: {exception.Message}"));
        }
    }

    private static VerifiedDocument FindAndVerifyDocument(
        MetadataReader pdbMetadata,
        PreparedSource source)
    {
        var matchingDocuments = pdbMetadata.Documents
            .Where(handle => string.Equals(
                NormalizeDocumentPath(pdbMetadata.GetString(pdbMetadata.GetDocument(handle).Name)),
                source.MappedDocumentPath,
                StringComparison.Ordinal))
            .ToArray();

        if (matchingDocuments.Length == 0)
        {
            return VerifiedDocument.Failed(
                Failure(
                    "compiled-source.document-missing",
                    $"The matching Portable PDB does not contain canonical document '{source.MappedDocumentPath}'."));
        }

        if (matchingDocuments.Length != 1)
        {
            return VerifiedDocument.Failed(
                Failure(
                    "compiled-source.document-ambiguous",
                    $"The matching Portable PDB contains canonical document '{source.MappedDocumentPath}' more than once."));
        }

        var handle = matchingDocuments[0];
        var document = pdbMetadata.GetDocument(handle);
        if (document.HashAlgorithm.IsNil
            || pdbMetadata.GetGuid(document.HashAlgorithm) != Sha256DocumentHashAlgorithm)
        {
            return VerifiedDocument.Failed(
                Failure(
                    "compiled-source.document-hash-algorithm-mismatch",
                    $"Portable PDB document '{source.MappedDocumentPath}' is not pinned with SHA-256."));
        }

        var pdbHash = pdbMetadata.GetBlobBytes(document.Hash);
        if (!CryptographicOperations.FixedTimeEquals(pdbHash, source.ExpectedHash))
        {
            return VerifiedDocument.Failed(
                Failure(
                    "compiled-source.document-checksum-mismatch",
                    $"Portable PDB document '{source.MappedDocumentPath}' was compiled from different bytes."));
        }

        var embeddedSourceFailure = VerifyEmbeddedSource(pdbMetadata, handle, source);
        if (embeddedSourceFailure is not null)
        {
            return VerifiedDocument.Failed(embeddedSourceFailure);
        }

        return new VerifiedDocument(handle, null);
    }

    private static CompiledTestSourceVerification? VerifyEmbeddedSource(
        MetadataReader pdbMetadata,
        DocumentHandle documentHandle,
        PreparedSource source)
    {
        var embeddedSources = pdbMetadata
            .GetCustomDebugInformation(documentHandle)
            .Where(handle =>
            {
                var information = pdbMetadata.GetCustomDebugInformation(handle);
                return !information.Kind.IsNil
                    && pdbMetadata.GetGuid(information.Kind) == EmbeddedSourceCustomDebugInformation;
            })
            .ToArray();

        if (embeddedSources.Length == 0)
        {
            return Failure(
                "compiled-source.embedded-source-missing",
                $"Portable PDB document '{source.MappedDocumentPath}' has no EmbeddedSource record.");
        }

        if (embeddedSources.Length != 1)
        {
            return Failure(
                "compiled-source.embedded-source-ambiguous",
                $"Portable PDB document '{source.MappedDocumentPath}' has more than one EmbeddedSource record.");
        }

        var information = pdbMetadata.GetCustomDebugInformation(embeddedSources[0]);
        var payload = pdbMetadata.GetBlobBytes(information.Value);
        if (payload.Length < sizeof(int))
        {
            return Failure(
                "compiled-source.embedded-source-payload-invalid",
                $"Portable PDB document '{source.MappedDocumentPath}' has a truncated EmbeddedSource header.");
        }

        var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (declaredLength < 0)
        {
            return Failure(
                "compiled-source.embedded-source-payload-invalid",
                $"Portable PDB document '{source.MappedDocumentPath}' has a negative EmbeddedSource length.");
        }

        byte[] embeddedBytes;
        if (declaredLength == 0)
        {
            embeddedBytes = payload[sizeof(int)..];
        }
        else
        {
            if (declaredLength != source.ExpectedLength)
            {
                return Failure(
                    "compiled-source.embedded-source-length-mismatch",
                    $"Portable PDB document '{source.MappedDocumentPath}' declares {declaredLength} embedded " +
                    $"bytes, but the pinned source contains {source.ExpectedLength} bytes.");
            }

            embeddedBytes = new byte[declaredLength];
            try
            {
                using var compressed = new MemoryStream(
                    payload,
                    sizeof(int),
                    payload.Length - sizeof(int),
                    writable: false);
                using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
                var offset = 0;
                while (offset < embeddedBytes.Length)
                {
                    var read = deflate.Read(embeddedBytes, offset, embeddedBytes.Length - offset);
                    if (read == 0)
                    {
                        break;
                    }

                    offset += read;
                }

                if (offset != embeddedBytes.Length || deflate.ReadByte() != -1)
                {
                    return Failure(
                        "compiled-source.embedded-source-length-mismatch",
                        $"Portable PDB document '{source.MappedDocumentPath}' does not decompress to its " +
                        $"declared {declaredLength} bytes.");
                }
            }
            catch (InvalidDataException exception)
            {
                return Failure(
                    "compiled-source.embedded-source-compression-invalid",
                    $"Portable PDB document '{source.MappedDocumentPath}' has invalid deflate data: {exception.Message}");
            }
        }

        if (embeddedBytes.LongLength != source.ExpectedLength)
        {
            return Failure(
                "compiled-source.embedded-source-length-mismatch",
                $"Portable PDB document '{source.MappedDocumentPath}' embeds {embeddedBytes.LongLength} bytes, " +
                $"but the pinned source contains {source.ExpectedLength} bytes.");
        }

        var embeddedHash = SHA256.HashData(embeddedBytes);
        if (!CryptographicOperations.FixedTimeEquals(embeddedHash, source.ExpectedHash))
        {
            return Failure(
                "compiled-source.embedded-source-digest-mismatch",
                $"Portable PDB document '{source.MappedDocumentPath}' embeds bytes with a different SHA-256 digest.");
        }

        return null;
    }

    private static HashSet<DocumentHandle> FindVisibleMethodDocuments(
        MetadataReader pdbMetadata,
        MethodDefinitionHandle declaredMethod)
    {
        var documents = new HashSet<DocumentHandle>();
        foreach (var debugHandle in pdbMetadata.MethodDebugInformation)
        {
            var debugInformation = pdbMetadata.GetMethodDebugInformation(debugHandle);
            var rowMethod = MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(debugHandle));
            if (rowMethod != declaredMethod
                && debugInformation.GetStateMachineKickoffMethod() != declaredMethod)
            {
                continue;
            }

            foreach (var sequencePoint in debugInformation.GetSequencePoints())
            {
                if (sequencePoint.IsHidden)
                {
                    continue;
                }

                var document = sequencePoint.Document.IsNil
                    ? debugInformation.Document
                    : sequencePoint.Document;
                if (!document.IsNil)
                {
                    documents.Add(document);
                }
            }
        }

        return documents;
    }

    private static string GetClrTypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var name = metadata.GetString(type.Name);
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            return GetClrTypeName(metadata, declaringType) + "+" + name;
        }

        var typeNamespace = metadata.GetString(type.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }

    private static string NormalizeDocumentPath(string value) => value.Replace('\\', '/');

    private static bool TryParseSha256(string value, out byte[] hash)
    {
        hash = [];
        const string prefix = "sha256:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 64)
        {
            return false;
        }

        try
        {
            hash = Convert.FromHexString(value[prefix.Length..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static CompiledTestSourceVerification Success(string message)
        => new(true, "compiled-source.verified", message);

    private static CompiledTestSourceVerification Failure(string code, string message)
        => new(false, code, message);

    private sealed record PreparedSource(
        string SourceFile,
        string MappedDocumentPath,
        byte[] ExpectedHash,
        long ExpectedLength,
        CompiledTestSourceVerification? Failure)
    {
        public static PreparedSource Failed(
            string sourceFile,
            CompiledTestSourceVerification failure)
            => new(sourceFile, string.Empty, [], 0, failure);
    }

    private sealed record VerifiedDocument(
        DocumentHandle Handle,
        CompiledTestSourceVerification? Failure)
    {
        public static VerifiedDocument Failed(CompiledTestSourceVerification failure)
            => new(default, failure);
    }

    private sealed class OpenedArtifacts : IDisposable
    {
        public OpenedArtifacts(
            Stream? peStream,
            PEReader? peReader,
            MetadataReaderProvider? pdbProvider,
            MetadataReader? peMetadata,
            MetadataReader? pdbMetadata,
            CompiledTestSourceVerification? failure)
        {
            PeStream = peStream;
            PeReader = peReader;
            PdbProvider = pdbProvider;
            _peMetadata = peMetadata;
            _pdbMetadata = pdbMetadata;
            Failure = failure;
        }

        private readonly MetadataReader? _peMetadata;

        private readonly MetadataReader? _pdbMetadata;

        public MetadataReader PeMetadata
            => _peMetadata ?? throw new InvalidOperationException("The assembly metadata is unavailable.");

        public MetadataReader PdbMetadata
            => _pdbMetadata ?? throw new InvalidOperationException("The Portable PDB metadata is unavailable.");

        public CompiledTestSourceVerification? Failure { get; }

        private Stream? PeStream { get; }

        private PEReader? PeReader { get; }

        private MetadataReaderProvider? PdbProvider { get; }

        public static OpenedArtifacts Failed(CompiledTestSourceVerification failure)
            => new(null, null, null, null, null, failure);

        public void Dispose()
        {
            PdbProvider?.Dispose();
            PeReader?.Dispose();
            PeStream?.Dispose();
        }
    }
}

/// <summary>The result of one compiled source provenance verification.</summary>
public sealed record CompiledTestSourceVerification(bool IsValid, string Code, string Message);
