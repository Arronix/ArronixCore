namespace Arronix.Compatibility.Ratchet;

/// <summary>Connects NUnit execution records to their exact compiled source provenance.</summary>
public static class CompiledTestRunVerifier
{
    /// <summary>Verifies the compiled method and support documents for every registered case.</summary>
    public static IReadOnlyDictionary<string, CompiledTestSourceVerification> VerifyCases(
        string repositoryRoot,
        CompatibilityLedger ledger,
        NUnitTestRun run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(run);

        var projects = IndexProjects(run);
        var methodCache = new Dictionary<MethodVerificationKey, CompiledTestSourceVerification>();
        var documentCache = new Dictionary<DocumentVerificationKey, CompiledTestSourceVerification>();
        var result = new Dictionary<string, CompiledTestSourceVerification>(StringComparer.Ordinal);
        foreach (var compatibilityCase in ledger.Cases)
        {
            var execution = ResolveCaseExecution(
                compatibilityCase.Binding.Project,
                compatibilityCase.Binding.FullNameDigest,
                projects);
            if (execution.Failure is not null)
            {
                result[compatibilityCase.CaseId] = execution.Failure;
                continue;
            }

            if (!IsBoundMethod(
                    execution.Test!.FullName,
                    compatibilityCase.Binding.Fixture,
                    compatibilityCase.Binding.Method))
            {
                result[compatibilityCase.CaseId] = new CompiledTestSourceVerification(
                    false,
                    "compiled-source.execution-method-mismatch",
                    $"Registered execution '{execution.Test.FullName}' is not the bound method " +
                    $"'{compatibilityCase.Binding.Fixture}.{compatibilityCase.Binding.Method}'.");
                continue;
            }

            var assembly = ResolveAssembly(
                repositoryRoot,
                compatibilityCase.Binding.Project,
                projects);
            if (assembly.Failure is not null)
            {
                result[compatibilityCase.CaseId] = assembly.Failure;
                continue;
            }

            var key = new MethodVerificationKey(
                assembly.Path,
                compatibilityCase.Binding.Fixture,
                compatibilityCase.Binding.Method,
                compatibilityCase.Binding.SourceFile,
                compatibilityCase.Binding.SourceFileDigest);
            if (!methodCache.TryGetValue(key, out var verification))
            {
                verification = CompiledTestSourceVerifier.VerifyMethod(
                    key.AssemblyPath,
                    key.Fixture,
                    key.Method,
                    repositoryRoot,
                    key.SourceFile,
                    key.SourceFileDigest);
                methodCache.Add(key, verification);
            }

            if (verification.IsValid)
            {
                foreach (var supportDocument in compatibilityCase.Binding.SupportDocuments)
                {
                    var documentKey = new DocumentVerificationKey(
                        assembly.Path,
                        supportDocument.SourceFile,
                        supportDocument.SourceFileDigest);
                    if (!documentCache.TryGetValue(documentKey, out var documentVerification))
                    {
                        documentVerification = CompiledTestSourceVerifier.VerifyDocument(
                            documentKey.AssemblyPath,
                            repositoryRoot,
                            documentKey.SourceFile,
                            documentKey.SourceFileDigest);
                        documentCache.Add(documentKey, documentVerification);
                    }

                    if (!documentVerification.IsValid)
                    {
                        verification = documentVerification;
                        break;
                    }
                }
            }

            result[compatibilityCase.CaseId] = verification;
        }

        return result;
    }

