using System.Text.Json;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Validates the permanent compatibility ledger against a fresh NUnit execution.</summary>
public static class CompatibilityValidator
{
    public const int PermanentSkipCeiling = 302;

    public static CompatibilityValidationReport Validate(
        CompatibilityLedger ledger,
        NUnitTestRun run,
        RepositorySnapshot snapshot,
        CompatibilityLedger? previousLedger = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<CompatibilityDiagnostic>();
        var sources = IndexUnique(
            ledger.Sources,
            static value => value.SourceId,
            "source.duplicate-id",
            diagnostics);
        var requirements = IndexUnique(
            ledger.Requirements,
            static value => value.RequirementId,
            "requirement.duplicate-id",
            diagnostics);
        var cases = IndexUnique(
            ledger.Cases,
            static value => value.CaseId,
            "case.duplicate-id",
            diagnostics);
        var replacements = IndexUnique(
            ledger.Replacements,
            static value => value.ReplacementId,
            "replacement.duplicate-id",
            diagnostics);

        ValidateBaseline(ledger, requirements, diagnostics);
        ValidateSources(ledger.Sources, sources, ledger.Cases, snapshot, diagnostics);
        ValidateRequirements(ledger.Requirements, sources, cases, diagnostics);
        ValidateCases(ledger.Cases, sources, requirements, ledger.Baseline, diagnostics);

        var executions = IndexExecutions(run, diagnostics);
        var bindings = IndexBindings(ledger.Cases, diagnostics);
        var executionState = ValidateExecutions(
            run,
            ledger.Baseline.CurrentSkipCount,
            sources,
            cases,
            bindings,
            executions,
            snapshot,
            previousLedger,
            diagnostics);
        var closedByReplacement = ValidateReplacements(
            ledger.Replacements,
            replacements,
            cases,
            requirements,
            sources,
            executionState,
            snapshot,
            previousLedger,
            diagnostics);

        ValidateDisappearances(ledger.Cases, executionState.ExecutionsByCase, closedByReplacement, diagnostics);
        ValidateSnapshot(ledger.Cases, snapshot, closedByReplacement, diagnostics);

        if (previousLedger is not null)
        {
            ValidateHistory(previousLedger, ledger, diagnostics);
        }

        return new CompatibilityValidationReport(
            diagnostics
                .OrderBy(static value => value.Code, StringComparer.Ordinal)
                .ThenBy(static value => value.Message, StringComparer.Ordinal)
                .ToArray(),
            run.Counts,
            ledger.Cases.Count,
            ledger.Replacements.Count,
            executionState.PassingWitnesses,
            executionState.ClosureEligibleWitnesses);
    }

    private static void ValidateBaseline(
        CompatibilityLedger ledger,
        IReadOnlyDictionary<string, CompatibilityRequirement> requirements,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var baseline = ledger.Baseline;
        if (baseline.SchemaVersion != 1)
        {
            Add(diagnostics, "baseline.schema-version", $"Unsupported baseline schema version {baseline.SchemaVersion}.");
        }

        if (!string.Equals(baseline.Schema, "schema/baseline.schema.json", StringComparison.Ordinal))
        {
            Add(diagnostics, "baseline.schema", $"Unexpected baseline schema reference '{baseline.Schema}'.");
        }

        ValidateId(baseline.BaselineId, "baseline.invalid-id", diagnostics);
        if (!IsCommit(baseline.RepositoryBaselineCommit) || !IsCommit(baseline.CaptureHeadCommit))
        {
            Add(diagnostics, "baseline.invalid-commit", "The baseline contains a non-SHA-1 repository commit.");
        }

        if (baseline.Totals.Skipped != PermanentSkipCeiling)
        {
            Add(
                diagnostics,
                "baseline.skip-ceiling-changed",
                $"The permanent skip ceiling is {PermanentSkipCeiling}, not {baseline.Totals.Skipped}.");
        }

        if (baseline.CurrentSkipCount is < 0 or > PermanentSkipCeiling)
        {
            Add(
                diagnostics,
                "baseline.invalid-current-skip-count",
                $"The current skip count {baseline.CurrentSkipCount} is outside the permanent range 0..{PermanentSkipCeiling}.");
        }

        if (baseline.Runs.Count != 2
            || baseline.Totals != new BaselineTotals
            {
                CapturedCases = 841,
                Passed = 539,
                Failed = 0,
                Inconclusive = 0,
                Skipped = PermanentSkipCeiling
            })
        {
            Add(diagnostics, "baseline.capture-changed", "The immutable R00 capture totals changed.");
        }

        if (baseline.InitialRecordCounts != new InitialRecordCounts
            {
                Sources = 12,
                Requirements = 129,
                Cases = 302,
                Replacements = 0
            })
        {
            Add(diagnostics, "baseline.initial-counts-changed", "The immutable R00 record counts changed.");
        }

        if (ledger.Sources.Count < baseline.InitialRecordCounts.Sources
            || ledger.Requirements.Count < baseline.InitialRecordCounts.Requirements
            || ledger.Cases.Count < baseline.InitialRecordCounts.Cases
            || ledger.Replacements.Count < baseline.InitialRecordCounts.Replacements)
        {
            Add(diagnostics, "baseline.record-removed", "A canonical record collection fell below its R00 count.");
        }

        var runIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var baselineRun in baseline.Runs)
        {
            ValidateId(baselineRun.RunId, "baseline.invalid-run-id", diagnostics);
            if (!runIds.Add(baselineRun.RunId))
            {
                Add(diagnostics, "baseline.duplicate-run", $"Baseline run '{baselineRun.RunId}' is duplicated.");
            }

            if (!string.Equals(baselineRun.Format, "nunit3", StringComparison.Ordinal)
                || !IsRelativePath(baselineRun.Project)
                || !IsDigest(baselineRun.ArtifactDigest))
            {
                Add(diagnostics, "baseline.invalid-run", $"Baseline run '{baselineRun.RunId}' is malformed.");
            }

            if (!CountsAddUp(
                    baselineRun.Total,
                    baselineRun.Passed,
                    baselineRun.Failed,
                    baselineRun.Skipped,
                    baselineRun.Inconclusive))
            {
                Add(diagnostics, "baseline.run-counts", $"Baseline run '{baselineRun.RunId}' counts do not add up.");
            }
        }

        if (baseline.Totals.CapturedCases != baseline.Runs.Sum(static value => value.Total)
            || baseline.Totals.Passed != baseline.Runs.Sum(static value => value.Passed)
            || baseline.Totals.Failed != baseline.Runs.Sum(static value => value.Failed)
            || baseline.Totals.Skipped != baseline.Runs.Sum(static value => value.Skipped)
            || baseline.Totals.Inconclusive != baseline.Runs.Sum(static value => value.Inconclusive)
            || !CountsAddUp(
                baseline.Totals.CapturedCases,
                baseline.Totals.Passed,
                baseline.Totals.Failed,
                baseline.Totals.Skipped,
                baseline.Totals.Inconclusive))
        {
            Add(diagnostics, "baseline.total-counts", "The baseline totals do not equal the captured-run totals.");
        }

        var baselineCases = ledger.Cases.Where(static value => value.Baseline is not null).ToArray();
        if (baselineCases.Length != baseline.InitialRecordCounts.Cases)
        {
            Add(
                diagnostics,
                "baseline.case-count",
                $"The ledger contains {baselineCases.Length} baseline cases; expected {baseline.InitialRecordCounts.Cases}.");
        }

        var reasons = baselineCases
            .GroupBy(static value => value.Baseline!.ReasonCode, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        if (!DictionaryEquals(baseline.ReasonCounts, reasons))
        {
            Add(diagnostics, "baseline.reason-counts", "Baseline case reason counts do not match the manifest.");
        }

        var fixtures = baselineCases
            .GroupBy(static value => value.Binding.Fixture, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var declaredFixtures = baseline.FixtureCounts.ToDictionary(
            static value => value.Fixture,
            static value => value.Skipped,
            StringComparer.Ordinal);
        if (declaredFixtures.Count != baseline.FixtureCounts.Count || !DictionaryEquals(declaredFixtures, fixtures))
        {
            Add(diagnostics, "baseline.fixture-counts", "Baseline case fixture counts do not match the manifest.");
        }

        foreach (var baselineRun in baseline.Runs)
        {
            var count = baselineCases.Count(value => string.Equals(
                value.Baseline!.RunId,
                baselineRun.RunId,
                StringComparison.Ordinal));
            if (count != baselineRun.Skipped)
            {
                Add(
                    diagnostics,
                    "baseline.run-skip-count",
                    $"Run '{baselineRun.RunId}' owns {count} ledger cases, not {baselineRun.Skipped}.");
            }
        }

        var zeroIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in baseline.ZeroCaseRequirementIds)
        {
            if (!zeroIds.Add(id))
            {
                Add(diagnostics, "baseline.duplicate-zero-requirement", $"Zero-case requirement '{id}' is duplicated.");
            }

            if (!requirements.ContainsKey(id))
            {
                Add(diagnostics, "baseline.unknown-zero-requirement", $"Zero-case requirement '{id}' does not exist.");
            }
        }
    }

