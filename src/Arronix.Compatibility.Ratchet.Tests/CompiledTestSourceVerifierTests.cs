using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class CompiledTestSourceVerifierTests
{
    private const string ThisSourceFile =
        "src/Arronix.Compatibility.Ratchet.Tests/CompiledTestSourceVerifierTests.cs";

    [Test]
    public void VerifierAcceptsSyncMethodCompiledFromPinnedSource()
    {
        var result = Verify(nameof(VerifierAcceptsSyncMethodCompiledFromPinnedSource));

        Assert.That(result.IsValid, Is.True, result.Message);
        Assert.That(result.Code, Is.EqualTo("compiled-source.verified"));
    }

    [Test]
    public async Task VerifierAcceptsAsyncMethodCompiledFromPinnedSource()
    {
        await Task.Yield();

        var result = Verify(nameof(VerifierAcceptsAsyncMethodCompiledFromPinnedSource));

        Assert.That(result.IsValid, Is.True, result.Message);
    }

    [Test]
    public void VerifierAcceptsIteratorMethodCompiledFromPinnedSource()
    {
        var result = Verify(nameof(IteratorProbe));

        Assert.That(result.IsValid, Is.True, result.Message);
    }

    [Test]
    public void VerifierRejectsARepositoryFileWhichDoesNotOwnTheMethod()
    {
        const string wrongSource = "src/Arronix.Compatibility.Ratchet.Tests/GlobalUsings.cs";

        var result = CompiledTestSourceVerifier.VerifyMethod(
            typeof(CompiledTestSourceVerifierTests).Assembly.Location,
            typeof(CompiledTestSourceVerifierTests).FullName!,
            nameof(VerifierRejectsARepositoryFileWhichDoesNotOwnTheMethod),
            RepositoryRoot,
            wrongSource,
            Digest(wrongSource));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Code, Is.EqualTo("compiled-source.method-document-mismatch"));
    }

    [Test]
    public void VerifierRejectsSourceWhosePinnedDigestDoesNotMatch()
    {
        var result = CompiledTestSourceVerifier.VerifyMethod(
            typeof(CompiledTestSourceVerifierTests).Assembly.Location,
            typeof(CompiledTestSourceVerifierTests).FullName!,
            nameof(VerifierRejectsSourceWhosePinnedDigestDoesNotMatch),
            RepositoryRoot,
            ThisSourceFile,
            "sha256:" + new string('0', 64));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Code, Is.EqualTo("compiled-source.source-digest-mismatch"));
    }

    [Test]
    public void VerifierRejectsMissingAssemblyArtifact()
    {
        var result = CompiledTestSourceVerifier.VerifyMethod(
            Path.Combine(RepositoryRoot, "artifacts", "missing", "Missing.Tests.dll"),
            typeof(CompiledTestSourceVerifierTests).FullName!,
            nameof(VerifierRejectsMissingAssemblyArtifact),
            RepositoryRoot,
            ThisSourceFile,
            Digest(ThisSourceFile));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Code, Is.EqualTo("compiled-source.assembly-missing"));
    }

    [Test]
    public void VerifierRejectsAssemblyWhoseAssociatedPdbIsMissing()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "arronix-compiled-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assemblyPath = Path.Combine(directory, "Copied.Tests.dll");
            File.Copy(typeof(CompiledTestSourceVerifierTests).Assembly.Location, assemblyPath);

            var result = CompiledTestSourceVerifier.VerifyMethod(
                assemblyPath,
                typeof(CompiledTestSourceVerifierTests).FullName!,
                nameof(VerifierRejectsAssemblyWhoseAssociatedPdbIsMissing),
                RepositoryRoot,
                ThisSourceFile,
                Digest(ThisSourceFile));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Code, Is.EqualTo("compiled-source.pdb-missing"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DocumentVerificationDoesNotRequireMethodSequencePoints()
    {
        const string supportSource = "src/Arronix.Compatibility.Ratchet.Tests/GlobalUsings.cs";

        var result = CompiledTestSourceVerifier.VerifyDocument(
            typeof(CompiledTestSourceVerifierTests).Assembly.Location,
            RepositoryRoot,
            supportSource,
            Digest(supportSource));

        Assert.That(result.IsValid, Is.True, result.Message);
    }

    [Test]
    public void VerifierRejectsLineMappedMethodWithForgedPragmaChecksum()
    {
        const string canonicalSource = "src/Forged.Tests/Canonical.cs";
        const string canonicalDigest =
            "sha256:c4f6c8abeee6e43a6c4c0e1446c09c8af173ecfe05260332f7fe7a001eb8e911";
        var forgedRepository = Path.Combine(
            Path.GetTempPath(),
            "arronix-forged-source-" + Guid.NewGuid().ToString("N"));
        var forgedSourcePath = Path.Combine(
            forgedRepository,
            canonicalSource.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(forgedSourcePath)!);
        File.WriteAllBytes(forgedSourcePath, "canonical source bytes\n"u8.ToArray());

        try
        {
            var assemblyPath = CompileForgedAssembly(forgedRepository);
            var result = CompiledTestSourceVerifier.VerifyMethod(
                assemblyPath,
                "Forged.Fixture",
                "Impostor",
                forgedRepository,
                canonicalSource,
                canonicalDigest);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Code, Is.EqualTo("compiled-source.embedded-source-missing"));
            });
        }
        finally
        {
            Directory.Delete(forgedRepository, recursive: true);
        }
    }

    private static string CompileForgedAssembly(string outputDirectory)
    {
        const string forgedSource =
            """
            #pragma checksum "./Forged.Tests/Canonical.cs" "{8829d00f-11b8-4213-878b-770e8597ac16}" "c4f6c8abeee6e43a6c4c0e1446c09c8af173ecfe05260332f7fe7a001eb8e911"
            #line 1 "./Forged.Tests/Canonical.cs"
            namespace Forged;
            public static class Fixture
            {
                public static void Impostor() { }
            }
            """;

        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("The runtime did not publish trusted platform assemblies."))
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Forged.Tests",
            [CSharpSyntaxTree.ParseText(SourceText.From(forgedSource, Encoding.UTF8), path: "Impostor.cs")],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true));
        var assemblyPath = Path.Combine(outputDirectory, "Forged.Tests.dll");
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        using var assemblyStream = File.Create(assemblyPath);
        using var pdbStream = File.Create(pdbPath);
        var emit = compilation.Emit(
            assemblyStream,
            pdbStream,
            options: new EmitOptions(
                debugInformationFormat: DebugInformationFormat.PortablePdb,
                pdbFilePath: Path.GetFileName(pdbPath)));

        Assert.That(
            emit.Success,
            Is.True,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        return assemblyPath;
    }

    private static CompiledTestSourceVerification Verify(string methodName)
        => CompiledTestSourceVerifier.VerifyMethod(
            typeof(CompiledTestSourceVerifierTests).Assembly.Location,
            typeof(CompiledTestSourceVerifierTests).FullName!,
            methodName,
            RepositoryRoot,
            ThisSourceFile,
            Digest(ThisSourceFile));

    private static IEnumerable<int> IteratorProbe()
    {
        yield return 1;
    }

    private static string Digest(string sourceFile)
    {
        using var stream = File.OpenRead(Path.Combine(RepositoryRoot, sourceFile));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the Arronix repository root.");
        }
    }
}
