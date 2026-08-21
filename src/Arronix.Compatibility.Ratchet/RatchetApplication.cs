namespace Arronix.Compatibility.Ratchet;

/// <summary>The command-line boundary used by repository CI.</summary>
public static class RatchetApplication
{
    public const string Usage =
        "Usage: Arronix.Compatibility.Ratchet validate --ledger <directory> "
        + "--results <file-or-directory> [--results <file-or-directory> ...] "
        + "--required-tests <registry.tsv> "
        + "--compile-inputs <directory> "
        + "[--root <repository-root>] [--previous-ledger <directory>]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            output.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var options, out var usageError))
        {
            error.WriteLine(usageError);
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            var ledger = CompatibilityDocumentReader.ReadLedger(options.LedgerDirectory);
            var previous = options.PreviousLedgerDirectory is null
                ? null
                : CompatibilityDocumentReader.ReadLedger(options.PreviousLedgerDirectory);
            var run = NUnitResultReader.ReadPaths(options.ResultPaths);
            var requiredTests = RequiredTestSentinelReader.Read(options.RequiredTestsPath);
            var compileInputs = CompileInputManifestVerifier.Verify(
                options.RepositoryRoot,
                options.CompileInputsDirectory,
                ledger,
                requiredTests,
                run);
            var snapshot = RepositorySnapshot.Capture(options.RepositoryRoot, ledger, run);
            var report = CompatibilityValidator.Validate(ledger, run, snapshot, previous);
            var sentinelVerifications = CompiledTestRunVerifier.VerifySentinels(
                options.RepositoryRoot,
                requiredTests,
                run);
            var counts = report.Counts;

            output.WriteLine(
                $"projects={run.Projects.Count} total={counts.Total} enabled={counts.Enabled} "
                + $"passed={counts.Passed} failed={counts.Failed} skipped={counts.Skipped} "
                + $"inconclusive={counts.Inconclusive} cases={report.RegisteredCases} "
                + $"replacements={report.RegisteredReplacements} "
                + $"passingWitnesses={report.PassingWitnesses} "
                + $"closureEligibleWitnesses={report.ClosureEligibleWitnesses} "
                + $"requiredTests={sentinelVerifications.Count} "
                + $"compileLogs={compileInputs.BinaryLogCount} "
                + $"compileProjects={compileInputs.ExpectedProjectCount} "
                + $"compileItems={compileInputs.CompileItemCount} "
                + $"boundSources={compileInputs.BoundSourceCount}");

            foreach (var diagnostic in report.Diagnostics)
            {
                error.WriteLine($"error {diagnostic.Code}: {diagnostic.Message}");
            }

            foreach (var sentinel in sentinelVerifications.Where(static value => !value.IsValid))
            {
                error.WriteLine($"error {sentinel.Code}: [{sentinel.Id}] {sentinel.Message}");
            }

            foreach (var diagnostic in compileInputs.Diagnostics)
            {
                error.WriteLine($"error {diagnostic.Code}: {diagnostic.Message}");
            }

            return report.IsValid
                && sentinelVerifications.All(static value => value.IsValid)
                && compileInputs.IsValid
                    ? 0
                    : 1;
        }
        catch (Exception exception) when (exception is CompatibilityDocumentException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            error.WriteLine($"input error: {exception.Message}");
            return 2;
        }
    }

    private static bool TryParse(IReadOnlyList<string> args, out CommandOptions options, out string error)
    {
        options = null!;
        error = string.Empty;
        if (args.Count == 0 || !string.Equals(args[0], "validate", StringComparison.Ordinal))
        {
            error = "The first argument must be the 'validate' command.";
            return false;
        }

        string? ledger = null;
        string? root = null;
        string? previous = null;
        string? requiredTests = null;
        string? compileInputs = null;
        var results = new List<string>();
        for (var index = 1; index < args.Count; index++)
        {
            var option = args[index];
            if (index + 1 >= args.Count)
            {
                error = $"Option '{option}' has no value.";
                return false;
            }

            var value = args[++index];
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"Option '{option}' has an empty value.";
                return false;
            }

            switch (option)
            {
                case "--ledger" when ledger is null:
                    ledger = value;
                    break;
                case "--root" when root is null:
                    root = value;
                    break;
                case "--previous-ledger" when previous is null:
                    previous = value;
                    break;
                case "--required-tests" when requiredTests is null:
                    requiredTests = value;
                    break;
                case "--compile-inputs" when compileInputs is null:
                    compileInputs = value;
                    break;
                case "--results":
                    results.Add(value);
                    break;
                case "--ledger" or "--root" or "--previous-ledger" or "--required-tests" or "--compile-inputs":
                    error = $"Option '{option}' can be supplied only once.";
                    return false;
                default:
                    error = $"Unknown option '{option}'.";
                    return false;
            }
        }

        if (ledger is null || results.Count == 0 || requiredTests is null || compileInputs is null)
        {
            error = "The validate command requires --ledger, --required-tests, --compile-inputs, "
                + "and at least one --results path.";
            return false;
        }

        options = new CommandOptions(
            ledger,
            results,
            root ?? Directory.GetCurrentDirectory(),
            previous,
            requiredTests,
            compileInputs);
        return true;
    }

    private sealed record CommandOptions(
        string LedgerDirectory,
        IReadOnlyList<string> ResultPaths,
        string RepositoryRoot,
        string? PreviousLedgerDirectory,
        string RequiredTestsPath,
        string CompileInputsDirectory);
}