    private static void ValidateSources(
        IEnumerable<CompatibilitySource> values,
        IReadOnlyDictionary<string, CompatibilitySource> sources,
        IReadOnlyList<CompatibilityCase> cases,
        RepositorySnapshot snapshot,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        foreach (var source in values)
        {
            if (source.SchemaVersion != 1)
            {
                Add(diagnostics, "source.schema-version", $"Source '{source.SourceId}' has schema {source.SchemaVersion}.");
            }

            ValidateId(source.SourceId, "source.invalid-id", diagnostics);
            RequireText(source.Title, "source.empty-title", source.SourceId, diagnostics);
            if (source.Restrictions.Count == 0
                || source.Restrictions.Any(string.IsNullOrWhiteSpace)
                || source.Restrictions.Distinct(StringComparer.Ordinal).Count() != source.Restrictions.Count)
            {
                Add(diagnostics, "source.invalid-restrictions", $"Source '{source.SourceId}' has no complete restriction record.");
            }

            if (source.Locator is not null && !IsRelativePath(source.Locator))
            {
                Add(diagnostics, "source.invalid-locator", $"Source '{source.SourceId}' has a non-relative locator.");
            }

            if (source.DerivedFromSourceIds is not null)
            {
                if (source.DerivedFromSourceIds.Distinct(StringComparer.Ordinal).Count()
                    != source.DerivedFromSourceIds.Count)
                {
                    Add(diagnostics, "source.duplicate-parent", $"Source '{source.SourceId}' repeats a parent source.");
                }

                foreach (var parent in source.DerivedFromSourceIds)
                {
                    if (!sources.ContainsKey(parent) || string.Equals(parent, source.SourceId, StringComparison.Ordinal))
                    {
                        Add(diagnostics, "source.invalid-parent", $"Source '{source.SourceId}' has invalid parent '{parent}'.");
                    }
                }
            }

            ValidateSourceIdentity(source, diagnostics);

            if (source.ProofUse != ProofUse.Ineligible)
            {
                var boundCases = cases.Count(value => string.Equals(
                    value.SourceId,
                    source.SourceId,
                    StringComparison.Ordinal));
                if (source.CaseCount is null || source.CaseCount.Value != boundCases)
                {
                    Add(
                        diagnostics,
                        "source.case-count",
                        $"Evidence-bearing source '{source.SourceId}' declares {source.CaseCount?.ToString() ?? "no"} cases but owns {boundCases}.");
                }
            }

            if (source.ArtifactState == ArtifactState.Missing
                && (source.ProofUse != ProofUse.Ineligible
                    || source.Provenance.Currency != EvidenceCurrency.Absent
                    || source.Provenance.PinState != PinState.Missing))
            {
                Add(diagnostics, "source.missing-used-as-proof", $"Missing source '{source.SourceId}' is not fully ineligible.");
            }

            if ((source.Provenance.Access != EvidenceAccess.Normal
                    || source.Provenance.Independence is Independence.DirtySide
                        or Independence.PendingIndependentReview)
                && source.ProofUse != ProofUse.Ineligible)
            {
                Add(diagnostics, "source.restricted-used-as-proof", $"Restricted source '{source.SourceId}' is proof-eligible.");
            }

            if (source.ProofUse == ProofUse.Eligible
                && (source.ArtifactState is ArtifactState.Missing or ArtifactState.Superseded
                    || source.Provenance.PinState is PinState.Missing or PinState.Unpinned))
            {
                Add(diagnostics, "source.weak-proof", $"Eligible source '{source.SourceId}' is absent, superseded, or unpinned.");
            }

            if (source.ProofUse == ProofUse.Eligible
                && snapshot.SourceIdentityMatches?.GetValueOrDefault(source.SourceId) != true)
            {
                Add(
                    diagnostics,
                    "source.identity-not-resolved",
                    $"Eligible source '{source.SourceId}' does not resolve to its declared pinned repository or artifact identity.");
            }

            if (source.ProofUse == ProofUse.Eligible
                && source.EvidenceClass is not EvidenceClass.ArronixOwnerDecision
                    and not EvidenceClass.ArchitectureGovernance
                && !HasIndependentCurrentProvenance(source))
            {
                Add(
                    diagnostics,
                    "source.ineligible-provenance",
                    $"Eligible source '{source.SourceId}' is not independent, current, normally accessible, and revision-pinned.");
            }

            if (source.EvidenceClass == EvidenceClass.ArronixOwnerDecision
                && (source.CaseCount != 0
                    || source.ProofUse != ProofUse.Eligible
                    || source.Provenance.Independence != Independence.NotApplicable
                    || source.Provenance.Access != EvidenceAccess.Normal
                    || source.Provenance.PinState is not (PinState.RepositoryPinned or PinState.ArtifactPinned)
                    || source.ArtifactState == ArtifactState.Empty
                        && source.Provenance.Currency != EvidenceCurrency.CurrentBaseline
                    || source.ArtifactState == ArtifactState.Current
                        && source.Provenance.Currency != EvidenceCurrency.Current
                    || source.ArtifactState is not (ArtifactState.Empty or ArtifactState.Current)))
            {
                Add(
                    diagnostics,
                    "source.invalid-owner-decision",
                    $"Owner-decision source '{source.SourceId}' is neither the empty inventory nor a pinned current decision record.");
            }

            if (source.EvidenceClass == EvidenceClass.ArchitectureGovernance
                && source.ProofUse == ProofUse.Eligible
                && (source.Provenance.Independence != Independence.NotApplicable
                    || source.Provenance.Currency != EvidenceCurrency.CurrentBaseline))
            {
                Add(diagnostics, "source.invalid-governance-provenance", $"Governance source '{source.SourceId}' has incompatible provenance.");
            }
        }
    }

    private static void ValidateRequirements(
        IEnumerable<CompatibilityRequirement> values,
        IReadOnlyDictionary<string, CompatibilitySource> sources,
        IReadOnlyDictionary<string, CompatibilityCase> cases,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        foreach (var requirement in values)
        {
            if (requirement.SchemaVersion != 1)
            {
                Add(
                    diagnostics,
                    "requirement.schema-version",
                    $"Requirement '{requirement.RequirementId}' has schema {requirement.SchemaVersion}.");
            }

            ValidateId(requirement.RequirementId, "requirement.invalid-id", diagnostics);
            RequireText(requirement.Title, "requirement.empty-title", requirement.RequirementId, diagnostics);
            RequireText(requirement.Statement, "requirement.empty-statement", requirement.RequirementId, diagnostics);
            ValidateId(requirement.Slice, "requirement.invalid-slice", diagnostics);
            ValidateId(requirement.Owner.Id, "requirement.invalid-owner", diagnostics);
            RequireText(requirement.CurrentReason, "requirement.empty-reason", requirement.RequirementId, diagnostics);
            ValidateGate(requirement.Target.ClassificationGate, "requirement.invalid-gate", diagnostics);
            ValidateGate(requirement.Target.ClosureGate, "requirement.invalid-gate", diagnostics);
            foreach (var gate in requirement.Target.Prerequisites)
            {
                ValidateGate(gate, "requirement.invalid-gate", diagnostics);
            }

            if (requirement.SourceIds.Count == 0 || requirement.SourceIds.Distinct(StringComparer.Ordinal).Count() != requirement.SourceIds.Count)
            {
                Add(diagnostics, "requirement.invalid-sources", $"Requirement '{requirement.RequirementId}' has no unique sources.");
            }

            foreach (var sourceId in requirement.SourceIds.Where(sourceId => !sources.ContainsKey(sourceId)))
            {
                Add(
                    diagnostics,
                    "requirement.unknown-source",
                    $"Requirement '{requirement.RequirementId}' names unknown source '{sourceId}'.");
            }

            var caseCount = cases.Values.Count(value => string.Equals(
                value.RequirementId,
                requirement.RequirementId,
                StringComparison.Ordinal));
            if (caseCount != requirement.CaseCount)
            {
                Add(
                    diagnostics,
                    "requirement.case-count",
                    $"Requirement '{requirement.RequirementId}' declares {requirement.CaseCount} cases but owns {caseCount}.");
            }

            if (requirement.BaselineStatus == BaselineStatus.Skipped && requirement.CaseCount == 0)
            {
                Add(diagnostics, "requirement.skipped-without-case", $"Skipped requirement '{requirement.RequirementId}' has no case.");
            }

            if (requirement.Disposition == RequirementDisposition.InventoryZero
                && (requirement.BaselineStatus != BaselineStatus.InventoryZero
                    || requirement.ClosurePolicy != ClosurePolicy.InventoryZero
                    || requirement.CaseCount != 0))
            {
                Add(diagnostics, "requirement.invalid-inventory-zero", $"Requirement '{requirement.RequirementId}' is not a zero inventory.");
            }

            if (requirement.Disposition == RequirementDisposition.ScopeCorrectionCandidate
                && (requirement.BaselineStatus != BaselineStatus.RecordedException
                    || requirement.ClosurePolicy != ClosurePolicy.OwnerDecision))
            {
                Add(diagnostics, "requirement.invalid-scope-candidate", $"Requirement '{requirement.RequirementId}' lacks owner-decision policy.");
            }
        }
    }

