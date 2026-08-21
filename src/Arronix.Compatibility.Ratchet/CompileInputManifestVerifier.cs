using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging.StructuredLogger;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Proves which inputs the C# compiler actually received for every test project.</summary>
public static class CompileInputManifestVerifier
{
    /// <summary>
    /// Reads the binary log written by the complete solution build and validates the recorded <c>Csc</c>
    /// task inputs against every compatibility and permanent-sentinel source binding.
    /// </summary>
    public static CompileInputVerificationReport Verify(
        string repositoryRoot,
        string evidenceDirectory,
        CompatibilityLedger ledger,
        IReadOnlyList<RequiredTestSentinel> requiredTests,
        NUnitTestRun run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(requiredTests);
        ArgumentNullException.ThrowIfNull(run);

        var root = ValidateRepositoryRoot(repositoryRoot);
        var evidenceRoot = Path.GetFullPath(evidenceDirectory);
        if (!Directory.Exists(evidenceRoot))
        {
            throw new DirectoryNotFoundException(
                $"The compile-input evidence directory '{evidenceDirectory}' does not exist.");
        }

        var diagnostics = new List<CompatibilityDiagnostic>();
        var expectedProjects = DiscoverTestProjects(root);
        var binaryLogs = Directory
            .EnumerateFiles(evidenceRoot, "*.binlog", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

        IReadOnlyList<CscCompileInvocation> invocations = [];
        if (binaryLogs.Length == 0)
        {
            Add(
                diagnostics,
                "compile-input.binary-log-missing",
                $"The compile-input directory '{evidenceDirectory}' contains no solution-build binary log.");
        }
        else if (binaryLogs.Length != 1)
        {
            Add(
                diagnostics,
                "compile-input.binary-log-ambiguous",
                $"The compile-input directory '{evidenceDirectory}' contains {binaryLogs.Length} binary logs; exactly one is required.");
        }
        else
        {
            invocations = ReadBinaryLog(root, binaryLogs[0], expectedProjects, diagnostics);
        }

        return VerifyCore(
            root,
            binaryLogs.Length,
            expectedProjects,
            invocations,
            ledger,
            requiredTests,
            run,
            diagnostics);
    }

    /// <summary>Validates already-decoded compiler invocations. Intended for deterministic verifier tests.</summary>
    public static CompileInputVerificationReport VerifyInvocations(
        string repositoryRoot,
        IReadOnlyList<CscCompileInvocation> invocations,
        CompatibilityLedger ledger,
        IReadOnlyList<RequiredTestSentinel> requiredTests,
        NUnitTestRun run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(invocations);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(requiredTests);
        ArgumentNullException.ThrowIfNull(run);

        var root = ValidateRepositoryRoot(repositoryRoot);
        return VerifyCore(
            root,
            binaryLogCount: 1,
            DiscoverTestProjects(root),
            invocations,
            ledger,
            requiredTests,
            run,
            []);
    }

    private static CompileInputVerificationReport VerifyCore(
        string repositoryRoot,
        int binaryLogCount,
        HashSet<string> expectedProjects,
        IReadOnlyList<CscCompileInvocation> invocations,
        CompatibilityLedger ledger,
        IReadOnlyList<RequiredTestSentinel> requiredTests,
        NUnitTestRun run,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (expectedProjects.Count == 0)
        {
            Add(
                diagnostics,
                "compile-input.project-inventory-empty",
                "The repository contains no discoverable src/**/*.Tests.csproj projects.");
        }

        var invocationsByProject = invocations
            .GroupBy(static value => value.Project, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.Ordinal);

        foreach (var project in expectedProjects.Order(StringComparer.Ordinal))
        {
            if (!invocationsByProject.TryGetValue(project, out var projectInvocations))
            {
                Add(
                    diagnostics,
                    "compile-input.compilation-missing",
                    $"Test project '{project}' has no recorded Csc invocation in the solution-build binary log.");
                continue;
            }

            if (projectInvocations.Length != 1)
            {
                Add(
                    diagnostics,
                    "compile-input.compilation-ambiguous",
                    $"Test project '{project}' has {projectInvocations.Length} recorded Csc invocations; exactly one is required.");
            }
        }

        foreach (var invocation in invocations.Where(value => !expectedProjects.Contains(value.Project)))
        {
            Add(
                diagnostics,
                "compile-input.compilation-unexpected-project",
                $"Compiler invocation '{invocation.InvocationId}' identifies '{invocation.Project}', which is not a discovered test project.");
        }

        foreach (var invocation in invocations)
        {
            ValidateInvocation(repositoryRoot, invocation, diagnostics);
        }

        var bindings = EnumerateBindings(ledger, requiredTests)
            .GroupBy(static value => new BindingKey(value.Project, value.SourceFile))
            .Select(static group => new CompileInputBinding(
                group.Key.Project,
                group.Key.SourceFile,
                group.Select(static value => value.Owner)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(static value => value.Project, StringComparer.Ordinal)
            .ThenBy(static value => value.SourceFile, StringComparer.Ordinal)
            .ToArray();

        foreach (var binding in bindings)
        {
            if (!invocationsByProject.TryGetValue(binding.Project, out var projectInvocations)
                || projectInvocations.Length != 1)
            {
                continue;
            }

            var matches = projectInvocations[0].Sources.Count(item => string.Equals(
                item.RepositoryPath,
                binding.SourceFile,
                StringComparison.Ordinal));
            if (matches == 0)
            {
                Add(
                    diagnostics,
                    "compile-input.binding-missing",
                    $"Bound source '{binding.SourceFile}' is not an actual Csc Sources input for "
                    + $"'{binding.Project}' (required by {string.Join(", ", binding.Owners)}).");
            }
            else if (matches != 1)
            {
                Add(
                    diagnostics,
                    "compile-input.binding-ambiguous",
                    $"Bound source '{binding.SourceFile}' occurs {matches} times in the actual Csc Sources inputs for "
                    + $"'{binding.Project}' (required by {string.Join(", ", binding.Owners)}).");
            }
        }

        ScanRepositoryCompileSources(repositoryRoot, invocations, diagnostics);
        ScanAnalyzerConfigs(invocations, diagnostics);
        BindExecutedAssemblies(repositoryRoot, invocationsByProject, expectedProjects, run, diagnostics);

        return new CompileInputVerificationReport(
            diagnostics
                .OrderBy(static value => value.Code, StringComparer.Ordinal)
                .ThenBy(static value => value.Message, StringComparer.Ordinal)
                .ToArray(),
            binaryLogCount,
            expectedProjects.Count,
            invocations.Sum(static value => value.Sources.Count),
            bindings.Length,
            invocations.OrderBy(static value => value.Project, StringComparer.Ordinal).ToArray());
    }

    private static void ValidateInvocation(
        string repositoryRoot,
        CscCompileInvocation invocation,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (!invocation.Succeeded)
        {
            Add(
                diagnostics,
                "compile-input.compilation-unsuccessful",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' did not finish successfully.");
        }

        if (invocation.Sources.Count == 0)
        {
            Add(
                diagnostics,
                "compile-input.sources-empty",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' records no Sources inputs.");
        }

        if (invocation.EmbedAllSources is not true)
        {
            Add(
                diagnostics,
                "compile-input.embed-all-sources-disabled",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' does not state exactly one true EmbedAllSources input.");
        }

        if (invocation.Deterministic is not true)
        {
            Add(
                diagnostics,
                "compile-input.nondeterministic",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' does not state exactly one true Deterministic input.");
        }

        if (invocation.TreatWarningsAsErrors is not true)
        {
            Add(
                diagnostics,
                "compile-input.warnings-not-errors",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' does not state exactly one true TreatWarningsAsErrors input.");
        }

        if (invocation.NoConfig is not true)
        {
            Add(
                diagnostics,
                "compile-input.compiler-response-config-enabled",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' does not state exactly one true NoConfig input.");
        }

        ValidateSdkTask(repositoryRoot, invocation, diagnostics);

        foreach (var embeddedFile in invocation.EmbeddedFiles)
        {
            Add(
                diagnostics,
                "compile-input.embedded-file",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' receives explicit EmbeddedFiles input "
                + $"'{embeddedFile.DisplayPath}'.");
        }

        foreach (var unsupported in invocation.UnsupportedFileInputs)
        {
            Add(
                diagnostics,
                "compile-input.unsupported-compiler-input",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' receives unsupported "
                + $"{unsupported.Parameter} input '{unsupported.Input.DisplayPath}'.");
        }

        if (ContainsWarningCode(invocation.DisabledWarnings))
        {
            Add(
                diagnostics,
                "compile-input.cs0436-disabled",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' disables compiler warning CS0436.");
        }

        if (ContainsWarningCode(invocation.WarningsNotAsErrors))
        {
            Add(
                diagnostics,
                "compile-input.cs0436-not-error",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' exempts compiler warning CS0436 from warnings-as-errors.");
        }
    }

    private static void ValidateSdkTask(
        string repositoryRoot,
        CscCompileInvocation invocation,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(invocation.SdkRoot))
        {
            Add(
                diagnostics,
                "compile-input.sdk-root-missing",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' has no attested SDK root.");
            return;
        }

        var sdkRoot = Path.GetFullPath(invocation.SdkRoot);
        if (TryRepositoryPath(repositoryRoot, sdkRoot, out _))
        {
            Add(
                diagnostics,
                "compile-input.sdk-root-repository-owned",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' uses repository-owned SDK root '{sdkRoot}'.");
        }

        var expectedTaskDefinition = Path.GetFullPath(
            Path.Combine(sdkRoot, "Roslyn", "Microsoft.CSharp.Core.targets"));
        var expectedTaskAssembly = Path.GetFullPath(
            Path.Combine(sdkRoot, "Roslyn", "Microsoft.Build.Tasks.CodeAnalysis.dll"));
        ValidateExactSdkPath(
            invocation,
            "task definition",
            invocation.TaskDefinitionFiles,
            expectedTaskDefinition,
            diagnostics);
        ValidateExactSdkPath(
            invocation,
            "task assembly",
            invocation.TaskAssemblyLocations,
            expectedTaskAssembly,
            diagnostics);
    }

    private static void ValidateExactSdkPath(
        CscCompileInvocation invocation,
        string kind,
        IReadOnlyList<CscPathInput> values,
        string expected,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (values.Count != 1
            || !string.Equals(values[0].FullPath, expected, PathComparison)
            || !File.Exists(expected))
        {
            Add(
                diagnostics,
                "compile-input.csc-task-not-sdk",
                $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' records {values.Count} "
                + $"{kind} paths; exactly '{expected}' must exist and be used.");
        }
    }

    private static IReadOnlyList<CscCompileInvocation> ReadBinaryLog(
        string repositoryRoot,
        string binaryLog,
        IReadOnlySet<string> expectedProjects,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var builders = new Dictionary<TaskContextKey, InvocationBuilder>();
        var taskAssemblies = new Dictionary<TargetContextKey, List<string>>();
        var buildEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recoverableErrors = new List<string>();
        var readerExceptions = new List<string>();
        var buildStartedCount = 0;
        var buildFinishedCount = 0;
        var buildSucceeded = false;
        var reader = new BinLogReader();
        reader.RecoverableReadError += error =>
            recoverableErrors.Add($"{error.ErrorType} ({error.RecordKind})");
        reader.OnException += exception => readerExceptions.Add(exception.Message);
        try
        {
            foreach (var record in reader.ReadRecords(binaryLog))
            {
                switch (record.Args)
                {
                    case BuildStartedEventArgs started:
                        buildStartedCount++;
                        if (started.BuildEnvironment is not null)
                        {
                            foreach (var pair in started.BuildEnvironment)
                            {
                                buildEnvironment[pair.Key] = pair.Value;
                            }
                        }

                        break;
                    case BuildFinishedEventArgs finished:
                        buildFinishedCount++;
                        buildSucceeded = finished.Succeeded;
                        break;
                    case BuildMessageEventArgs message
                        when TryReadCscTaskAssembly(message.Message, out var taskAssembly)
                            && TryTargetContextKey(message.BuildEventContext, out var assemblyContext):
                        if (!taskAssemblies.TryGetValue(assemblyContext, out var locations))
                        {
                            locations = [];
                            taskAssemblies.Add(assemblyContext, locations);
                        }

                        locations.Add(taskAssembly);
                        break;
                    case TaskStartedEventArgs started
                        when string.Equals(started.TaskName, "Csc", StringComparison.Ordinal):
                        ReadTaskStart(repositoryRoot, expectedProjects, started, builders, diagnostics);
                        break;
                    case TaskParameterEventArgs2 parameter
                        when parameter.Kind == TaskParameterMessageKind.TaskInput:
                        if (TryContextKey(parameter.BuildEventContext, out var parameterKey)
                            && builders.TryGetValue(parameterKey, out var builder))
                        {
                            builder.AddParameter(parameter);
                        }

                        break;
                    case TaskFinishedEventArgs finished
                        when string.Equals(finished.TaskName, "Csc", StringComparison.Ordinal):
                        if (TryContextKey(finished.BuildEventContext, out var finishedKey)
                            && builders.TryGetValue(finishedKey, out var finishedBuilder))
                        {
                            if (finishedBuilder.Finished)
                            {
                                Add(
                                    diagnostics,
                                    "compile-input.binary-log-task-finished-twice",
                                    $"Binary log '{binaryLog}' finishes Csc invocation '{finishedBuilder.InvocationId}' more than once.");
                            }

                            finishedBuilder.Finished = true;
                            finishedBuilder.Succeeded = finished.Succeeded;
                        }

                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or FormatException
            or NullReferenceException
            or InvalidCastException)
        {
            Add(
                diagnostics,
                "compile-input.binary-log-invalid",
                $"Compile-input binary log '{binaryLog}' cannot be replayed: {exception.Message}");
            return [];
        }

        if (reader.HasEncounteredTruncation)
        {
            Add(
                diagnostics,
                "compile-input.binary-log-truncated",
                $"Compile-input binary log '{binaryLog}' ended after a truncated record.");
        }

        foreach (var error in recoverableErrors.Distinct(StringComparer.Ordinal))
        {
            Add(
                diagnostics,
                "compile-input.binary-log-recoverable-error",
                $"Compile-input binary log '{binaryLog}' required recoverable parsing: {error}.");
        }

        foreach (var error in readerExceptions.Distinct(StringComparer.Ordinal))
        {
            Add(
                diagnostics,
                "compile-input.binary-log-reader-error",
                $"Compile-input binary log '{binaryLog}' raised a reader exception: {error}");
        }

        if (buildStartedCount != 1)
        {
            Add(
                diagnostics,
                "compile-input.build-start-ambiguous",
                $"Compile-input binary log '{binaryLog}' records {buildStartedCount} BuildStarted events; exactly one is required.");
        }

        if (buildFinishedCount != 1 || !buildSucceeded)
        {
            Add(
                diagnostics,
                "compile-input.build-not-successful",
                $"Compile-input binary log '{binaryLog}' records {buildFinishedCount} BuildFinished events "
                + $"and successful={buildSucceeded}; exactly one successful completion is required.");
        }

        var sdkRoot = ReadSdkRoot(buildEnvironment, binaryLog, diagnostics);
        return builders.Values
            .Select(builder => builder.Build(
                repositoryRoot,
                sdkRoot,
                taskAssemblies.TryGetValue(builder.TargetContext, out var locations) ? locations : [],
                diagnostics))
            .Where(static value => value is not null)
            .Select(static value => value!)
            .OrderBy(static value => value.Project, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ReadTaskStart(
        string repositoryRoot,
        IReadOnlySet<string> expectedProjects,
        TaskStartedEventArgs started,
        IDictionary<TaskContextKey, InvocationBuilder> builders,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (!TryContextKey(started.BuildEventContext, out var key))
        {
            Add(
                diagnostics,
                "compile-input.binary-log-context-missing",
                "The binary log contains a Csc task without a complete build event context.");
            return;
        }

        if (string.IsNullOrWhiteSpace(started.ProjectFile))
        {
            Add(
                diagnostics,
                "compile-input.binary-log-project-missing",
                $"Csc invocation '{key}' has no project path.");
            return;
        }

        string projectFullPath;
        try
        {
            projectFullPath = ResolvePath(repositoryRoot, started.ProjectFile);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            Add(
                diagnostics,
                "compile-input.binary-log-project-invalid",
                $"Csc invocation '{key}' has invalid project path '{started.ProjectFile}': {exception.Message}");
            return;
        }

        if (!TryRepositoryPath(repositoryRoot, projectFullPath, out var project)
            || project is null
            || !expectedProjects.Contains(project))
        {
            return;
        }

        if (!TryTargetContextKey(started.BuildEventContext, out var targetContext))
        {
            Add(
                diagnostics,
                "compile-input.binary-log-context-missing",
                $"Csc invocation '{key}' has no complete target context.");
            return;
        }

        if (!builders.TryAdd(
                key,
                new InvocationBuilder(
                    key.ToString(),
                    project,
                    projectFullPath,
                    targetContext,
                    started.TaskFile)))
        {
            Add(
                diagnostics,
                "compile-input.binary-log-context-duplicate",
                $"Binary log reuses build event context '{key}' for more than one Csc task.");
        }
    }

    private static bool TryContextKey(BuildEventContext? context, out TaskContextKey key)
    {
        if (context is null
            || context.NodeId < 0
            || context.ProjectContextId < 0
            || context.TargetId < 0
            || context.TaskId < 0)
        {
            key = default;
            return false;
        }

        key = new TaskContextKey(
            context.SubmissionId,
            context.NodeId,
            context.ProjectInstanceId,
            context.ProjectContextId,
            context.TargetId,
            context.TaskId,
            context.EvaluationId,
            context.BuildRequestId);
        return true;
    }

    private static bool TryTargetContextKey(BuildEventContext? context, out TargetContextKey key)
    {
        if (context is null
            || context.NodeId < 0
            || context.ProjectContextId < 0
            || context.TargetId < 0)
        {
            key = default;
            return false;
        }

        key = new TargetContextKey(
            context.SubmissionId,
            context.NodeId,
            context.ProjectInstanceId,
            context.ProjectContextId,
            context.TargetId);
        return true;
    }

    private static bool TryReadCscTaskAssembly(string? message, out string taskAssembly)
    {
        const string prefix = "Using \"Csc\" task from assembly \"";
        const string suffix = "\".";
        if (message is not null
            && message.StartsWith(prefix, StringComparison.Ordinal)
            && message.EndsWith(suffix, StringComparison.Ordinal)
            && message.Length > prefix.Length + suffix.Length)
        {
            taskAssembly = message[prefix.Length..^suffix.Length];
            return true;
        }

        taskAssembly = string.Empty;
        return false;
    }

    private static string? ReadSdkRoot(
        IReadOnlyDictionary<string, string> buildEnvironment,
        string binaryLog,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (!buildEnvironment.TryGetValue("MSBuildExtensionsPath", out var extensionsPath)
            || string.IsNullOrWhiteSpace(extensionsPath)
            || !buildEnvironment.TryGetValue("MSBuildSDKsPath", out var sdksPath)
            || string.IsNullOrWhiteSpace(sdksPath))
        {
            Add(
                diagnostics,
                "compile-input.sdk-environment-missing",
                $"Compile-input binary log '{binaryLog}' does not record MSBuildExtensionsPath and MSBuildSDKsPath.");
            return null;
        }

        try
        {
            var sdkRoot = Path.GetFullPath(extensionsPath);
            var recordedSdks = Path.GetFullPath(sdksPath);
            var expectedSdks = Path.Combine(sdkRoot, "Sdks");
            if (!string.Equals(recordedSdks, expectedSdks, PathComparison))
            {
                Add(
                    diagnostics,
                    "compile-input.sdk-environment-inconsistent",
                    $"Compile-input binary log '{binaryLog}' records MSBuildSDKsPath '{recordedSdks}', "
                    + $"which is not the Sdks directory below MSBuildExtensionsPath '{sdkRoot}'.");
                return null;
            }

            return sdkRoot;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            Add(
                diagnostics,
                "compile-input.sdk-environment-invalid",
                $"Compile-input binary log '{binaryLog}' records an invalid SDK path: {exception.Message}");
            return null;
        }
    }

    private static string ValidateRepositoryRoot(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The repository root '{repositoryRoot}' does not exist.");
        }

        return root;
    }

    private static HashSet<string> DiscoverTestProjects(string root)
    {
        var sourceRoot = Path.Combine(root, "src");
        return Directory.Exists(sourceRoot)
            ? Directory
                .EnumerateFiles(sourceRoot, "*.Tests.csproj", SearchOption.AllDirectories)
                .Select(path => RepositoryPath(root, path))
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private static IEnumerable<DeclaredCompileInput> EnumerateBindings(
        CompatibilityLedger ledger,
        IReadOnlyList<RequiredTestSentinel> requiredTests)
    {
        foreach (var compatibilityCase in ledger.Cases)
        {
            yield return new DeclaredCompileInput(
                compatibilityCase.Binding.Project,
                compatibilityCase.Binding.SourceFile,
                $"case '{compatibilityCase.CaseId}'");
            foreach (var support in compatibilityCase.Binding.SupportDocuments)
            {
                yield return new DeclaredCompileInput(
                    compatibilityCase.Binding.Project,
                    support.SourceFile,
                    $"case '{compatibilityCase.CaseId}'");
            }
        }

        foreach (var sentinel in requiredTests)
        {
            yield return new DeclaredCompileInput(
                sentinel.Project,
                sentinel.SourceFile,
                $"sentinel '{sentinel.Id}'");
            foreach (var support in sentinel.SupportDocuments)
            {
                yield return new DeclaredCompileInput(
                    sentinel.Project,
                    support.SourceFile,
                    $"sentinel '{sentinel.Id}'");
            }
        }
    }

    private static void BindExecutedAssemblies(
        string repositoryRoot,
        IReadOnlyDictionary<string, CscCompileInvocation[]> invocationsByProject,
        IEnumerable<string> expectedProjects,
        NUnitTestRun run,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        foreach (var project in expectedProjects)
        {
            if (!invocationsByProject.TryGetValue(project, out var projectInvocations)
                || projectInvocations.Length != 1)
            {
                continue;
            }

            var invocation = projectInvocations[0];
            if (invocation.OutputAssemblies.Count != 1)
            {
                Add(
                    diagnostics,
                    "compile-input.output-assembly-ambiguous",
                    $"Csc invocation '{invocation.InvocationId}' for '{project}' records "
                    + $"{invocation.OutputAssemblies.Count} OutputAssembly inputs; exactly one is required.");
                continue;
            }

            var expectedAssemblyName = Path.GetFileNameWithoutExtension(project) + ".dll";
            var outputAssembly = invocation.OutputAssemblies[0];
            if (!string.Equals(
                    Path.GetFileName(outputAssembly.FullPath),
                    expectedAssemblyName,
                    StringComparison.Ordinal))
            {
                Add(
                    diagnostics,
                    "compile-input.output-assembly-name-mismatch",
                    $"Csc OutputAssembly '{outputAssembly.DisplayPath}' for '{project}' is not named "
                    + $"'{expectedAssemblyName}'.");
                continue;
            }

            var executions = run.Projects
                .Where(value => string.Equals(value.Project, expectedAssemblyName, StringComparison.Ordinal))
                .ToArray();
            if (executions.Length != 1)
            {
                Add(
                    diagnostics,
                    "compile-input.executed-assembly-ambiguous",
                    $"Test project '{project}' has {executions.Length} NUnit assembly results named "
                    + $"'{expectedAssemblyName}'; exactly one is required.");
                continue;
            }

            var execution = executions[0];
            if (string.IsNullOrWhiteSpace(execution.AssemblyPath))
            {
                Add(
                    diagnostics,
                    "compile-input.executed-assembly-path-missing",
                    $"NUnit result '{execution.Source}' for '{project}' has no executed assembly path.");
                continue;
            }

            string executedAssembly;
            try
            {
                executedAssembly = ResolvePath(repositoryRoot, execution.AssemblyPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                Add(
                    diagnostics,
                    "compile-input.executed-assembly-path-invalid",
                    $"NUnit result '{execution.Source}' for '{project}' has invalid assembly path "
                    + $"'{execution.AssemblyPath}': {exception.Message}");
                continue;
            }

            if (!TryRepositoryPath(repositoryRoot, executedAssembly, out var executedRepositoryPath)
                || executedRepositoryPath is null
                || !string.Equals(
                    Path.GetFileName(executedAssembly),
                    expectedAssemblyName,
                    StringComparison.Ordinal))
            {
                Add(
                    diagnostics,
                    "compile-input.executed-assembly-path-invalid",
                    $"NUnit result '{execution.Source}' for '{project}' executed non-repository or wrongly named "
                    + $"assembly '{executedAssembly}'.");
                continue;
            }

            if (!TryFileDigest(outputAssembly.FullPath, out var outputDigest, out var outputError))
            {
                Add(
                    diagnostics,
                    "compile-input.output-assembly-unreadable",
                    $"Csc OutputAssembly '{outputAssembly.DisplayPath}' for '{project}' cannot be hashed: {outputError}");
                continue;
            }

            if (!TryFileDigest(executedAssembly, out var executedDigest, out var executedError))
            {
                Add(
                    diagnostics,
                    "compile-input.executed-assembly-unreadable",
                    $"Executed NUnit assembly '{executedRepositoryPath}' for '{project}' cannot be hashed: {executedError}");
                continue;
            }

            if (!string.Equals(outputDigest, executedDigest, StringComparison.Ordinal))
            {
                Add(
                    diagnostics,
                    "compile-input.executed-assembly-mismatch",
                    $"Executed NUnit assembly '{executedRepositoryPath}' is not byte-for-byte the Csc OutputAssembly "
                    + $"'{outputAssembly.DisplayPath}' for '{project}'.");
            }
        }
    }

    private static bool TryFileDigest(string path, out string? digest, out string? error)
    {
        try
        {
            using var stream = File.OpenRead(path);
            digest = Convert.ToHexString(SHA256.HashData(stream));
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            digest = null;
            error = exception.Message;
            return false;
        }
    }

    private static void ScanRepositoryCompileSources(
        string repositoryRoot,
        IEnumerable<CscCompileInvocation> invocations,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var scanned = new HashSet<BindingKey>();
        foreach (var invocation in invocations)
        {
            foreach (var additionalFile in invocation.AdditionalFiles.Where(
                         static value => value.RepositoryPath is null))
            {
                Add(
                    diagnostics,
                    "compile-input.additional-file-external",
                    $"Csc invocation '{invocation.InvocationId}' for '{invocation.Project}' receives non-repository "
                    + $"AdditionalFiles input '{additionalFile.FullPath}'.");
            }

            foreach (var item in invocation.Sources
                         .Concat(invocation.AdditionalFiles)
                         .Where(static value => value.RepositoryPath is not null))
            {
                var repositoryPath = item.RepositoryPath!;
                var key = new BindingKey(invocation.Project, repositoryPath);
                if (!scanned.Add(key))
                {
                    continue;
                }

                if (!File.Exists(item.FullPath))
                {
                    Add(
                        diagnostics,
                        "compile-input.source-missing",
                        $"Actual Csc source/additional input '{repositoryPath}' for '{invocation.Project}' no longer exists.");
                    continue;
                }

                var lineNumber = 0;
                var scanner = new CSharpDirectiveScanner();
                foreach (var line in File.ReadLines(item.FullPath))
                {
                    lineNumber++;
                    var directive = scanner.ReadDirective(line);
                    scanner.Advance(line);
                    if (directive is null)
                    {
                        continue;
                    }

                    if (directive.Value.Kind is DirectiveKind.Line or DirectiveKind.Checksum)
                    {
                        var code = directive.Value.Kind == DirectiveKind.Line
                            ? "compile-input.line-remap"
                            : "compile-input.checksum-remap";
                        var name = directive.Value.Kind == DirectiveKind.Line ? "line" : "pragma checksum";
                        Add(
                            diagnostics,
                            code,
                            $"Actual Csc source/additional input '{repositoryPath}' for '{invocation.Project}' contains "
                            + $"forbidden '#{name}' source remapping at line {lineNumber}.");
                    }
                    else if (directive.Value.Kind == DirectiveKind.WarningDisable
                        && (string.IsNullOrWhiteSpace(directive.Value.Arguments)
                            || StartsWithComment(directive.Value.Arguments)
                            || ContainsWarningCode([directive.Value.Arguments])))
                    {
                        Add(
                            diagnostics,
                            "compile-input.cs0436-pragma",
                            $"Actual Csc source/additional input '{repositoryPath}' for '{invocation.Project}' can suppress CS0436 "
                            + $"with '#pragma warning disable' at line {lineNumber}.");
                    }
                }
            }
        }
    }

    private static void ScanAnalyzerConfigs(
        IEnumerable<CscCompileInvocation> invocations,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var scanned = new HashSet<BindingKey>();
        foreach (var invocation in invocations)
        {
            foreach (var config in invocation.AnalyzerConfigFiles)
            {
                var key = new BindingKey(invocation.Project, config.FullPath);
                if (!scanned.Add(key))
                {
                    continue;
                }

                if (!File.Exists(config.FullPath))
                {
                    Add(
                        diagnostics,
                        "compile-input.analyzer-config-missing",
                        $"Csc analyzer config '{config.DisplayPath}' for '{invocation.Project}' no longer exists.");
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in File.ReadLines(config.FullPath))
                {
                    lineNumber++;
                    var value = line.AsSpan().Trim().TrimStart('\uFEFF');
                    if (value.IsEmpty || value[0] is '#' or ';' or '[')
                    {
                        continue;
                    }

                    var separator = value.IndexOf('=');
                    if (separator < 0)
                    {
                        continue;
                    }

                    var property = value[..separator].Trim();
                    var setting = value[(separator + 1)..].Trim();
                    var suppressesWarning = property.Equals(
                            "build_property.NoWarn",
                            StringComparison.OrdinalIgnoreCase)
                        && ContainsWarningCode([setting.ToString()]);
                    suppressesWarning |= property.Equals(
                            "build_property.WarningsNotAsErrors",
                            StringComparison.OrdinalIgnoreCase)
                        && ContainsWarningCode([setting.ToString()]);
                    suppressesWarning |= IsCs0436SeverityProperty(property)
                        && IsSuppressiveSeverity(setting);

                    if (suppressesWarning)
                    {
                        Add(
                            diagnostics,
                            "compile-input.cs0436-analyzer-config",
                            $"Csc analyzer config '{config.DisplayPath}' for '{invocation.Project}' suppresses or demotes "
                            + $"CS0436 at line {lineNumber}.");
                    }
                }
            }
        }
    }

    private static bool IsCs0436SeverityProperty(ReadOnlySpan<char> property)
        => property.Equals("dotnet_diagnostic.CS0436.severity", StringComparison.OrdinalIgnoreCase)
            || property.Equals("dotnet_analyzer_diagnostic.severity", StringComparison.OrdinalIgnoreCase)
            || property.Equals(
                "dotnet_analyzer_diagnostic.category-Compiler.severity",
                StringComparison.OrdinalIgnoreCase);

    private static bool IsSuppressiveSeverity(ReadOnlySpan<char> value)
        => !value.Equals("default", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("warning", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("error", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsWarningCode(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            foreach (var token in value.Split(
                [';', ',', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = token.AsSpan();
                if (candidate.StartsWith("CS", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate[2..];
                }

                if (int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number == 436)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool StartsWithComment(string value)
    {
        var trimmed = value.AsSpan().TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal);
    }

    private static CSharpDirective? ReadDirectiveAtLineStart(string line)
    {
        var value = line.AsSpan().TrimStart();
        if (value.IsEmpty || value[0] != '#')
        {
            return null;
        }

        value = value[1..].TrimStart();
        if (TakeToken(ref value, "line"))
        {
            return new CSharpDirective(DirectiveKind.Line, value.ToString());
        }

        if (!TakeToken(ref value, "pragma"))
        {
            return null;
        }

        value = value.TrimStart();
        if (TakeToken(ref value, "checksum"))
        {
            return new CSharpDirective(DirectiveKind.Checksum, value.ToString());
        }

        if (!TakeToken(ref value, "warning"))
        {
            return null;
        }

        value = value.TrimStart();
        return TakeToken(ref value, "disable")
            ? new CSharpDirective(DirectiveKind.WarningDisable, value.ToString())
            : null;
    }

    private static bool TakeToken(ref ReadOnlySpan<char> value, string token)
    {
        if (!value.StartsWith(token, StringComparison.Ordinal)
            || value.Length > token.Length && !char.IsWhiteSpace(value[token.Length]))
        {
            return false;
        }

        value = value[token.Length..];
        return true;
    }

    private static string ResolvePath(string baseDirectory, string path)
        => Path.GetFullPath(path, baseDirectory);

    private static bool TryRepositoryPath(string root, string fullPath, out string? repositoryPath)
    {
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, PathComparison))
        {
            repositoryPath = null;
            return false;
        }

        repositoryPath = RepositoryPath(root, fullPath);
        return true;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string RepositoryPath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static void Add(ICollection<CompatibilityDiagnostic> diagnostics, string code, string message)
        => diagnostics.Add(new CompatibilityDiagnostic(code, message));

    private sealed record DeclaredCompileInput(string Project, string SourceFile, string Owner);

    private sealed record CompileInputBinding(
        string Project,
        string SourceFile,
        IReadOnlyList<string> Owners);

    private readonly record struct BindingKey(string Project, string SourceFile);

    private readonly record struct TaskContextKey(
        int SubmissionId,
        int NodeId,
        int ProjectInstanceId,
        int ProjectContextId,
        int TargetId,
        int TaskId,
        int EvaluationId,
        long BuildRequestId)
    {
        public override string ToString()
            => $"{SubmissionId}/{NodeId}/{ProjectInstanceId}/{ProjectContextId}/{TargetId}/{TaskId}/{EvaluationId}/{BuildRequestId}";
    }

    private readonly record struct TargetContextKey(
        int SubmissionId,
        int NodeId,
        int ProjectInstanceId,
        int ProjectContextId,
        int TargetId);

    private sealed class InvocationBuilder(
        string invocationId,
        string project,
        string projectFullPath,
        TargetContextKey targetContext,
        string? taskDefinitionFile)
    {
        private readonly List<RawTaskItem> additionalFiles = [];
        private readonly List<RawTaskItem> analyzerConfigFiles = [];
        private readonly List<RawTaskItem> analyzers = [];
        private readonly List<RawTaskItem> deterministic = [];
        private readonly List<RawTaskItem> disabledWarnings = [];
        private readonly List<RawTaskItem> embedAllSources = [];
        private readonly List<RawTaskItem> embeddedFiles = [];
        private readonly List<RawTaskItem> noConfig = [];
        private readonly List<RawTaskItem> outputAssemblies = [];
        private readonly List<RawTaskItem> references = [];
        private readonly List<RawTaskItem> sourceLink = [];
        private readonly List<RawTaskItem> sources = [];
        private readonly List<RawTaskItem> treatWarningsAsErrors = [];
        private readonly List<RawTaskItem> warningsAsErrors = [];
        private readonly List<RawTaskItem> warningsNotAsErrors = [];
        private readonly List<NamedRawTaskItem> unsupportedFileInputs = [];

        internal string InvocationId { get; } = invocationId;

        internal string Project { get; } = project;

        internal string ProjectFullPath { get; } = projectFullPath;

        internal TargetContextKey TargetContext { get; } = targetContext;

        internal bool Finished { get; set; }

        internal bool Succeeded { get; set; }

        internal void AddParameter(TaskParameterEventArgs2 parameter)
        {
            if (parameter.Items is null)
            {
                return;
            }

            if (IsUnsupportedFileInput(parameter.ParameterName))
            {
                foreach (var value in parameter.Items)
                {
                    unsupportedFileInputs.Add(new NamedRawTaskItem(
                        parameter.ParameterName,
                        ToRawTaskItem(value)));
                }

                return;
            }

            var destination = parameter.ParameterName switch
            {
                "AdditionalFiles" => additionalFiles,
                "AnalyzerConfigFiles" => analyzerConfigFiles,
                "Analyzers" => analyzers,
                "Deterministic" => deterministic,
                "DisabledWarnings" => disabledWarnings,
                "EmbedAllSources" => embedAllSources,
                "EmbeddedFiles" => embeddedFiles,
                "NoConfig" => noConfig,
                "OutputAssembly" => outputAssemblies,
                "References" => references,
                "SourceLink" => sourceLink,
                "Sources" => sources,
                "TreatWarningsAsErrors" => treatWarningsAsErrors,
                "WarningsAsErrors" => warningsAsErrors,
                "WarningsNotAsErrors" => warningsNotAsErrors,
                _ => null
            };
            if (destination is null)
            {
                return;
            }

            foreach (var value in parameter.Items)
            {
                destination.Add(ToRawTaskItem(value));
            }
        }

        internal CscCompileInvocation? Build(
            string repositoryRoot,
            string? sdkRoot,
            IReadOnlyList<string> taskAssemblyLocations,
            ICollection<CompatibilityDiagnostic> diagnostics)
        {
            var projectDirectory = Path.GetDirectoryName(ProjectFullPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                Add(
                    diagnostics,
                    "compile-input.binary-log-project-invalid",
                    $"Csc invocation '{InvocationId}' has project path '{ProjectFullPath}' without a directory.");
                return null;
            }

            return new CscCompileInvocation(
                InvocationId,
                Project,
                ProjectFullPath,
                Finished && Succeeded,
                sdkRoot,
                ConvertPaths(
                    repositoryRoot,
                    projectDirectory,
                    string.IsNullOrWhiteSpace(taskDefinitionFile)
                        ? []
                        : [new RawTaskItem(taskDefinitionFile, string.Empty, string.Empty)],
                    "TaskDefinitionFile",
                    diagnostics),
                ConvertPaths(
                    repositoryRoot,
                    projectDirectory,
                    taskAssemblyLocations.Select(static value => new RawTaskItem(value, string.Empty, string.Empty)),
                    "TaskAssemblyLocation",
                    diagnostics),
                ConvertPaths(repositoryRoot, projectDirectory, sources, "Sources", diagnostics),
                ConvertPaths(repositoryRoot, projectDirectory, additionalFiles, "AdditionalFiles", diagnostics),
                ConvertPaths(repositoryRoot, projectDirectory, embeddedFiles, "EmbeddedFiles", diagnostics),
                ConvertPaths(repositoryRoot, projectDirectory, outputAssemblies, "OutputAssembly", diagnostics),
                ConvertPaths(repositoryRoot, projectDirectory, sourceLink, "SourceLink", diagnostics),
                disabledWarnings.Select(static value => value.ItemSpec).ToArray(),
                warningsAsErrors.Select(static value => value.ItemSpec).ToArray(),
                warningsNotAsErrors.Select(static value => value.ItemSpec).ToArray(),
                ConvertPaths(repositoryRoot, projectDirectory, analyzerConfigFiles, "AnalyzerConfigFiles", diagnostics),
                ConvertReferences(repositoryRoot, projectDirectory, references, diagnostics),
                ConvertPaths(repositoryRoot, projectDirectory, analyzers, "Analyzers", diagnostics),
                ConvertUnsupportedInputs(repositoryRoot, projectDirectory, unsupportedFileInputs, diagnostics),
                ReadBoolean(embedAllSources),
                ReadBoolean(deterministic),
                ReadBoolean(treatWarningsAsErrors),
                ReadBoolean(noConfig));
        }

        private IReadOnlyList<CscPathInput> ConvertPaths(
            string repositoryRoot,
            string projectDirectory,
            IEnumerable<RawTaskItem> values,
            string parameter,
            ICollection<CompatibilityDiagnostic> diagnostics)
        {
            var result = new List<CscPathInput>();
            foreach (var value in values)
            {
                if (TryResolveItemPath(projectDirectory, value, out var fullPath, out var error))
                {
                    _ = TryRepositoryPath(repositoryRoot, fullPath!, out var repositoryPath);
                    result.Add(new CscPathInput(value.ItemSpec, fullPath!, repositoryPath));
                }
                else
                {
                    Add(
                        diagnostics,
                        "compile-input.binary-log-item-invalid",
                        $"Csc invocation '{InvocationId}' for '{Project}' has invalid {parameter} item "
                        + $"'{value.ItemSpec}': {error}");
                }
            }

            return result;
        }

        private IReadOnlyList<CscReferenceInput> ConvertReferences(
            string repositoryRoot,
            string projectDirectory,
            IEnumerable<RawTaskItem> values,
            ICollection<CompatibilityDiagnostic> diagnostics)
        {
            var result = new List<CscReferenceInput>();
            foreach (var value in values)
            {
                if (TryResolveItemPath(projectDirectory, value, out var fullPath, out var error))
                {
                    _ = TryRepositoryPath(repositoryRoot, fullPath!, out var repositoryPath);
                    result.Add(new CscReferenceInput(
                        value.ItemSpec,
                        fullPath!,
                        repositoryPath,
                        value.Aliases.Split(
                            [',', ';'],
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                }
                else
                {
                    Add(
                        diagnostics,
                        "compile-input.binary-log-item-invalid",
                        $"Csc invocation '{InvocationId}' for '{Project}' has invalid References item "
                        + $"'{value.ItemSpec}': {error}");
                }
            }

            return result;
        }

        private IReadOnlyList<CscUnsupportedInput> ConvertUnsupportedInputs(
            string repositoryRoot,
            string projectDirectory,
            IEnumerable<NamedRawTaskItem> values,
            ICollection<CompatibilityDiagnostic> diagnostics)
        {
            var result = new List<CscUnsupportedInput>();
            foreach (var value in values)
            {
                var converted = ConvertPaths(
                    repositoryRoot,
                    projectDirectory,
                    [value.Item],
                    value.Parameter,
                    diagnostics);
                if (converted.Count == 1)
                {
                    result.Add(new CscUnsupportedInput(value.Parameter, converted[0]));
                }
            }

            return result;
        }

        private static bool IsUnsupportedFileInput(string? parameter)
            => parameter is "AddModules"
                or "Resources"
                or "LinkResources"
                or "ResponseFiles"
                or "Win32Resource"
                or "Win32Icon"
                or "Win32Manifest"
                or "CodeAnalysisRuleSet"
                or "RuleSet";

        private static RawTaskItem ToRawTaskItem(object? value)
            => value is ITaskItem item
                ? new RawTaskItem(
                    item.ItemSpec ?? string.Empty,
                    item.GetMetadata("FullPath") ?? string.Empty,
                    item.GetMetadata("Aliases") ?? string.Empty)
                : new RawTaskItem(value?.ToString() ?? string.Empty, string.Empty, string.Empty);

        private static bool TryResolveItemPath(
            string projectDirectory,
            RawTaskItem value,
            out string? fullPath,
            out string? error)
        {
            var candidate = string.IsNullOrWhiteSpace(value.FullPath) ? value.ItemSpec : value.FullPath;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                fullPath = null;
                error = "the item path is empty";
                return false;
            }

            try
            {
                fullPath = ResolvePath(projectDirectory, candidate);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                fullPath = null;
                error = exception.Message;
                return false;
            }
        }

        private static bool? ReadBoolean(IReadOnlyList<RawTaskItem> values)
            => values.Count == 1 && bool.TryParse(values[0].ItemSpec, out var value) ? value : null;
    }

    private sealed record RawTaskItem(string ItemSpec, string FullPath, string Aliases);

    private sealed record NamedRawTaskItem(string Parameter, RawTaskItem Item);

    private sealed class CSharpDirectiveScanner
    {
        private LexicalMode mode;
        private int rawDelimiterLength;

        internal CSharpDirective? ReadDirective(string line)
            => mode == LexicalMode.Normal ? ReadDirectiveAtLineStart(line) : null;

        internal void Advance(string line)
        {
            var index = 0;
            while (index < line.Length)
            {
                switch (mode)
                {
                    case LexicalMode.BlockComment:
                        var commentEnd = line.IndexOf("*/", index, StringComparison.Ordinal);
                        if (commentEnd < 0)
                        {
                            return;
                        }

                        mode = LexicalMode.Normal;
                        index = commentEnd + 2;
                        break;
                    case LexicalMode.VerbatimString:
                        var quote = line.IndexOf('"', index);
                        if (quote < 0)
                        {
                            return;
                        }

                        if (quote + 1 < line.Length && line[quote + 1] == '"')
                        {
                            index = quote + 2;
                            break;
                        }

                        mode = LexicalMode.Normal;
                        index = quote + 1;
                        break;
                    case LexicalMode.RawString:
                        var run = CountQuotes(line, index);
                        if (run >= rawDelimiterLength)
                        {
                            mode = LexicalMode.Normal;
                            index += run;
                        }
                        else
                        {
                            index += Math.Max(run, 1);
                        }

                        break;
                    default:
                        if (StartsWith(line, index, "//"))
                        {
                            return;
                        }

                        if (StartsWith(line, index, "/*"))
                        {
                            mode = LexicalMode.BlockComment;
                            index += 2;
                            break;
                        }

                        if (StartsWith(line, index, "@$\"") || StartsWith(line, index, "$@\""))
                        {
                            mode = LexicalMode.VerbatimString;
                            index += 3;
                            break;
                        }

                        if (StartsWith(line, index, "@\""))
                        {
                            mode = LexicalMode.VerbatimString;
                            index += 2;
                            break;
                        }

                        if (line[index] == '"')
                        {
                            var quoteCount = CountQuotes(line, index);
                            if (quoteCount >= 3)
                            {
                                mode = LexicalMode.RawString;
                                rawDelimiterLength = quoteCount;
                                index += quoteCount;
                            }
                            else
                            {
                                index = SkipQuotedLiteral(line, index, '"');
                            }

                            break;
                        }

                        if (line[index] == '\'')
                        {
                            index = SkipQuotedLiteral(line, index, '\'');
                            break;
                        }

                        index++;
                        break;
                }
            }
        }

        private static bool StartsWith(string value, int index, string expected)
            => value.AsSpan(index).StartsWith(expected, StringComparison.Ordinal);

        private static int CountQuotes(string value, int index)
        {
            var current = index;
            while (current < value.Length && value[current] == '"')
            {
                current++;
            }

            return current - index;
        }

        private static int SkipQuotedLiteral(string value, int index, char delimiter)
        {
            index++;
            var escaped = false;
            while (index < value.Length)
            {
                var character = value[index++];
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == delimiter)
                {
                    break;
                }
            }

            return index;
        }

        private enum LexicalMode
        {
            Normal,
            BlockComment,
            VerbatimString,
            RawString
        }
    }

    private readonly record struct CSharpDirective(DirectiveKind Kind, string Arguments);

    private enum DirectiveKind
    {
        Line,
        Checksum,
        WarningDisable
    }
}

/// <summary>A path supplied to the C# compiler, with both the recorded and resolved forms.</summary>
public sealed record CscPathInput(string ItemSpec, string FullPath, string? RepositoryPath)
{
    /// <summary>Gets the repository-relative form when owned by the repository, otherwise the resolved path.</summary>
    public string DisplayPath => RepositoryPath ?? FullPath;
}

/// <summary>A compiler metadata reference, including its resolved path and any explicit aliases.</summary>
public sealed record CscReferenceInput(
    string ItemSpec,
    string FullPath,
    string? RepositoryPath,
    IReadOnlyList<string> Aliases);

/// <summary>A file-bearing C# compiler input which this ratchet does not permit.</summary>
public sealed record CscUnsupportedInput(string Parameter, CscPathInput Input);

/// <summary>The security-relevant inputs recorded for one actual C# compiler invocation.</summary>
public sealed record CscCompileInvocation(
    string InvocationId,
    string Project,
    string ProjectFullPath,
    bool Succeeded,
    string? SdkRoot,
    IReadOnlyList<CscPathInput> TaskDefinitionFiles,
    IReadOnlyList<CscPathInput> TaskAssemblyLocations,
    IReadOnlyList<CscPathInput> Sources,
    IReadOnlyList<CscPathInput> AdditionalFiles,
    IReadOnlyList<CscPathInput> EmbeddedFiles,
    IReadOnlyList<CscPathInput> OutputAssemblies,
    IReadOnlyList<CscPathInput> SourceLinkFiles,
    IReadOnlyList<string> DisabledWarnings,
    IReadOnlyList<string> WarningsAsErrors,
    IReadOnlyList<string> WarningsNotAsErrors,
    IReadOnlyList<CscPathInput> AnalyzerConfigFiles,
    IReadOnlyList<CscReferenceInput> References,
    IReadOnlyList<CscPathInput> Analyzers,
    IReadOnlyList<CscUnsupportedInput> UnsupportedFileInputs,
    bool? EmbedAllSources,
    bool? Deterministic,
    bool? TreatWarningsAsErrors,
    bool? NoConfig);

/// <summary>The fail-closed result of validating the actual test compiler invocations.</summary>
public sealed record CompileInputVerificationReport(
    IReadOnlyList<CompatibilityDiagnostic> Diagnostics,
    int BinaryLogCount,
    int ExpectedProjectCount,
    int CompileItemCount,
    int BoundSourceCount,
    IReadOnlyList<CscCompileInvocation> Invocations)
{
    /// <summary>Gets whether every compile-input invariant was proved.</summary>
    public bool IsValid => Diagnostics.Count == 0;
}