    /// <summary>Verifies required passing tests and their compiled source provenance.</summary>
    public static IReadOnlyList<RequiredTestSentinelVerification> VerifySentinels(
        string repositoryRoot,
        IReadOnlyList<RequiredTestSentinel> sentinels,
        NUnitTestRun run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(sentinels);
        ArgumentNullException.ThrowIfNull(run);

        var projects = IndexProjects(run);
        var methodCache = new Dictionary<MethodVerificationKey, CompiledTestSourceVerification>();
        var documentCache = new Dictionary<DocumentVerificationKey, CompiledTestSourceVerification>();
        var result = new List<RequiredTestSentinelVerification>(sentinels.Count);
        foreach (var sentinel in sentinels)
        {
            var projectKey = ProjectKey(sentinel.Project);
            if (!projects.TryGetValue(projectKey, out var candidates) || candidates.Count == 0)
            {
                result.Add(Failure(
                    sentinel.Id,
                    "required-test.project-result-missing",
                    $"Required test '{sentinel.Id}' has no NUnit result for project '{projectKey}'."));
                continue;
            }

            if (candidates.Count != 1)
            {
                result.Add(Failure(
                    sentinel.Id,
                    "required-test.project-result-ambiguous",
                    $"Required test '{sentinel.Id}' has more than one NUnit result for project '{projectKey}'."));
                continue;
            }

            var executions = candidates[0].Tests
                .Where(test => string.Equals(test.FullName, sentinel.FullName, StringComparison.Ordinal))
                .ToArray();
            if (executions.Length != 1)
            {
                result.Add(Failure(
                    sentinel.Id,
                    executions.Length == 0
                        ? "required-test.execution-missing"
                        : "required-test.execution-ambiguous",
                    $"Required test '{sentinel.Id}' did not produce exactly one NUnit leaf '{sentinel.FullName}'."));
                continue;
            }

            if (executions[0].Outcome != NUnitTestOutcome.Passed)
            {
                result.Add(Failure(
                    sentinel.Id,
                    "required-test.execution-not-passed",
                    $"Required test '{sentinel.Id}' executed as {executions[0].Outcome}, not Passed."));
                continue;
            }

            if (!IsBoundMethod(executions[0].FullName, sentinel.Fixture, sentinel.Method))
            {
                result.Add(Failure(
                    sentinel.Id,
                    "required-test.execution-method-mismatch",
                    $"Required test execution '{executions[0].FullName}' is not the bound method " +
                    $"'{sentinel.Fixture}.{sentinel.Method}'."));
                continue;
            }

            var assembly = ResolveAssembly(repositoryRoot, sentinel.Project, projects);
            if (assembly.Failure is not null)
            {
                result.Add(new RequiredTestSentinelVerification(
                    sentinel.Id,
                    assembly.Failure.IsValid,
                    assembly.Failure.Code,
                    assembly.Failure.Message));
                continue;
            }

            var key = new MethodVerificationKey(
                assembly.Path,
                sentinel.Fixture,
                sentinel.Method,
                sentinel.SourceFile,
                sentinel.SourceFileDigest);
            if (!methodCache.TryGetValue(key, out var verification))
            {
                verification = CompiledTestSourceVerifier.VerifyMethod(
                    key.AssemblyPath,
                    key.Fixture,
                    key.Method,
                    repositoryRoot,
                    key.SourceFile,
                    key.SourceFileDigest);
                methodCache.Add(key, verification);
            }

            if (verification.IsValid)
            {
                foreach (var supportDocument in sentinel.SupportDocuments)
                {
                    var documentKey = new DocumentVerificationKey(
                        assembly.Path,
                        supportDocument.SourceFile,
                        supportDocument.SourceFileDigest);
                    if (!documentCache.TryGetValue(documentKey, out var documentVerification))
                    {
                        documentVerification = CompiledTestSourceVerifier.VerifyDocument(
                            documentKey.AssemblyPath,
                            repositoryRoot,
                            documentKey.SourceFile,
                            documentKey.SourceFileDigest);
                        documentCache.Add(documentKey, documentVerification);
                    }

                    if (!documentVerification.IsValid)
                    {
                        verification = documentVerification;
                        break;
                    }
                }
            }

            result.Add(new RequiredTestSentinelVerification(
                sentinel.Id,
                verification.IsValid,
                verification.Code,
                verification.Message));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<NUnitProjectResult>> IndexProjects(NUnitTestRun run)
        => run.Projects
            .GroupBy(static value => ProjectKey(value.Project), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<NUnitProjectResult>)group.ToArray(),
                StringComparer.Ordinal);

    private static ResolvedExecution ResolveCaseExecution(
        string project,
        string fullNameDigest,
        IReadOnlyDictionary<string, IReadOnlyList<NUnitProjectResult>> projects)
    {
        var projectKey = ProjectKey(project);
        if (!projects.TryGetValue(projectKey, out var candidates) || candidates.Count == 0)
        {
            return ResolvedExecution.Failed(new CompiledTestSourceVerification(
                false,
                "compiled-source.project-result-missing",
                $"No NUnit result identifies project '{projectKey}'."));
        }

        if (candidates.Count != 1)
        {
            return ResolvedExecution.Failed(new CompiledTestSourceVerification(
                false,
                "compiled-source.project-result-ambiguous",
                $"More than one NUnit result identifies project '{projectKey}'."));
        }

        var executions = candidates[0].Tests
            .Where(test => string.Equals(
                CompatibilityDigest.Sha256(test.FullName),
                fullNameDigest,
                StringComparison.Ordinal))
            .ToArray();
        if (executions.Length != 1)
        {
            return ResolvedExecution.Failed(new CompiledTestSourceVerification(
                false,
                executions.Length == 0
                    ? "compiled-source.execution-missing"
                    : "compiled-source.execution-ambiguous",
                $"The bound digest did not resolve to exactly one NUnit leaf in project '{projectKey}'."));
        }

        return new ResolvedExecution(executions[0], null);
    }

    private static bool IsBoundMethod(string fullName, string fixture, string method)
    {
        var expected = fixture + "." + method;
        return string.Equals(fullName, expected, StringComparison.Ordinal)
            || fullName.StartsWith(expected + "(", StringComparison.Ordinal);
    }

    private static ResolvedAssembly ResolveAssembly(
        string repositoryRoot,
        string project,
        IReadOnlyDictionary<string, IReadOnlyList<NUnitProjectResult>> projects)
    {
        var projectKey = ProjectKey(project);
        if (!projects.TryGetValue(projectKey, out var candidates) || candidates.Count == 0)
        {
            return ResolvedAssembly.Failed(new CompiledTestSourceVerification(
                false,
                "compiled-source.project-result-missing",
                $"No NUnit result identifies project '{projectKey}'."));
        }

        if (candidates.Count != 1)
        {
            return ResolvedAssembly.Failed(new CompiledTestSourceVerification(
                false,
                "compiled-source.project-result-ambiguous",
                $"More than one NUnit result identifies project '{projectKey}'."));
        }

        var assemblyPath = candidates[0].AssemblyPath;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !Path.IsPathFullyQualified(assemblyPath))
        {
            return ResolvedAssembly.Failed(new CompiledTestSourceVerification(
                false,
                "compiled-source.assembly-path-missing",
                $"NUnit did not report an absolute executed assembly path for project '{projectKey}'."));
        }

        var root = Path.GetFullPath(repositoryRoot);
        var projectPath = Path.GetFullPath(project.Replace('/', Path.DirectorySeparatorChar), root);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var expectedBinRoot = Path.GetFullPath(Path.Combine(projectDirectory, "bin")) + Path.DirectorySeparatorChar;
        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        var expectedAssemblyName = Path.GetFileNameWithoutExtension(project) + ".dll";
        if (!fullAssemblyPath.StartsWith(expectedBinRoot, StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(fullAssemblyPath),
                expectedAssemblyName,
                StringComparison.Ordinal))
        {
            return ResolvedAssembly.Failed(new CompiledTestSourceVerification(
                false,
                "compiled-source.assembly-path-mismatch",
                $"NUnit assembly '{fullAssemblyPath}' is not the '{expectedAssemblyName}' output beneath the bound project's bin directory."));
        }

        return new ResolvedAssembly(fullAssemblyPath, null);
    }

    private static RequiredTestSentinelVerification Failure(string id, string code, string message)
        => new(id, false, code, message);

    private static string ProjectKey(string value)
        => Path.GetFileNameWithoutExtension(value.Replace('\\', '/'));

    private sealed record ResolvedAssembly(
        string Path,
        CompiledTestSourceVerification? Failure)
    {
        public static ResolvedAssembly Failed(CompiledTestSourceVerification failure)
            => new(string.Empty, failure);
    }

    private sealed record ResolvedExecution(
        NUnitTestCaseResult? Test,
        CompiledTestSourceVerification? Failure)
    {
        public static ResolvedExecution Failed(CompiledTestSourceVerification failure)
            => new(null, failure);
    }

    private sealed record MethodVerificationKey(
        string AssemblyPath,
        string Fixture,
        string Method,
        string SourceFile,
        string SourceFileDigest);

    private sealed record DocumentVerificationKey(
        string AssemblyPath,
        string SourceFile,
        string SourceFileDigest);
}

/// <summary>The outcome of verifying one required proof sentinel.</summary>
public sealed record RequiredTestSentinelVerification(
    string Id,
    bool IsValid,
    string Code,
    string Message);