    private static void ValidateCases(
        IEnumerable<CompatibilityCase> values,
        IReadOnlyDictionary<string, CompatibilitySource> sources,
        IReadOnlyDictionary<string, CompatibilityRequirement> requirements,
        CompatibilityBaselineDocument baseline,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var runIds = baseline.Runs.Select(static value => value.RunId).ToHashSet(StringComparer.Ordinal);
        foreach (var compatibilityCase in values)
        {
            if (compatibilityCase.SchemaVersion != 1)
            {
                Add(diagnostics, "case.schema-version", $"Case '{compatibilityCase.CaseId}' has schema {compatibilityCase.SchemaVersion}.");
            }

            ValidateId(compatibilityCase.CaseId, "case.invalid-id", diagnostics);
            if (!requirements.TryGetValue(compatibilityCase.RequirementId, out var requirement))
            {
                Add(diagnostics, "case.unknown-requirement", $"Case '{compatibilityCase.CaseId}' has unknown requirement '{compatibilityCase.RequirementId}'.");
            }
            else if (!requirement.SourceIds.Contains(compatibilityCase.SourceId, StringComparer.Ordinal))
            {
                Add(diagnostics, "case.source-outside-requirement", $"Case '{compatibilityCase.CaseId}' source is not on its requirement.");
            }
            else if (compatibilityCase.Introduced?.Role != IntroducedCaseRole.ReplacementWitness
                && !CaseDispositionMatchesRequirement(
                    compatibilityCase.Disposition.Kind,
                    requirement.Disposition))
            {
                Add(
                    diagnostics,
                    "case.requirement-disposition-mismatch",
                    $"Case '{compatibilityCase.CaseId}' does not share its requirement's disposition.");
            }

            if (!sources.ContainsKey(compatibilityCase.SourceId))
            {
                Add(diagnostics, "case.unknown-source", $"Case '{compatibilityCase.CaseId}' has unknown source '{compatibilityCase.SourceId}'.");
            }

            ValidateBinding(compatibilityCase, diagnostics);
            ValidateExpectation(compatibilityCase, diagnostics);
            ValidateGate(compatibilityCase.Disposition.ClassificationGate, "case.invalid-gate", diagnostics);
            ValidateGate(compatibilityCase.Disposition.ClosureGate, "case.invalid-gate", diagnostics);

            if ((compatibilityCase.Baseline is null) == (compatibilityCase.Introduced is null))
            {
                Add(diagnostics, "case.invalid-origin", $"Case '{compatibilityCase.CaseId}' must have exactly one origin record.");
            }

            if (compatibilityCase.Baseline is not null)
            {
                if (!runIds.Contains(compatibilityCase.Baseline.RunId)
                    || !string.Equals(compatibilityCase.Baseline.Outcome, "skipped", StringComparison.Ordinal)
                    || !IsDigest(compatibilityCase.Baseline.ReasonDigest))
                {
                    Add(diagnostics, "case.invalid-baseline", $"Case '{compatibilityCase.CaseId}' has an invalid baseline record.");
                }
            }

            if (compatibilityCase.Introduced is not null
                && (!IsGate(compatibilityCase.Introduced.RegisteredAtGate)
                    || !string.Equals(compatibilityCase.Introduced.ExpectedResult, "passed", StringComparison.Ordinal)))
            {
                Add(diagnostics, "case.invalid-introduction", $"Case '{compatibilityCase.CaseId}' has an invalid introduction record.");
            }
        }
    }

    private static void ValidateBinding(CompatibilityCase compatibilityCase, ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var binding = compatibilityCase.Binding;
        var supportFiles = binding.SupportDocuments.Select(static value => value.SourceFile).ToArray();
        if (!string.Equals(binding.Framework, "nunit", StringComparison.Ordinal)
            || !IsRelativePath(binding.Project)
            || !IsRelativePath(binding.SourceFile)
            || string.IsNullOrWhiteSpace(binding.Fixture)
            || string.IsNullOrWhiteSpace(binding.Method)
            || !IsDigest(binding.FullNameDigest)
            || !IsDigest(binding.SourceFileDigest)
            || binding.SupportDocuments.Any(static value =>
                !IsRelativePath(value.SourceFile) || !IsDigest(value.SourceFileDigest))
            || supportFiles.Distinct(StringComparer.Ordinal).Count() != supportFiles.Length
            || supportFiles.Contains(binding.SourceFile, StringComparer.Ordinal))
        {
            Add(diagnostics, "case.invalid-binding", $"Case '{compatibilityCase.CaseId}' has an invalid NUnit binding.");
        }
    }

    private static void ValidateExpectation(CompatibilityCase compatibilityCase, ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var expected = compatibilityCase.Expected;
        if (expected.State == ExpectedState.KnownRegression)
        {
            if (expected.Kind is null || !IsDigest(expected.SemanticDigest) || expected.GapReason is not null)
            {
                Add(diagnostics, "case.invalid-known-expectation", $"Case '{compatibilityCase.CaseId}' has an incomplete known expectation.");
            }
        }
        else if (expected.Kind is not null || expected.SemanticDigest is not null || string.IsNullOrWhiteSpace(expected.GapReason))
        {
            Add(diagnostics, "case.invalid-lost-expectation", $"Case '{compatibilityCase.CaseId}' has an invalid lost expectation.");
        }
    }

    private static IReadOnlyDictionary<ExecutionKey, NUnitTestCaseResult> IndexExecutions(
        NUnitTestRun run,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var projects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in run.Projects)
        {
            var key = ProjectKey(project.Project);
            if (!projects.Add(key))
            {
                Add(diagnostics, "execution.duplicate-project", $"NUnit project '{key}' appears in multiple result documents.");
            }
        }

