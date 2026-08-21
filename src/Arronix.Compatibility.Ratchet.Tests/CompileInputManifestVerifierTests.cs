namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class CompileInputManifestVerifierTests
{
    [Test]
    public void AcceptsExactlyCompiledPrimaryAndSupportSources()
    {
        using var repository = TestRepository.Create();

        var report = repository.Verify(repository.Invocation());

        Assert.Multiple(() =>
        {
            Assert.That(report.IsValid, Is.True);
            Assert.That(report.BinaryLogCount, Is.EqualTo(1));
            Assert.That(report.ExpectedProjectCount, Is.EqualTo(1));
            Assert.That(report.BoundSourceCount, Is.EqualTo(2));
            Assert.That(report.Invocations, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RejectsAnExcludedBoundSupportSource()
    {
        using var repository = TestRepository.Create();

        var report = repository.Verify(repository.Invocation(compile: [repository.PrimarySource]));

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Code == "compile-input.binding-missing"
                && diagnostic.Message.Contains(TestRepository.SupportPath, StringComparison.Ordinal)));
    }

    [Test]
    public void RejectsAManuallyEvaluatedEmbeddedFile()
    {
        using var repository = TestRepository.Create();
        var embedded = repository.WriteFile("src/Example.Tests/Hidden.cs", "internal static class Hidden { }");

        var report = repository.Verify(repository.Invocation(embeddedFiles: [embedded]));

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Code == "compile-input.embedded-file"
                && diagnostic.Message.Contains("Hidden.cs", StringComparison.Ordinal)));
    }

    [Test]
    public void RejectsAdditionalFileSourceRemapping()
    {
        using var repository = TestRepository.Create();
        var additional = repository.WriteFile("src/Example.Tests/GeneratorInput.txt", "#line 1 \"forged.cs\"");

        var report = repository.Verify(repository.Invocation(additionalFiles: [additional]));

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Code == "compile-input.line-remap"));
    }

    [Test]
    public void RejectsAnExecutedAssemblyDifferentFromCscOutput()
    {
        using var repository = TestRepository.Create();
        File.WriteAllText(repository.ExecutedAssembly, "substituted after compilation");

        var report = repository.Verify(repository.Invocation());

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Code == "compile-input.executed-assembly-mismatch"));
    }

    [Test]
    public void RejectsUnsupportedCompilerFileInputs()
    {
        using var repository = TestRepository.Create();
        var resource = repository.WriteFile("src/Example.Tests/Forged.resources", "resource");
        var invocation = repository.Invocation() with
        {
            UnsupportedFileInputs =
            [
                new CscUnsupportedInput("Resources", repository.PathInput(resource))
            ]
        };

        var report = repository.Verify(invocation);

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Code == "compile-input.unsupported-compiler-input"));
    }

    [TestCase("  # line 200 \"forged.cs\"", "compile-input.line-remap")]
    [TestCase(
        "#pragma checksum \"forged.cs\" \"{406EA660-64CF-4C82-B6F0-42D48172A799}\" \"00\"",
        "compile-input.checksum-remap")]
    public void RejectsRepositoryCompileSourceRemappingDirectives(string directive, string expectedCode)
    {
        using var repository = TestRepository.Create(primaryContent: directive + Environment.NewLine);

        var report = repository.Verify(repository.Invocation());

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Code == expectedCode
                && diagnostic.Message.Contains(TestRepository.PrimaryPath, StringComparison.Ordinal)));
    }

    [TestCase(";CS1591;CS0436")]
    [TestCase("0436")]
    [TestCase("436")]
    public void RejectsCs0436InDisabledWarnings(string disabledWarnings)
    {
        using var repository = TestRepository.Create();

        var report = repository.Verify(repository.Invocation(disabledWarnings: [disabledWarnings]));

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Code == "compile-input.cs0436-disabled"));
    }

    [Test]
    public void RejectsCs0436InWarningsNotAsErrors()
    {
        using var repository = TestRepository.Create();

        var report = repository.Verify(repository.Invocation(warningsNotAsErrors: ["CS0436"]));

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Code == "compile-input.cs0436-not-error"));
    }

    [TestCase("#pragma warning disable CS0436")]
    [TestCase("#pragma warning disable 0436, CS0168")]
    [TestCase("#pragma warning disable // all warnings")]
    public void RejectsSourcePragmasThatCanDisableCs0436(string pragma)
    {
        using var repository = TestRepository.Create(primaryContent: pragma + Environment.NewLine);

        var report = repository.Verify(repository.Invocation());

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null && diagnostic.Code == "compile-input.cs0436-pragma"));
    }

    [TestCase("none")]
    [TestCase("silent")]
    [TestCase("suggestion")]
    public void RejectsAnalyzerConfigThatSuppressesOrDemotesCs0436(string severity)
    {
        using var repository = TestRepository.Create();
        var config = repository.WriteFile(
            ".editorconfig",
            $"root = true{Environment.NewLine}dotnet_diagnostic.CS0436.severity = {severity}{Environment.NewLine}");

        var report = repository.Verify(repository.Invocation(analyzerConfigs: [config]));

        Assert.That(
            report.Diagnostics,
            Has.One.Matches<CompatibilityDiagnostic>(diagnostic =>
                diagnostic is not null
                && diagnostic.Code == "compile-input.cs0436-analyzer-config"
                && diagnostic.Message.Contains(".editorconfig", StringComparison.Ordinal)));
    }

    [Test]
    public void DirectiveTextInsideARawStringIsNotTreatedAsACompilerDirective()
    {
        const string source =
            "internal static class Fixture\n"
            + "{\n"
            + "    private const string Text = \"\"\"\n"
            + "    #line 1 \"not-a-directive.cs\"\n"
            + "    #pragma checksum \"not-a-directive.cs\" \"{406EA660-64CF-4C82-B6F0-42D48172A799}\" \"00\"\n"
            + "    #pragma warning disable CS0436\n"
            + "    \"\"\";\n"
            + "}\n";
        using var repository = TestRepository.Create(primaryContent: source);

        var report = repository.Verify(repository.Invocation());

        Assert.That(report.IsValid, Is.True);
    }

    private sealed class TestRepository : IDisposable
    {
        internal const string ProjectPath = "src/Example.Tests/Example.Tests.csproj";
        internal const string PrimaryPath = "src/Example.Tests/Fixture.cs";
        internal const string SupportPath = "src/Example.Tests/FixtureData.cs";

        private TestRepository(
            string root,
            string sdkRoot,
            string primarySource,
            string supportSource,
            string outputAssembly,
            string executedAssembly)
        {
            Root = root;
            SdkRoot = sdkRoot;
            PrimarySource = primarySource;
            SupportSource = supportSource;
            OutputAssembly = outputAssembly;
            ExecutedAssembly = executedAssembly;

            Ledger = CompatibilityFixture.Create().Ledger with { Cases = [] };
            Sentinel = new RequiredTestSentinel(
                "proof.example",
                ProjectPath,
                "Example.Tests.Fixture.ProvesIt",
                "Example.Tests.Fixture",
                "ProvesIt",
                PrimaryPath,
                CompatibilityDigest.Sha256(File.ReadAllText(primarySource)))
            {
                SupportDocuments =
                [
                    new RequiredTestSupportDocument(
                        SupportPath,
                        CompatibilityDigest.Sha256(File.ReadAllText(supportSource)))
                ]
            };
        }

        internal string Root { get; }

        internal string PrimarySource { get; }

        internal string SupportSource { get; }

        internal string SdkRoot { get; }

        internal string OutputAssembly { get; }

        internal string ExecutedAssembly { get; }

        internal CompatibilityLedger Ledger { get; }

        internal RequiredTestSentinel Sentinel { get; }

        internal static TestRepository Create(string primaryContent = "internal static class Fixture { }")
        {
            var identifier = Guid.NewGuid().ToString("N");
            var root = Path.Combine(
                Path.GetTempPath(),
                "arronix-compile-inputs-" + identifier);
            var sdkRoot = Path.Combine(Path.GetTempPath(), "arronix-sdk-" + identifier);
            Directory.CreateDirectory(Path.Combine(root, "src", "Example.Tests"));
            Directory.CreateDirectory(Path.Combine(sdkRoot, "Roslyn"));
            File.WriteAllText(Path.Combine(root, ProjectPath), "<Project />");
            File.WriteAllText(Path.Combine(sdkRoot, "Roslyn", "Microsoft.CSharp.Core.targets"), "sdk target");
            File.WriteAllText(
                Path.Combine(sdkRoot, "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll"),
                "sdk task");
            var primary = Path.Combine(root, PrimaryPath);
            var support = Path.Combine(root, SupportPath);
            var outputAssembly = Path.Combine(
                root,
                "src",
                "Example.Tests",
                "obj",
                "Release",
                "net11.0",
                "Example.Tests.dll");
            var executedAssembly = Path.Combine(
                root,
                "src",
                "Example.Tests",
                "bin",
                "Release",
                "net11.0",
                "Example.Tests.dll");
            File.WriteAllText(primary, primaryContent);
            File.WriteAllText(support, "internal static class FixtureData { }");
            Directory.CreateDirectory(Path.GetDirectoryName(outputAssembly)!);
            Directory.CreateDirectory(Path.GetDirectoryName(executedAssembly)!);
            File.WriteAllText(outputAssembly, "same compiled assembly");
            File.WriteAllText(executedAssembly, "same compiled assembly");
            return new TestRepository(
                root,
                sdkRoot,
                primary,
                support,
                outputAssembly,
                executedAssembly);
        }

        internal CscCompileInvocation Invocation(
            IReadOnlyList<string>? compile = null,
            IReadOnlyList<string>? additionalFiles = null,
            IReadOnlyList<string>? embeddedFiles = null,
            IReadOnlyList<string>? disabledWarnings = null,
            IReadOnlyList<string>? warningsNotAsErrors = null,
            IReadOnlyList<string>? analyzerConfigs = null)
            => new(
                "test-context",
                ProjectPath,
                Path.Combine(Root, ProjectPath),
                Succeeded: true,
                SdkRoot,
                TaskDefinitionFiles:
                [
                    PathInput(Path.Combine(SdkRoot, "Roslyn", "Microsoft.CSharp.Core.targets"))
                ],
                TaskAssemblyLocations:
                [
                    PathInput(Path.Combine(SdkRoot, "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll"))
                ],
                (compile ?? [PrimarySource, SupportSource]).Select(PathInput).ToArray(),
                (additionalFiles ?? []).Select(PathInput).ToArray(),
                (embeddedFiles ?? []).Select(PathInput).ToArray(),
                OutputAssemblies: [PathInput(OutputAssembly)],
                SourceLinkFiles: [],
                disabledWarnings ?? [],
                WarningsAsErrors: [],
                warningsNotAsErrors ?? [],
                (analyzerConfigs ?? []).Select(PathInput).ToArray(),
                References:
                [
                    new CscReferenceInput(
                        "External.dll",
                        Path.Combine(Root, "External.dll"),
                        "External.dll",
                        ["proof"])
                ],
                Analyzers: [],
                UnsupportedFileInputs: [],
                EmbedAllSources: true,
                Deterministic: true,
                TreatWarningsAsErrors: true,
                NoConfig: true);

        internal CompileInputVerificationReport Verify(CscCompileInvocation invocation)
            => CompileInputManifestVerifier.VerifyInvocations(
                Root,
                [invocation],
                Ledger,
                [Sentinel],
                new NUnitTestRun(
                [
                    new NUnitProjectResult("Example.Tests.dll", "in-memory", [])
                    {
                        AssemblyPath = ExecutedAssembly
                    }
                ]));

        internal string WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
            Directory.Delete(SdkRoot, recursive: true);
        }

        internal CscPathInput PathInput(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            var repositoryPath = relative.StartsWith("../", StringComparison.Ordinal) ? null : relative;
            return new CscPathInput(path, fullPath, repositoryPath);
        }
    }
}
