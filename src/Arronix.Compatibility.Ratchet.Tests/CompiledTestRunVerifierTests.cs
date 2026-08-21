using System.Security.Cryptography;

namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class CompiledTestRunVerifierTests
{
    private const string Project =
        "src/Arronix.Compatibility.Ratchet.Tests/Arronix.Compatibility.Ratchet.Tests.csproj";
    private const string SourceFile =
        "src/Arronix.Compatibility.Ratchet.Tests/CompiledTestRunVerifierTests.cs";

    [Test]
    public void VerifiesARequiredPassingLeafAgainstItsExecutedAssemblyAndPdb()
    {
        var fixture = typeof(CompiledTestRunVerifierTests).FullName!;
        var method = nameof(VerifiesARequiredPassingLeafAgainstItsExecutedAssemblyAndPdb);
        var fullName = fixture + "." + method;
        var sentinel = new RequiredTestSentinel(
            "proof.compiled-binding",
            Project,
            fullName,
            fixture,
            method,
            SourceFile,
            Digest(SourceFile));
        var run = Run(fullName, NUnitTestOutcome.Passed, typeof(CompiledTestRunVerifierTests).Assembly.Location);

        var result = CompiledTestRunVerifier.VerifySentinels(RepositoryRoot, [sentinel], run).Single();

        Assert.That(result.IsValid, Is.True, result.Message);
    }

    [Test]
    public void RejectsARequiredLeafWhoseSupportDocumentIsNotPinned()
    {
        var fixture = typeof(CompiledTestRunVerifierTests).FullName!;
        var method = nameof(RejectsARequiredLeafWhoseSupportDocumentIsNotPinned);
        var fullName = fixture + "." + method;
        var sentinel = new RequiredTestSentinel(
            "proof.compiled-binding",
            Project,
            fullName,
            fixture,
            method,
            SourceFile,
            Digest(SourceFile))
        {
            SupportDocuments =
            [
                new RequiredTestSupportDocument(
                    "src/Arronix.Compatibility.Ratchet.Tests/GlobalUsings.cs",
                    "sha256:" + new string('0', 64))
            ]
        };
        var run = Run(fullName, NUnitTestOutcome.Passed, typeof(CompiledTestRunVerifierTests).Assembly.Location);

        var result = CompiledTestRunVerifier.VerifySentinels(RepositoryRoot, [sentinel], run).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Code, Is.EqualTo("compiled-source.source-digest-mismatch"));
        });
    }

    [Test]
    public void RejectsARequiredLeafWhichDidNotPass()
    {
        var fixture = typeof(CompiledTestRunVerifierTests).FullName!;
        var method = nameof(RejectsARequiredLeafWhichDidNotPass);
        var fullName = fixture + "." + method;
        var sentinel = new RequiredTestSentinel(
            "proof.compiled-binding",
            Project,
            fullName,
            fixture,
            method,
            SourceFile,
            Digest(SourceFile));
        var run = Run(fullName, NUnitTestOutcome.Skipped, typeof(CompiledTestRunVerifierTests).Assembly.Location);

        var result = CompiledTestRunVerifier.VerifySentinels(RepositoryRoot, [sentinel], run).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Code, Is.EqualTo("required-test.execution-not-passed"));
        });
    }

    [Test]
    public void RejectsARequiredLeafWhoseCustomNameDoesNotIdentifyTheBoundMethod()
    {
        var fixture = typeof(CompiledTestRunVerifierTests).FullName!;
        var method = nameof(RejectsARequiredLeafWhoseCustomNameDoesNotIdentifyTheBoundMethod);
        var customFullName = fixture + ".A convincing but unrelated custom name";
        var sentinel = new RequiredTestSentinel(
            "proof.compiled-binding",
            Project,
            customFullName,
            fixture,
            method,
            SourceFile,
            Digest(SourceFile));
        var run = Run(customFullName, NUnitTestOutcome.Passed, typeof(CompiledTestRunVerifierTests).Assembly.Location);

        var result = CompiledTestRunVerifier.VerifySentinels(RepositoryRoot, [sentinel], run).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Code, Is.EqualTo("required-test.execution-method-mismatch"));
        });
    }

    [Test]
    public void RejectsACompatibilityLeafWhoseCustomNameDoesNotIdentifyTheBoundMethod()
    {
        var state = CompatibilityFixture.Create();
        var customFullName = CompatibilityFixture.Fixture + ".A convincing but unrelated custom name";
        var cases = state.Ledger.Cases.ToArray();
        cases[0] = cases[0] with
        {
            Binding = cases[0].Binding with
            {
                Method = "BoundMethod",
                FullNameDigest = CompatibilityDigest.Sha256(customFullName)
            }
        };
        var tests = state.Run.Tests.ToArray();
        tests[0] = tests[0] with
        {
            Name = "A convincing but unrelated custom name",
            FullName = customFullName
        };
        var run = new NUnitTestRun(
        [
            new NUnitProjectResult(CompatibilityFixture.Assembly, "in-memory", tests)
        ]);

        var result = CompiledTestRunVerifier.VerifyCases(
            RepositoryRoot,
            state.Ledger with { Cases = cases },
            run)[cases[0].CaseId];

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Code, Is.EqualTo("compiled-source.execution-method-mismatch"));
        });
    }

    [Test]
    public void RejectsAnAssemblyOutsideTheBoundProjectsOutputTree()
    {
        var fixture = typeof(CompiledTestRunVerifierTests).FullName!;
        var method = nameof(RejectsAnAssemblyOutsideTheBoundProjectsOutputTree);
        var fullName = fixture + "." + method;
        var sentinel = new RequiredTestSentinel(
            "proof.compiled-binding",
            Project,
            fullName,
            fixture,
            method,
            SourceFile,
            Digest(SourceFile));
        var run = Run(
            fullName,
            NUnitTestOutcome.Passed,
            Path.Combine(Path.GetTempPath(), "Arronix.Compatibility.Ratchet.Tests.dll"));

        var result = CompiledTestRunVerifier.VerifySentinels(RepositoryRoot, [sentinel], run).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Code, Is.EqualTo("compiled-source.assembly-path-mismatch"));
        });
    }

    private static NUnitTestRun Run(string fullName, NUnitTestOutcome outcome, string assemblyPath)
        => new(
        [
            new NUnitProjectResult(
                "Arronix.Compatibility.Ratchet.Tests.dll",
                "in-memory",
                [
                    new NUnitTestCaseResult(
                        "Arronix.Compatibility.Ratchet.Tests.dll",
                        fullName[(fullName.LastIndexOf('.') + 1)..],
                        fullName,
                        outcome)
                ])
            {
                AssemblyPath = assemblyPath
            }
        ]);

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