        var result = new Dictionary<ExecutionKey, NUnitTestCaseResult>();
        foreach (var test in run.Tests)
        {
            var key = new ExecutionKey(ProjectKey(test.Project), CompatibilityDigest.Sha256(test.FullName));
            if (!result.TryAdd(key, test))
            {
                Add(diagnostics, "execution.duplicate-binding", $"NUnit binding '{key}' executed more than once.");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<ExecutionKey, CompatibilityCase> IndexBindings(
        IEnumerable<CompatibilityCase> cases,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var result = new Dictionary<ExecutionKey, CompatibilityCase>();
        foreach (var compatibilityCase in cases)
        {
            var key = new ExecutionKey(ProjectKey(compatibilityCase.Binding.Project), compatibilityCase.Binding.FullNameDigest);
            if (!result.TryAdd(key, compatibilityCase))
            {
                Add(diagnostics, "case.duplicate-binding", $"Cases '{result[key].CaseId}' and '{compatibilityCase.CaseId}' share one NUnit binding.");
            }
        }

        return result;
    }

    private static ExecutionState ValidateExecutions(
        NUnitTestRun run,
        int currentSkipCount,
        IReadOnlyDictionary<string, CompatibilitySource> sources,
        IReadOnlyDictionary<string, CompatibilityCase> cases,
        IReadOnlyDictionary<ExecutionKey, CompatibilityCase> bindings,
        IReadOnlyDictionary<ExecutionKey, NUnitTestCaseResult> executions,
        RepositorySnapshot snapshot,
        CompatibilityLedger? previousLedger,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (run.Counts.Skipped > currentSkipCount)
        {
            Add(diagnostics, "execution.skip-ceiling-exceeded", $"The run skipped {run.Counts.Skipped} cases; the committed current ceiling is {currentSkipCount}.");
        }
        else if (run.Counts.Skipped < currentSkipCount)
        {
            Add(
                diagnostics,
                "execution.skip-count-not-ratcheted",
                $"The run skipped {run.Counts.Skipped} cases; reduce the committed current skip count from {currentSkipCount} to lock in the improvement.");
        }

        if (run.Counts.Failed != 0)
        {
            Add(diagnostics, "execution.failed", $"The run contains {run.Counts.Failed} failed cases.");
        }

        if (run.Counts.Inconclusive != 0)
        {
            Add(diagnostics, "execution.inconclusive", $"The run contains {run.Counts.Inconclusive} inconclusive cases.");
        }

        foreach (var skipped in run.Tests.Where(static value => value.Outcome == NUnitTestOutcome.Skipped))
        {
            var key = new ExecutionKey(ProjectKey(skipped.Project), CompatibilityDigest.Sha256(skipped.FullName));
            if (!bindings.ContainsKey(key))
            {
                Add(diagnostics, "execution.unregistered-skip", $"Skipped test '{skipped.FullName}' has no exact case binding.");
            }
        }

        var byCase = new Dictionary<string, NUnitTestCaseResult>(StringComparer.Ordinal);
        var witnessValid = new Dictionary<string, bool>(StringComparer.Ordinal);
        var passingWitnesses = 0;
        var closureEligibleWitnesses = 0;
        var previousCases = previousLedger?.Cases.ToDictionary(static value => value.CaseId, StringComparer.Ordinal);
        foreach (var compatibilityCase in cases.Values)
        {
            var key = new ExecutionKey(ProjectKey(compatibilityCase.Binding.Project), compatibilityCase.Binding.FullNameDigest);
            if (!executions.TryGetValue(key, out var execution))
            {
                continue;
            }

            byCase.Add(compatibilityCase.CaseId, execution);
            if (!execution.FullName.StartsWith(compatibilityCase.Binding.Fixture + ".", StringComparison.Ordinal))
            {
                Add(diagnostics, "execution.fixture-mismatch", $"Case '{compatibilityCase.CaseId}' no longer executes in its bound fixture.");
            }

            var compiledValid = snapshot.CompiledSourceVerifications is not null
                && snapshot.CompiledSourceVerifications.TryGetValue(
                    compatibilityCase.CaseId,
                    out var compiledVerification)
                && compiledVerification.IsValid;
            if (!compiledValid)
            {
                var reason = snapshot.CompiledSourceVerifications?.GetValueOrDefault(
                    compatibilityCase.CaseId);
                Add(
                    diagnostics,
                    "execution.compiled-source-mismatch",
                    $"Case '{compatibilityCase.CaseId}' is not bound to its declared compiled method source"
                    + (reason is null ? "." : $": {reason.Code}: {reason.Message}"));
            }

            switch (execution.Outcome)
            {
                case NUnitTestOutcome.Skipped:
                    witnessValid.Add(compatibilityCase.CaseId, false);
                    if (compatibilityCase.Introduced is not null)
                    {
                        Add(diagnostics, "execution.new-case-skipped", $"Introduced case '{compatibilityCase.CaseId}' is skipped.");
                    }

                    break;
                case NUnitTestOutcome.Passed:
                    var sourceValid = ValidatePassingWitness(
                        compatibilityCase,
                        snapshot,
                        previousCases,
                        diagnostics);
                    var valid = compiledValid && sourceValid;
                    witnessValid.Add(compatibilityCase.CaseId, valid);
                    if (valid)
                    {
                        passingWitnesses++;
                        if (sources.TryGetValue(compatibilityCase.SourceId, out var source)
                            && IsClosureEligibleSource(source, compatibilityCase, snapshot))
                        {
                            closureEligibleWitnesses++;
                        }
                        else if (compatibilityCase.Introduced is not null)
                        {
                            Add(
                                diagnostics,
                                "execution.introduced-ineligible-source",
                                $"Introduced case '{compatibilityCase.CaseId}' does not use closure-eligible evidence.");
                        }
                    }

                    break;
                case NUnitTestOutcome.Failed:
                case NUnitTestOutcome.Inconclusive:
                    witnessValid.Add(compatibilityCase.CaseId, false);
                    Add(diagnostics, "execution.case-not-successful", $"Case '{compatibilityCase.CaseId}' executed as {execution.Outcome}.");
                    break;
                default:
                    throw new InvalidOperationException($"Unknown NUnit outcome '{execution.Outcome}'.");
            }
        }

        return new ExecutionState(byCase, witnessValid, passingWitnesses, closureEligibleWitnesses);
    }

    private static bool ValidatePassingWitness(
        CompatibilityCase compatibilityCase,
        RepositorySnapshot snapshot,
        IReadOnlyDictionary<string, CompatibilityCase>? previousCases,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (compatibilityCase.Expected.State != ExpectedState.KnownRegression
            || compatibilityCase.Expected.SemanticDigest is null)
        {
            Add(
                diagnostics,
                "witness.unknown-expectation",
                $"Passing case '{compatibilityCase.CaseId}' has no locked known expectation.");
            return false;
        }

        if (EnumerateSourceDocuments(compatibilityCase.Binding).Any(document =>
                !snapshot.FileDigests.TryGetValue(document.SourceFile, out var sourceDigest)
                || !string.Equals(sourceDigest, document.SourceFileDigest, StringComparison.Ordinal)))
        {
            Add(
                diagnostics,
                "witness.source-not-locked",
                $"Passing case '{compatibilityCase.CaseId}' does not execute from its locked source content.");
            return false;
        }

        if (previousCases is not null
            && previousCases.TryGetValue(compatibilityCase.CaseId, out var previousCase)
            && !SemanticallyEqual(previousCase, compatibilityCase))
        {
            Add(
                diagnostics,
                "witness.case-not-locked",
                $"Passing case '{compatibilityCase.CaseId}' changed since the prior ledger.");
            return false;
        }

        return true;
    }

    private static IReadOnlySet<string> ValidateReplacements(
        IEnumerable<CompatibilityReplacement> values,
        IReadOnlyDictionary<string, CompatibilityReplacement> replacements,
        IReadOnlyDictionary<string, CompatibilityCase> cases,
        IReadOnlyDictionary<string, CompatibilityRequirement> requirements,
        IReadOnlyDictionary<string, CompatibilitySource> sources,
        ExecutionState executionState,
        RepositorySnapshot snapshot,
        CompatibilityLedger? previousLedger,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var targetOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var structurallyValid = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var replacement in values)
        {
            var valid = replacement.SchemaVersion == 1
                && !string.IsNullOrWhiteSpace(replacement.Rationale)
                && replacement.ToCaseIds.Count > 0
                && replacement.ToCaseIds.Distinct(StringComparer.Ordinal).Count() == replacement.ToCaseIds.Count;
            ValidateId(replacement.ReplacementId, "replacement.invalid-id", diagnostics);
            if (!valid)
            {
                Add(diagnostics, "replacement.invalid-record", $"Replacement '{replacement.ReplacementId}' is incomplete.");
            }

            CompatibilityRequirement? sourceRequirement = null;
            if (!cases.TryGetValue(replacement.FromCaseId, out var sourceCase))
            {
                Add(diagnostics, "replacement.unknown-source-case", $"Replacement '{replacement.ReplacementId}' has unknown source case '{replacement.FromCaseId}'.");
                valid = false;
            }
            else
            {
                if (!OutcomeMatchesCaseDisposition(replacement.Outcome, sourceCase.Disposition.Kind))
                {
                    Add(
                        diagnostics,
                        "replacement.source-disposition-mismatch",
                        $"Replacement '{replacement.ReplacementId}' cannot resolve source disposition '{sourceCase.Disposition.Kind}'.");
                    valid = false;
                }

                if (replacement.Outcome != ReplacementOutcome.EvidenceRecovered
                    && sourceCase.Expected.State != ExpectedState.KnownRegression)
                {
                    Add(
                        diagnostics,
                        "replacement.source-semantics-unknown",
                        $"Replacement outcome '{replacement.Outcome}' requires known source semantics.");
                    valid = false;
                }

                if (!requirements.TryGetValue(sourceCase.RequirementId, out sourceRequirement)
                    || !OutcomeMatchesRequirementDisposition(
                        replacement.Outcome,
                        sourceRequirement.Disposition))
                {
                    Add(
                        diagnostics,
                        "replacement.requirement-disposition-mismatch",
                        $"Replacement '{replacement.ReplacementId}' does not match its source requirement's disposition.");
                    valid = false;
                }
            }

            foreach (var target in replacement.ToCaseIds)
            {
                if (!cases.ContainsKey(target) || string.Equals(target, replacement.FromCaseId, StringComparison.Ordinal))
                {
                    Add(diagnostics, "replacement.invalid-target", $"Replacement '{replacement.ReplacementId}' has invalid target '{target}'.");
                    valid = false;
                }

                if (cases.TryGetValue(target, out var targetCase)
                    && (targetCase.Introduced is null
                        || targetCase.Baseline is not null
                        || targetCase.Introduced.Role != IntroducedCaseRole.ReplacementWitness
                        || targetCase.Disposition.Kind != CaseDispositionKind.Proven
                        || targetCase.Expected.State != ExpectedState.KnownRegression))
                {
                    Add(
                        diagnostics,
                        "replacement.target-not-executable",
                        $"Replacement target '{target}' is not a permanent introduced executable witness.");
                    valid = false;
                }

                if (sourceCase is not null
                    && cases.TryGetValue(target, out targetCase)
                    && RequiresMatchingExpectedKind(replacement.Outcome, sourceCase.Expected.State)
                    && targetCase.Expected.Kind != sourceCase.Expected.Kind)
                {
                    Add(
                        diagnostics,
                        "replacement.target-kind-mismatch",
                        $"Replacement target '{target}' proves a different expectation kind from its source case.");
                    valid = false;
                }

                if (sourceCase is not null
                    && sourceRequirement is not null
                    && cases.TryGetValue(target, out targetCase)
                    && requirements.TryGetValue(targetCase.RequirementId, out var targetRequirement)
                    && !ValidTargetRequirementTransition(
                        replacement.Outcome,
                        sourceRequirement,
                        targetRequirement))
                {
                    Add(
                        diagnostics,
                        "replacement.target-requirement-transition",
                        $"Replacement target '{target}' does not make the requirement or ownership transition claimed by '{replacement.Outcome}'.");
                    valid = false;
                }

                if (!targetOwners.TryAdd(target, replacement.ReplacementId))
                {
                    Add(diagnostics, "replacement.target-reused", $"Target case '{target}' is reused by multiple replacements.");
                    valid = false;
                }
            }

            if (replacement.Coverage == ReplacementCoverage.Partial && replacement.Status != ReplacementStatus.Candidate)
            {
                Add(diagnostics, "replacement.partial-not-candidate", $"Partial replacement '{replacement.ReplacementId}' is not a candidate.");
                valid = false;
            }

            if (replacement.Shape == ReplacementShape.OneToOne
                && replacement.ToCaseIds.Count != 1)
            {
                Add(
                    diagnostics,
                    "replacement.one-to-one-target-count",
                    $"One-to-one replacement '{replacement.ReplacementId}' must have exactly one target case.");
                valid = false;
            }

            if (replacement.Shape == ReplacementShape.Partition
                && replacement.ToCaseIds.Count < 2)
            {
                Add(
                    diagnostics,
                    "replacement.partition-target-count",
                    $"Partition replacement '{replacement.ReplacementId}' must have at least two target cases.");
                valid = false;
            }

            if (replacement.Shape == ReplacementShape.Partition
                && replacement.Status == ReplacementStatus.Verified)
            {
                Add(
                    diagnostics,
                    "replacement.partition-composition-unmodeled",
                    $"Partition replacement '{replacement.ReplacementId}' cannot be verified until its aggregate semantic composition is modeled.");
                valid = false;
            }

            if (RequiresOwnerDecision(replacement, sourceCase))
            {
                if (string.IsNullOrWhiteSpace(replacement.DecisionReference))
                {
                    Add(diagnostics, "replacement.missing-decision", $"Replacement '{replacement.ReplacementId}' requires an owner decision.");
                    valid = false;
                }
                else
                {
                    ValidateId(
                        replacement.DecisionReference,
                        "replacement.invalid-decision",
                        diagnostics);
                    if (!sources.TryGetValue(replacement.DecisionReference, out var decision)
                        || !IsResolvedOwnerDecision(decision, snapshot))
                    {
                        Add(
                            diagnostics,
                            "replacement.unresolved-decision",
                            $"Replacement '{replacement.ReplacementId}' does not reference a pinned current owner-decision source.");
                        valid = false;
                    }

                    if (sourceRequirement is null
                        || !sourceRequirement.SourceIds.Contains(
                            replacement.DecisionReference,
                            StringComparer.Ordinal))
                    {
                        Add(
                            diagnostics,
                            "replacement.decision-outside-requirement",
                            $"Replacement '{replacement.ReplacementId}' uses an owner decision that does not belong to its source requirement.");
                        valid = false;
                    }
                }
            }

            structurallyValid[replacement.ReplacementId] = valid;
        }

        foreach (var orphan in cases.Values.Where(value =>
                     value.Introduced?.Role == IntroducedCaseRole.ReplacementWitness
                     && !targetOwners.ContainsKey(value.CaseId)))
        {
            Add(
                diagnostics,
                "replacement.orphan-target",
                $"Replacement-target case '{orphan.CaseId}' is not owned by a replacement record.");
        }

        foreach (var duplicateFrom in values.GroupBy(static value => value.FromCaseId, StringComparer.Ordinal).Where(static group => group.Count() > 1))
        {
            Add(diagnostics, "replacement.duplicate-source", $"Case '{duplicateFrom.Key}' has multiple replacement records.");
            foreach (var replacement in duplicateFrom)
            {
                structurallyValid[replacement.ReplacementId] = false;
            }
        }

        var cycleMembers = FindCycleMembers(values);
        foreach (var member in cycleMembers)
        {
            Add(diagnostics, "replacement.cycle", $"Replacement graph contains case '{member}' in a cycle.");
        }

        var closed = new HashSet<string>(StringComparer.Ordinal);
        var closureCandidates = new List<ReplacementClosureCandidate>();
        var previousReplacements = previousLedger?.Replacements.ToDictionary(
            static value => value.ReplacementId,
            StringComparer.Ordinal);
        var previousCases = previousLedger?.Cases.ToDictionary(static value => value.CaseId, StringComparer.Ordinal);
        var previousSources = previousLedger?.Sources.ToDictionary(static value => value.SourceId, StringComparer.Ordinal);
        foreach (var replacement in replacements.Values)
        {
            if (replacement.Status != ReplacementStatus.Verified
                || replacement.Coverage != ReplacementCoverage.Full
                || !structurallyValid.GetValueOrDefault(replacement.ReplacementId)
                || cycleMembers.Contains(replacement.FromCaseId))
            {
                continue;
            }

            if (previousReplacements is null
                || !previousReplacements.TryGetValue(replacement.ReplacementId, out var previousReplacement)
                || !SameReplacementDefinition(previousReplacement, replacement))
            {
                Add(
                    diagnostics,
                    "replacement.not-history-anchored",
                    $"Verified replacement '{replacement.ReplacementId}' was not registered unchanged in the prior ledger.");
                continue;
            }

            var witnesses = new HashSet<string>(StringComparer.Ordinal);
            var directWitnesses = new HashSet<string>(StringComparer.Ordinal);
            var valid = true;
            var fromDigest = cases.TryGetValue(replacement.FromCaseId, out var from)
                ? from.Expected.SemanticDigest
                : null;
            if (RequiresOwnerDecision(replacement, from)
                && (replacement.DecisionReference is null
                    || !string.Equals(
                        previousReplacement.DecisionReference,
                        replacement.DecisionReference,
                        StringComparison.Ordinal)
                    || !sources.TryGetValue(replacement.DecisionReference, out var decision)
                    || previousSources is null
                    || !previousSources.TryGetValue(replacement.DecisionReference, out var previousDecision)
                    || !SemanticallyEqual(previousDecision, decision)))
            {
                Add(
                    diagnostics,
                    "replacement.decision-not-history-anchored",
                    $"Replacement '{replacement.ReplacementId}' does not use an unchanged owner decision from the prior ledger.");
                valid = false;
            }

            foreach (var targetId in replacement.ToCaseIds)
            {
                if (!cases.TryGetValue(targetId, out var target))
                {
                    valid = false;
                    continue;
                }

                var hasPassingWitness = executionState.ExecutionsByCase.TryGetValue(targetId, out var execution)
                    && execution.Outcome == NUnitTestOutcome.Passed
                    && executionState.WitnessValid.GetValueOrDefault(targetId);
                var hasSource = sources.TryGetValue(target.SourceId, out var source);
                if (hasPassingWitness
                    && hasSource
                    && IsClosureEligibleSource(source!, target, snapshot))
                {
                    directWitnesses.Add(targetId);
                }

                if (previousCases is null
                    || !previousCases.TryGetValue(targetId, out var previousTarget)
                    || !SemanticallyEqual(previousTarget, target)
                    || !hasSource
                    || previousSources is null
                    || !previousSources.TryGetValue(target.SourceId, out var previousSource)
                    || !SemanticallyEqual(previousSource, source!))
                {
                    Add(
                        diagnostics,
                        "replacement.target-not-history-anchored",
                        $"Replacement target '{targetId}' or its evidence source changed since the prior ledger.");
                    valid = false;
                }

                var digest = target.Expected.SemanticDigest;
                if (digest is null)
                {
                    Add(
                        diagnostics,
                        "replacement.target-missing-semantics",
                        $"Replacement target '{targetId}' has no locked semantic expectation.");
                    valid = false;
                }
                else if (MustPreserveSourceDigest(replacement, from))
                {
                    if (!string.Equals(digest, fromDigest, StringComparison.Ordinal))
                    {
                        Add(
                            diagnostics,
                            "replacement.target-changed-semantics",
                            $"Replacement target '{targetId}' does not preserve the source case semantics required by '{replacement.Outcome}'.");
                        valid = false;
                    }

                    if (!witnesses.Add(digest))
                    {
                        Add(
                            diagnostics,
                            "replacement.target-not-distinct",
                            $"Replacement target '{targetId}' duplicates another target's semantic expectation.");
                        valid = false;
                    }
                }
                else if (MustChangeSourceDigest(replacement.Outcome)
                    && string.Equals(digest, fromDigest, StringComparison.Ordinal))
                {
                    Add(
                        diagnostics,
                        "replacement.target-unchanged-semantics",
                        $"Replacement target '{targetId}' does not express the semantic change claimed by '{replacement.Outcome}'.");
                    valid = false;
                }
                else if (!witnesses.Add(digest))
                {
                    Add(
                        diagnostics,
                        "replacement.target-not-distinct",
                        $"Replacement target '{targetId}' duplicates another target's semantic expectation.");
                    valid = false;
                }
            }

            if (valid)
            {
                closureCandidates.Add(new ReplacementClosureCandidate(replacement, directWitnesses));
            }
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var candidate in closureCandidates)
            {
                if (!closed.Contains(candidate.Replacement.FromCaseId)
                    && candidate.Replacement.ToCaseIds.All(target =>
                        candidate.DirectWitnesses.Contains(target) || closed.Contains(target)))
                {
                    changed |= closed.Add(candidate.Replacement.FromCaseId);
                }
            }
        }
        while (changed);

        foreach (var candidate in closureCandidates.Where(candidate =>
                     !closed.Contains(candidate.Replacement.FromCaseId)))
        {
            foreach (var targetId in candidate.Replacement.ToCaseIds.Where(target =>
                         !candidate.DirectWitnesses.Contains(target) && !closed.Contains(target)))
            {
                var hasPassingWitness = executionState.ExecutionsByCase.TryGetValue(targetId, out var execution)
                    && execution.Outcome == NUnitTestOutcome.Passed
                    && executionState.WitnessValid.GetValueOrDefault(targetId);
                Add(
                    diagnostics,
                    hasPassingWitness ? "replacement.target-ineligible" : "replacement.target-not-proven",
                    hasPassingWitness
                        ? $"Replacement target '{targetId}' does not use eligible provenance."
                        : $"Replacement target '{targetId}' is neither a passing semantic witness nor closed by its own verified replacement.");
            }
        }

        return closed;
    }

    private static IReadOnlySet<string> FindCycleMembers(IEnumerable<CompatibilityReplacement> replacements)
    {
        var graph = replacements
            .GroupBy(static value => value.FromCaseId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.SelectMany(static value => value.ToCaseIds).ToArray(),
                StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in graph.Keys)
        {
            var stack = new Stack<(string Node, IReadOnlyList<string> Path)>();
            stack.Push((start, []));
            while (stack.Count > 0)
            {
                var (node, path) = stack.Pop();
                var index = Array.IndexOf(path.ToArray(), node);
                if (index >= 0)
                {
                    foreach (var member in path.Skip(index))
                    {
                        result.Add(member);
                    }

                    continue;
                }

                if (!graph.TryGetValue(node, out var targets))
                {
                    continue;
                }

                var nextPath = path.Append(node).ToArray();
                foreach (var target in targets)
                {
                    stack.Push((target, nextPath));
                }
            }
        }

        return result;
    }

    private static void ValidateDisappearances(
        IEnumerable<CompatibilityCase> cases,
        IReadOnlyDictionary<string, NUnitTestCaseResult> executions,
        IReadOnlySet<string> closedByReplacement,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        foreach (var compatibilityCase in cases)
        {
            if (!executions.ContainsKey(compatibilityCase.CaseId) && !closedByReplacement.Contains(compatibilityCase.CaseId))
            {
                Add(diagnostics, "execution.case-disappeared", $"Permanent case '{compatibilityCase.CaseId}' did not execute and has no verified replacement.");
            }
        }
    }

    private static void ValidateSnapshot(
        IEnumerable<CompatibilityCase> cases,
        RepositorySnapshot snapshot,
        IReadOnlySet<string> closedByReplacement,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        foreach (var compatibilityCase in cases)
        {
            if (closedByReplacement.Contains(compatibilityCase.CaseId))
            {
                continue;
            }

            foreach (var document in EnumerateSourceDocuments(compatibilityCase.Binding))
            {
                if (!snapshot.FileDigests.TryGetValue(document.SourceFile, out var digest))
                {
                    Add(
                        diagnostics,
                        "snapshot.source-missing",
                        $"Case '{compatibilityCase.CaseId}' source file '{document.SourceFile}' is missing.");
                }
                else if (!string.Equals(digest, document.SourceFileDigest, StringComparison.Ordinal))
                {
                    Add(
                        diagnostics,
                        "snapshot.source-changed",
                        $"Case '{compatibilityCase.CaseId}' source file '{document.SourceFile}' digest changed.");
                }
            }
        }
    }

    private static IEnumerable<CaseSupportDocument> EnumerateSourceDocuments(CaseBinding binding)
    {
        yield return new CaseSupportDocument
        {
            SourceFile = binding.SourceFile,
            SourceFileDigest = binding.SourceFileDigest
        };
        foreach (var supportDocument in binding.SupportDocuments)
        {
            yield return supportDocument;
        }
    }

    private static void ValidateHistory(
        CompatibilityLedger previous,
        CompatibilityLedger current,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (!SemanticallyEqual(
                previous.Baseline with { CurrentSkipCount = current.Baseline.CurrentSkipCount },
                current.Baseline))
        {
            Add(diagnostics, "history.baseline-changed", "The immutable R00 baseline document changed.");
        }

        if (current.Baseline.CurrentSkipCount > previous.Baseline.CurrentSkipCount)
        {
            Add(diagnostics, "history.ceiling-increased", "The checked-in current skip count increased.");
        }

        ValidateRetained(previous.Sources, current.Sources, static value => value.SourceId, "history.source-removed", diagnostics);
        ValidateRetained(previous.Requirements, current.Requirements, static value => value.RequirementId, "history.requirement-removed", diagnostics);
        ValidateRetained(previous.Cases, current.Cases, static value => value.CaseId, "history.case-removed", diagnostics);
        ValidateRetained(previous.Replacements, current.Replacements, static value => value.ReplacementId, "history.replacement-removed", diagnostics);

        var currentSources = current.Sources.ToDictionary(static value => value.SourceId, StringComparer.Ordinal);
        foreach (var oldSource in previous.Sources)
        {
            if (currentSources.TryGetValue(oldSource.SourceId, out var newSource)
                && !SemanticallyEqual(oldSource, newSource))
            {
                Add(
                    diagnostics,
                    "history.source-changed",
                    $"Evidence source '{oldSource.SourceId}' changed; a new revision requires a new stable source identifier.");
            }
        }

        var currentRequirements = current.Requirements.ToDictionary(static value => value.RequirementId, StringComparer.Ordinal);
        foreach (var oldRequirement in previous.Requirements)
        {
            if (!currentRequirements.TryGetValue(oldRequirement.RequirementId, out var newRequirement))
            {
                continue;
            }

            if (!SameRequirementSemantics(oldRequirement, newRequirement))
            {
                Add(
                    diagnostics,
                    "history.requirement-changed",
                    $"Requirement '{oldRequirement.RequirementId}' changed its locked semantic definition.");
            }

            foreach (var source in oldRequirement.SourceIds.Except(newRequirement.SourceIds, StringComparer.Ordinal))
            {
                Add(diagnostics, "history.requirement-source-removed", $"Requirement '{oldRequirement.RequirementId}' lost source '{source}'.");
            }

            if (newRequirement.CaseCount < oldRequirement.CaseCount)
            {
                Add(
                    diagnostics,
                    "history.requirement-case-count-decreased",
                    $"Requirement '{oldRequirement.RequirementId}' lost registered cases.");
            }
        }

        var currentCases = current.Cases.ToDictionary(static value => value.CaseId, StringComparer.Ordinal);
        foreach (var oldCase in previous.Cases)
        {
            if (!currentCases.TryGetValue(oldCase.CaseId, out var newCase))
            {
                continue;
            }

            if (!SemanticallyEqual(oldCase, newCase))
            {
                Add(
                    diagnostics,
                    "history.case-changed",
                    $"Case '{oldCase.CaseId}' changed its binding, source, expectation, origin, or disposition; use a new stable case and replacement.");
            }
        }

        var currentReplacements = current.Replacements.ToDictionary(
            static value => value.ReplacementId,
            StringComparer.Ordinal);
        foreach (var oldReplacement in previous.Replacements)
        {
            if (!currentReplacements.TryGetValue(oldReplacement.ReplacementId, out var newReplacement))
            {
                continue;
            }

            if (!SameReplacementDefinition(oldReplacement, newReplacement))
            {
                Add(
                    diagnostics,
                    "history.replacement-changed",
                    $"Replacement '{oldReplacement.ReplacementId}' changed its locked graph or coverage definition.");
            }

            if (ReplacementStatusRank(newReplacement.Status) < ReplacementStatusRank(oldReplacement.Status))
            {
                Add(
                    diagnostics,
                    "history.replacement-status-regressed",
                    $"Replacement '{oldReplacement.ReplacementId}' moved to an earlier status.");
            }

            if (oldReplacement.DecisionReference is not null
                && !string.Equals(
                    oldReplacement.DecisionReference,
                    newReplacement.DecisionReference,
                    StringComparison.Ordinal))
            {
                Add(
                    diagnostics,
                    "history.replacement-decision-changed",
                    $"Replacement '{oldReplacement.ReplacementId}' changed or removed its decision reference.");
            }
        }
    }

    private static void ValidateRetained<T>(
        IEnumerable<T> previous,
        IEnumerable<T> current,
        Func<T, string> id,
        string code,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var currentIds = current.Select(id).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in previous.Select(id).Where(value => !currentIds.Contains(value)))
        {
            Add(diagnostics, code, $"Permanent identifier '{missing}' disappeared or was renamed.");
        }
    }

    private static Dictionary<string, T> IndexUnique<T>(
        IEnumerable<T> values,
        Func<T, string> id,
        string code,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var key = id(value);
            if (!result.TryAdd(key, value))
            {
                Add(diagnostics, code, $"Duplicate identifier '{key}'.");
            }
        }

        return result;
    }

    private static bool CountsAddUp(int total, int passed, int failed, int skipped, int inconclusive)
        => total >= 0 && passed >= 0 && failed >= 0 && skipped >= 0 && inconclusive >= 0
            && total == passed + failed + skipped + inconclusive;

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right)
        => left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void ValidateSourceIdentity(
        CompatibilitySource source,
        ICollection<CompatibilityDiagnostic> diagnostics)
    {
        var valid = source.Provenance.PinState switch
        {
            PinState.RepositoryPinned => source.Locator is not null
                && source.Revision is { Kind: RevisionKind.RepositoryCommit }
                && IsCommit(source.Revision.Value),
            PinState.ArtifactPinned => source.Locator is not null
                && source.Revision is { Kind: RevisionKind.ArtifactSha256 }
                && IsRawSha256(source.Revision.Value),
            PinState.Missing => source.Locator is null && source.Revision is null,
            PinState.Unpinned => source.Revision is null
                || source.Revision.Kind is RevisionKind.Unversioned or RevisionKind.Version,
            _ => false
        };

        if (!valid)
        {
            Add(
                diagnostics,
                "source.invalid-identity",
                $"Source '{source.SourceId}' locator, revision, and pin state do not identify one repository or artifact revision.");
        }
    }

    private static bool HasIndependentCurrentProvenance(CompatibilitySource source)
        => source.ArtifactState == ArtifactState.Current
            && source.Provenance.Independence == Independence.Independent
            && source.Provenance.Access == EvidenceAccess.Normal
            && source.Provenance.Currency == EvidenceCurrency.Current
            && source.Provenance.PinState is PinState.RepositoryPinned or PinState.ArtifactPinned
            && source.Locator is not null
            && source.Revision is not null;

    private static bool IsClosureEligibleSource(
        CompatibilitySource source,
        CompatibilityCase compatibilityCase,
        RepositorySnapshot snapshot)
        => source.ProofUse == ProofUse.Eligible
            && source.CaseCount is > 0
            && snapshot.SourceIdentityMatches?.GetValueOrDefault(source.SourceId) == true
            && (source.EvidenceClass switch
            {
                EvidenceClass.ArronixOwnerDecision => false,
                EvidenceClass.ArchitectureGovernance => compatibilityCase.Expected.Kind == ExpectedKind.Governance
                    && source.ArtifactState == ArtifactState.Current
                    && source.Provenance.Independence == Independence.NotApplicable
                    && source.Provenance.Access == EvidenceAccess.Normal
                    && source.Provenance.Currency == EvidenceCurrency.CurrentBaseline
                    && source.Provenance.PinState == PinState.RepositoryPinned,
                _ => HasIndependentCurrentProvenance(source)
            });

    private static bool IsResolvedOwnerDecision(
        CompatibilitySource source,
        RepositorySnapshot snapshot)
        => source.EvidenceClass == EvidenceClass.ArronixOwnerDecision
            && source.ArtifactState == ArtifactState.Current
            && source.CaseCount == 0
            && source.ProofUse == ProofUse.Eligible
            && source.Provenance.Independence == Independence.NotApplicable
            && source.Provenance.Access == EvidenceAccess.Normal
            && source.Provenance.Currency == EvidenceCurrency.Current
            && source.Provenance.PinState is PinState.RepositoryPinned or PinState.ArtifactPinned
            && source.Locator is not null
            && source.Revision is not null
            && snapshot.SourceIdentityMatches?.GetValueOrDefault(source.SourceId) == true;

    private static bool CaseDispositionMatchesRequirement(
        CaseDispositionKind caseDisposition,
        RequirementDisposition requirementDisposition)
        => (caseDisposition, requirementDisposition) switch
        {
            (CaseDispositionKind.Restore, RequirementDisposition.Restore) => true,
            (CaseDispositionKind.OwnershipCorrectReplacement, RequirementDisposition.OwnershipCorrectReplacement) => true,
            (CaseDispositionKind.EvidenceGap, RequirementDisposition.EvidenceGap) => true,
            (CaseDispositionKind.CandidateDivergence, RequirementDisposition.CandidateDivergence) => true,
            (CaseDispositionKind.ScopeCorrectionCandidate, RequirementDisposition.ScopeCorrectionCandidate) => true,
            (CaseDispositionKind.Proven, RequirementDisposition.Restore) => true,
            _ => false
        };

    private static bool OutcomeMatchesCaseDisposition(
        ReplacementOutcome outcome,
        CaseDispositionKind disposition)
        => (outcome, disposition) switch
        {
            (ReplacementOutcome.Equivalent, CaseDispositionKind.Restore) => true,
            (ReplacementOutcome.Equivalent, CaseDispositionKind.Proven) => true,
            (ReplacementOutcome.Equivalent, CaseDispositionKind.CandidateDivergence) => true,
            (ReplacementOutcome.Equivalent, CaseDispositionKind.ScopeCorrectionCandidate) => true,
            (ReplacementOutcome.OwnershipCorrect, CaseDispositionKind.OwnershipCorrectReplacement) => true,
            (ReplacementOutcome.EvidenceRecovered, CaseDispositionKind.EvidenceGap) => true,
            (ReplacementOutcome.ApprovedDivergence, CaseDispositionKind.CandidateDivergence) => true,
            (ReplacementOutcome.ScopeCorrection, CaseDispositionKind.ScopeCorrectionCandidate) => true,
            _ => false
        };

    private static bool OutcomeMatchesRequirementDisposition(
        ReplacementOutcome outcome,
        RequirementDisposition disposition)
        => (outcome, disposition) switch
        {
            (ReplacementOutcome.Equivalent, RequirementDisposition.Restore) => true,
            (ReplacementOutcome.Equivalent, RequirementDisposition.EvidenceGap) => true,
            (ReplacementOutcome.Equivalent, RequirementDisposition.CandidateDivergence) => true,
            (ReplacementOutcome.Equivalent, RequirementDisposition.ScopeCorrectionCandidate) => true,
            (ReplacementOutcome.OwnershipCorrect, RequirementDisposition.OwnershipCorrectReplacement) => true,
            (ReplacementOutcome.EvidenceRecovered, RequirementDisposition.EvidenceGap) => true,
            (ReplacementOutcome.ApprovedDivergence, RequirementDisposition.CandidateDivergence) => true,
            (ReplacementOutcome.ScopeCorrection, RequirementDisposition.ScopeCorrectionCandidate) => true,
            _ => false
        };

    private static bool RequiresMatchingExpectedKind(
        ReplacementOutcome outcome,
        ExpectedState sourceState)
        => outcome != ReplacementOutcome.EvidenceRecovered
            || sourceState == ExpectedState.KnownRegression;

    private static bool ValidTargetRequirementTransition(
        ReplacementOutcome outcome,
        CompatibilityRequirement source,
        CompatibilityRequirement target)
    {
        var sameRequirement = string.Equals(
            source.RequirementId,
            target.RequirementId,
            StringComparison.Ordinal);
        return outcome switch
        {
            ReplacementOutcome.Equivalent or ReplacementOutcome.EvidenceRecovered => sameRequirement,
            ReplacementOutcome.OwnershipCorrect => !sameRequirement
                && target.Disposition == RequirementDisposition.Restore
                && target.Owner.State == OwnerState.Assigned
                && !string.Equals(source.Owner.Id, target.Owner.Id, StringComparison.Ordinal),
            ReplacementOutcome.ApprovedDivergence => !sameRequirement
                && target.Disposition == RequirementDisposition.Restore
                && target.Owner.State == OwnerState.Assigned,
            ReplacementOutcome.ScopeCorrection => !sameRequirement
                && target.Disposition == RequirementDisposition.Restore
                && target.Owner.State == OwnerState.Assigned
                && (source.Scope != target.Scope
                    || !string.Equals(source.Statement, target.Statement, StringComparison.Ordinal)),
            _ => false
        };
    }

    private static bool MustPreserveSourceDigest(
        CompatibilityReplacement replacement,
        CompatibilityCase? source)
        => replacement.Shape == ReplacementShape.OneToOne
            && (replacement.Outcome is ReplacementOutcome.Equivalent
                    or ReplacementOutcome.OwnershipCorrect
                || replacement.Outcome == ReplacementOutcome.EvidenceRecovered
                    && source?.Expected.State == ExpectedState.KnownRegression);

    private static bool MustChangeSourceDigest(ReplacementOutcome outcome)
        => outcome is ReplacementOutcome.ApprovedDivergence
            or ReplacementOutcome.ScopeCorrection;

    private static bool RequiresOwnerDecision(
        CompatibilityReplacement replacement,
        CompatibilityCase? source)
        => replacement.Shape == ReplacementShape.Partition
            || replacement.Status == ReplacementStatus.Approved
            || replacement.Outcome is ReplacementOutcome.ApprovedDivergence
                or ReplacementOutcome.ScopeCorrection
            || source?.Disposition.Kind == CaseDispositionKind.ScopeCorrectionCandidate
            || replacement.Outcome == ReplacementOutcome.EvidenceRecovered
                && source?.Expected.State == ExpectedState.UnknownLost;

    private static bool SameRequirementSemantics(
        CompatibilityRequirement previous,
        CompatibilityRequirement current)
        => previous.SchemaVersion == current.SchemaVersion
            && string.Equals(previous.RequirementId, current.RequirementId, StringComparison.Ordinal)
            && string.Equals(previous.Title, current.Title, StringComparison.Ordinal)
            && string.Equals(previous.Statement, current.Statement, StringComparison.Ordinal)
            && string.Equals(previous.Slice, current.Slice, StringComparison.Ordinal)
            && previous.Scope == current.Scope
            && SemanticallyEqual(previous.Owner, current.Owner)
            && previous.BaselineStatus == current.BaselineStatus
            && string.Equals(previous.CurrentReason, current.CurrentReason, StringComparison.Ordinal)
            && previous.Disposition == current.Disposition
            && SemanticallyEqual(previous.Target, current.Target)
            && previous.ClosurePolicy == current.ClosurePolicy;

    private static bool SameReplacementDefinition(
        CompatibilityReplacement previous,
        CompatibilityReplacement current)
        => previous.SchemaVersion == current.SchemaVersion
            && string.Equals(previous.ReplacementId, current.ReplacementId, StringComparison.Ordinal)
            && string.Equals(previous.FromCaseId, current.FromCaseId, StringComparison.Ordinal)
            && previous.ToCaseIds.SequenceEqual(current.ToCaseIds, StringComparer.Ordinal)
            && previous.Shape == current.Shape
            && previous.Outcome == current.Outcome
            && previous.Coverage == current.Coverage
            && string.Equals(previous.Rationale, current.Rationale, StringComparison.Ordinal);

    private static int ReplacementStatusRank(ReplacementStatus status) => status switch
    {
        ReplacementStatus.Candidate => 0,
        ReplacementStatus.Approved => 1,
        ReplacementStatus.Verified => 2,
        _ => throw new InvalidOperationException($"Unknown replacement status '{status}'.")
    };

    private static bool SemanticallyEqual<T>(T left, T right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, CompatibilityDocumentReader.StrictJsonOptions),
            JsonSerializer.SerializeToElement(right, CompatibilityDocumentReader.StrictJsonOptions));

    private static string ProjectKey(string path)
        => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

    private static bool IsCommit(string? value)
        => value is { Length: 40 } && value.All(IsLowerHex);

    private static bool IsDigest(string? value)
        => value is { Length: 71 }
            && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value[7..].All(IsLowerHex);

    private static bool IsRawSha256(string? value)
        => value is { Length: 64 } && value.All(IsLowerHex);

    private static bool IsLowerHex(char value)
        => value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static bool IsRelativePath(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && !Path.IsPathRooted(value)
            && !value.Contains('\\')
            && value.Split('/').All(static segment => segment is not "" and not "." and not "..");

    private static bool IsGate(string? value)
        => value is { Length: 3 or 4 }
            && value[0] == 'G'
            && char.IsAsciiDigit(value[1])
            && char.IsAsciiDigit(value[2])
            && (value.Length == 3 || value[3] is >= 'A' and <= 'Z');

    private static bool IsStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
        {
            return false;
        }

        var previousSeparator = true;
        foreach (var character in value)
        {
            var separator = character is '.' or '-';
            if (separator && previousSeparator)
            {
                return false;
            }

            if (!separator && !(character is >= 'a' and <= 'z') && !char.IsAsciiDigit(character))
            {
                return false;
            }

            previousSeparator = separator;
        }

        return !previousSeparator;
    }

    private static void ValidateId(string? value, string code, ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (!IsStableId(value))
        {
            Add(diagnostics, code, $"Invalid stable identifier '{value}'.");
        }
    }

    private static void ValidateGate(string? value, string code, ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (!IsGate(value))
        {
            Add(diagnostics, code, $"Invalid roadmap gate '{value}'.");
        }
    }

    private static void RequireText(string? value, string code, string owner, ICollection<CompatibilityDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(diagnostics, code, $"'{owner}' contains an empty required value.");
        }
    }

    private static void Add(ICollection<CompatibilityDiagnostic> diagnostics, string code, string message)
        => diagnostics.Add(new CompatibilityDiagnostic(code, message));

    private readonly record struct ExecutionKey(string Project, string FullNameDigest)
    {
        public override string ToString() => $"{Project}/{FullNameDigest}";
    }

    private sealed record ExecutionState(
        IReadOnlyDictionary<string, NUnitTestCaseResult> ExecutionsByCase,
        IReadOnlyDictionary<string, bool> WitnessValid,
        int PassingWitnesses,
        int ClosureEligibleWitnesses);

    private sealed record ReplacementClosureCandidate(
        CompatibilityReplacement Replacement,
        IReadOnlySet<string> DirectWitnesses);
}

public sealed record CompatibilityDiagnostic(string Code, string Message);

public sealed record CompatibilityValidationReport(
    IReadOnlyList<CompatibilityDiagnostic> Diagnostics,
    NUnitCounts Counts,
    int RegisteredCases,
    int RegisteredReplacements,
    int PassingWitnesses,
    int ClosureEligibleWitnesses)
{
    public bool IsValid => Diagnostics.Count == 0;
}
