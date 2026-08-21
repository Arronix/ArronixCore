namespace Arronix.Compatibility.Ratchet.Tests;

internal static class CompatibilityFixture
{
    internal const string Project = "src/Tests/Tests.csproj";
    internal const string Assembly = "Tests.dll";
    internal const string Fixture = "Tests.Fixture";
    internal const string SourceFile = "src/Tests/Fixture.cs";

    internal static string SourceDigest { get; } = CompatibilityDigest.Sha256("fixture-source");

    internal static FixtureState Create()
    {
        var sources = Enumerable.Range(0, 12).Select(CreateSource).ToArray();
        var requirements = Enumerable.Range(0, 129).Select(CreateRequirement).ToArray();
        var cases = Enumerable.Range(0, 302).Select(CreateCase).ToArray();
        var tests = cases.Select(CreateSkippedExecution).ToArray();
        var ledger = new CompatibilityLedger(
            CreateBaseline(),
            sources,
            requirements,
            cases,
            []);
        var run = new NUnitTestRun([new NUnitProjectResult(Assembly, "in-memory", tests)]);
        var snapshot = new RepositorySnapshot(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SourceFile] = SourceDigest
            },
            sources.ToDictionary(static value => value.SourceId, static _ => true, StringComparer.Ordinal),
            cases.ToDictionary(
                static value => value.CaseId,
                static _ => CompiledSuccess,
                StringComparer.Ordinal));
        return new FixtureState(ledger, run, snapshot);
    }

    internal static FixtureState Execute(
        FixtureState state,
        int caseIndex,
        NUnitTestOutcome outcome,
        bool ratchetSkipCount = true)
    {
        var compatibilityCase = state.Ledger.Cases[caseIndex];
        var tests = state.Run.Tests.Select(test =>
        {
            var digest = CompatibilityDigest.Sha256(test.FullName);
            if (!string.Equals(digest, compatibilityCase.Binding.FullNameDigest, StringComparison.Ordinal))
            {
                return test;
            }

            return test with { Outcome = outcome };
        }).ToArray();

        var run = new NUnitTestRun([new NUnitProjectResult(Assembly, "in-memory", tests)]);
        return state with
        {
            Ledger = ratchetSkipCount
                ? state.Ledger with
                {
                    Baseline = state.Ledger.Baseline with { CurrentSkipCount = run.Counts.Skipped }
                }
                : state.Ledger,
            Run = run
        };
    }

    internal static FixtureState RemoveExecution(FixtureState state, int caseIndex)
        => RemoveExecutionByCaseId(state, state.Ledger.Cases[caseIndex].CaseId);

    internal static FixtureState RemoveExecutionByCaseId(FixtureState state, string caseId)
    {
        var compatibilityCase = state.Ledger.Cases.Single(value =>
            string.Equals(value.CaseId, caseId, StringComparison.Ordinal));
        var tests = state.Run.Tests
            .Where(test => !string.Equals(
                CompatibilityDigest.Sha256(test.FullName),
                compatibilityCase.Binding.FullNameDigest,
                StringComparison.Ordinal))
            .ToArray();
        var run = new NUnitTestRun([new NUnitProjectResult(Assembly, "in-memory", tests)]);
        return state with
        {
            Ledger = state.Ledger with
            {
                Baseline = state.Ledger.Baseline with { CurrentSkipCount = run.Counts.Skipped }
            },
            Run = run
        };
    }

    internal static CompatibilityReplacement Replacement(
        int from,
        params int[] targets)
        => new()
        {
            SchemaVersion = 1,
            ReplacementId = $"replacement.r{from:D3}",
            FromCaseId = $"case.c{from:D3}",
            ToCaseIds = targets.Select(static value => $"case.c{value:D3}").ToArray(),
            Shape = ReplacementShape.OneToOne,
            Outcome = ReplacementOutcome.Equivalent,
            Coverage = ReplacementCoverage.Full,
            Status = ReplacementStatus.Verified,
            Rationale = "The target cases form an independently proved replacement."
        };

    internal static FixtureState AddReplacementCandidate(
        FixtureState state,
        int from,
        int target)
        => AddReplacementCandidate(
            state,
            from,
            ReplacementShape.OneToOne,
            ReplacementOutcome.OwnershipCorrect,
            target);

    internal static FixtureState AddReplacementCandidate(
        FixtureState state,
        int from,
        ReplacementShape shape,
        ReplacementOutcome outcome,
        params int[] targets)
    {
        if (targets.Length == 0)
        {
            throw new ArgumentException("At least one replacement target is required.", nameof(targets));
        }

        var sourceCaseId = $"case.c{from:D3}";
        var cases = state.Ledger.Cases.ToArray();
        var sourceCaseIndex = Array.FindIndex(
            cases,
            value => string.Equals(value.CaseId, sourceCaseId, StringComparison.Ordinal));
        var sourceCase = cases[sourceCaseIndex];
        var (caseDisposition, requirementDisposition, baselineStatus, closurePolicy) = outcome switch
        {
            ReplacementOutcome.Equivalent => (
                CaseDispositionKind.Restore,
                RequirementDisposition.Restore,
                BaselineStatus.Skipped,
                ClosurePolicy.AllCases),
            ReplacementOutcome.OwnershipCorrect => (
                CaseDispositionKind.OwnershipCorrectReplacement,
                RequirementDisposition.OwnershipCorrectReplacement,
                BaselineStatus.Skipped,
                ClosurePolicy.AllCases),
            ReplacementOutcome.EvidenceRecovered => (
                CaseDispositionKind.EvidenceGap,
                RequirementDisposition.EvidenceGap,
                BaselineStatus.MissingProof,
                ClosurePolicy.AllCases),
            ReplacementOutcome.ApprovedDivergence => (
                CaseDispositionKind.CandidateDivergence,
                RequirementDisposition.CandidateDivergence,
                BaselineStatus.MissingProof,
                ClosurePolicy.AllCases),
            ReplacementOutcome.ScopeCorrection => (
                CaseDispositionKind.ScopeCorrectionCandidate,
                RequirementDisposition.ScopeCorrectionCandidate,
                BaselineStatus.RecordedException,
                ClosurePolicy.OwnerDecision),
            _ => throw new InvalidOperationException($"Unknown replacement outcome '{outcome}'.")
        };
        for (var index = 0; index < cases.Length; index++)
        {
            if (string.Equals(
                    cases[index].RequirementId,
                    sourceCase.RequirementId,
                    StringComparison.Ordinal))
            {
                cases[index] = cases[index] with
                {
                    Disposition = cases[index].Disposition with { Kind = caseDisposition }
                };
            }
        }

        sourceCase = cases[sourceCaseIndex] with
        {
            Expected = outcome == ReplacementOutcome.EvidenceRecovered
                ? new CaseExpectation
                {
                    State = ExpectedState.UnknownLost,
                    GapReason = "The synthetic source expectation was intentionally lost."
                }
                : sourceCase.Expected
        };
        cases[sourceCaseIndex] = sourceCase;

        var targetSources = targets.Select((target, offset) =>
            CreateSource(state.Ledger.Sources.Count + offset) with
            {
                SourceId = $"source.target.s{target:D3}",
                Title = $"Independent target source {target}",
                CaseCount = 1
            }).ToArray();
        var requiresDecision = shape == ReplacementShape.Partition
            || outcome is ReplacementOutcome.ApprovedDivergence
            or ReplacementOutcome.ScopeCorrection
            || outcome == ReplacementOutcome.EvidenceRecovered
                && sourceCase.Expected.State == ExpectedState.UnknownLost;
        var decisionSource = requiresDecision ? CreateDecisionSource(from) : null;

        var sourceRequirementIndex = Array.FindIndex(
            state.Ledger.Requirements.ToArray(),
            value => string.Equals(
                value.RequirementId,
                sourceCase.RequirementId,
                StringComparison.Ordinal));
        var requirements = state.Ledger.Requirements.ToArray();
        var sourceRequirement = requirements[sourceRequirementIndex];
        var targetUsesSourceRequirement = outcome is ReplacementOutcome.Equivalent
            or ReplacementOutcome.EvidenceRecovered;
        var sourceIds = sourceRequirement.SourceIds.AsEnumerable();
        if (targetUsesSourceRequirement)
        {
            sourceIds = sourceIds.Concat(targetSources.Select(static value => value.SourceId));
        }

        if (decisionSource is not null)
        {
            sourceIds = sourceIds.Append(decisionSource.SourceId);
        }

        requirements[sourceRequirementIndex] = sourceRequirement with
        {
            SourceIds = sourceIds.ToArray(),
            BaselineStatus = baselineStatus,
            Disposition = requirementDisposition,
            ClosurePolicy = closurePolicy,
            CaseCount = sourceRequirement.CaseCount + (targetUsesSourceRequirement ? targets.Length : 0)
        };

        var targetRequirement = targetUsesSourceRequirement
            ? null
            : CreateReplacementRequirement(from, outcome, targetSources);
        if (targetRequirement is not null)
        {
            requirements = requirements.Append(targetRequirement).ToArray();
        }

        var targetRequirementId = targetRequirement?.RequirementId ?? sourceRequirement.RequirementId;
        var targetCases = targets.Select((target, offset) =>
        {
            var preserveDigest = shape == ReplacementShape.OneToOne
                && outcome is ReplacementOutcome.Equivalent
                    or ReplacementOutcome.OwnershipCorrect;
            var semanticDigest = preserveDigest
                ? sourceCase.Expected.SemanticDigest!
                : CompatibilityDigest.Sha256($"replacement-semantic-{target}");
            return CreateReplacementTarget(
                target,
                targetSources[offset].SourceId,
                targetRequirementId,
                sourceCase.Expected.Kind ?? ExpectedKind.Invariant,
                semanticDigest);
        }).ToArray();
        var replacement = new CompatibilityReplacement
        {
            SchemaVersion = 1,
            ReplacementId = $"replacement.r{from:D3}",
            FromCaseId = sourceCaseId,
            ToCaseIds = targetCases.Select(static value => value.CaseId).ToArray(),
            Shape = shape,
            Outcome = outcome,
            Coverage = ReplacementCoverage.Full,
            Status = ReplacementStatus.Candidate,
            Rationale = "The independently sourced executable target covers the retired case.",
            DecisionReference = decisionSource?.SourceId
        };
        var run = state.Run with
        {
            Projects =
            [
                new NUnitProjectResult(
                    Assembly,
                    "in-memory",
                    state.Run.Tests.Concat(targetCases.Select(CreatePassedExecution)).ToArray())
            ]
        };
        var sourceIdentities = new Dictionary<string, bool>(
            state.Snapshot.SourceIdentityMatches ?? new Dictionary<string, bool>(),
            StringComparer.Ordinal);
        foreach (var source in targetSources)
        {
            sourceIdentities[source.SourceId] = true;
        }

        if (decisionSource is not null)
        {
            sourceIdentities[decisionSource.SourceId] = true;
        }

        var sources = state.Ledger.Sources
            .Concat(targetSources)
            .Concat(decisionSource is null ? [] : [decisionSource])
            .OrderBy(static value => value.SourceId)
            .ToArray();
        return state with
        {
            Ledger = state.Ledger with
            {
                Sources = sources,
                Requirements = requirements,
                Cases = cases.Concat(targetCases).OrderBy(static value => value.CaseId).ToArray(),
                Replacements = [replacement]
            },
            Run = run,
            Snapshot = state.Snapshot with
            {
                SourceIdentityMatches = sourceIdentities,
                CompiledSourceVerifications = AddCompiledVerifications(state.Snapshot, targetCases)
            }
        };
    }

    internal static FixtureState AddEquivalentReplacementCandidateFromWitness(
        FixtureState state,
        int fromTarget,
        int target)
    {
        var sourceCaseId = $"case.target.c{fromTarget:D3}";
        var sourceCase = state.Ledger.Cases.Single(value =>
            string.Equals(value.CaseId, sourceCaseId, StringComparison.Ordinal));
        if (sourceCase.Introduced?.Role != IntroducedCaseRole.ReplacementWitness
            || sourceCase.Disposition.Kind != CaseDispositionKind.Proven
            || sourceCase.Expected.State != ExpectedState.KnownRegression
            || sourceCase.Expected.Kind is null
            || sourceCase.Expected.SemanticDigest is null)
        {
            throw new ArgumentException($"'{sourceCaseId}' is not a proven replacement witness.", nameof(fromTarget));
        }

        var targetSource = CreateSource(state.Ledger.Sources.Count) with
        {
            SourceId = $"source.target.s{target:D3}",
            Title = $"Independent target source {target}",
            CaseCount = 1
        };
        var requirements = state.Ledger.Requirements.ToArray();
        var requirementIndex = Array.FindIndex(
            requirements,
            value => string.Equals(value.RequirementId, sourceCase.RequirementId, StringComparison.Ordinal));
        var sourceRequirement = requirements[requirementIndex];
        requirements[requirementIndex] = sourceRequirement with
        {
            SourceIds = sourceRequirement.SourceIds.Append(targetSource.SourceId).ToArray(),
            CaseCount = sourceRequirement.CaseCount + 1
        };

        var targetCase = CreateReplacementTarget(
            target,
            targetSource.SourceId,
            sourceCase.RequirementId,
            sourceCase.Expected.Kind.Value,
            sourceCase.Expected.SemanticDigest);
        var replacement = new CompatibilityReplacement
        {
            SchemaVersion = 1,
            ReplacementId = $"replacement.target.r{fromTarget:D3}",
            FromCaseId = sourceCaseId,
            ToCaseIds = [targetCase.CaseId],
            Shape = ReplacementShape.OneToOne,
            Outcome = ReplacementOutcome.Equivalent,
            Coverage = ReplacementCoverage.Full,
            Status = ReplacementStatus.Candidate,
            Rationale = "The successor preserves the retired witness's locked semantics."
        };
        var identities = new Dictionary<string, bool>(
            state.Snapshot.SourceIdentityMatches ?? new Dictionary<string, bool>(),
            StringComparer.Ordinal)
        {
            [targetSource.SourceId] = true
        };
        return state with
        {
            Ledger = state.Ledger with
            {
                Sources = state.Ledger.Sources.Append(targetSource).OrderBy(static value => value.SourceId).ToArray(),
                Requirements = requirements,
                Cases = state.Ledger.Cases.Append(targetCase).OrderBy(static value => value.CaseId).ToArray(),
                Replacements = state.Ledger.Replacements.Append(replacement).ToArray()
            },
            Run = state.Run with
            {
                Projects =
                [
                    new NUnitProjectResult(
                        Assembly,
                        "in-memory",
                        state.Run.Tests.Append(CreatePassedExecution(targetCase)).ToArray())
                ]
            },
            Snapshot = state.Snapshot with
            {
                SourceIdentityMatches = identities,
                CompiledSourceVerifications = AddCompiledVerifications(state.Snapshot, [targetCase])
            }
        };
    }

    internal static FixtureState AddEquivalentScopeResolutionCandidate(
        FixtureState state,
        int from,
        int target)
    {
        state = AddReplacementCandidate(
            state,
            from,
            ReplacementShape.OneToOne,
            ReplacementOutcome.Equivalent,
            target);
        var sourceCaseId = $"case.c{from:D3}";
        var sourceCase = state.Ledger.Cases.Single(value =>
            string.Equals(value.CaseId, sourceCaseId, StringComparison.Ordinal));
        var decision = CreateDecisionSource(from);
        var cases = state.Ledger.Cases.Select(value =>
            string.Equals(value.RequirementId, sourceCase.RequirementId, StringComparison.Ordinal)
                && value.Introduced is null
                ? value with
                {
                    Disposition = value.Disposition with
                    {
                        Kind = CaseDispositionKind.ScopeCorrectionCandidate
                    }
                }
                : value).ToArray();
        var requirements = state.Ledger.Requirements.Select(value =>
            string.Equals(value.RequirementId, sourceCase.RequirementId, StringComparison.Ordinal)
                ? value with
                {
                    SourceIds = value.SourceIds.Append(decision.SourceId).ToArray(),
                    BaselineStatus = BaselineStatus.RecordedException,
                    Disposition = RequirementDisposition.ScopeCorrectionCandidate,
                    ClosurePolicy = ClosurePolicy.OwnerDecision
                }
                : value).ToArray();
        var replacements = state.Ledger.Replacements.Select(value =>
            string.Equals(value.FromCaseId, sourceCaseId, StringComparison.Ordinal)
                ? value with { DecisionReference = decision.SourceId }
                : value).ToArray();
        var identities = new Dictionary<string, bool>(
            state.Snapshot.SourceIdentityMatches ?? new Dictionary<string, bool>(),
            StringComparer.Ordinal)
        {
            [decision.SourceId] = true
        };
        return state with
        {
            Ledger = state.Ledger with
            {
                Sources = state.Ledger.Sources.Append(decision).OrderBy(static value => value.SourceId).ToArray(),
                Requirements = requirements,
                Cases = cases,
                Replacements = replacements
            },
            Snapshot = state.Snapshot with { SourceIdentityMatches = identities }
        };
    }

    internal static FixtureState VerifyReplacement(FixtureState state, int from)
    {
        var replacements = state.Ledger.Replacements.ToArray();
        var index = Array.FindIndex(
            replacements,
            value => string.Equals(value.FromCaseId, $"case.c{from:D3}", StringComparison.Ordinal));
        replacements[index] = replacements[index] with { Status = ReplacementStatus.Verified };
        return state with { Ledger = state.Ledger with { Replacements = replacements } };
    }

    internal static FixtureState VerifyAllReplacements(FixtureState state)
        => state with
        {
            Ledger = state.Ledger with
            {
                Replacements = state.Ledger.Replacements
                    .Select(static value => value with { Status = ReplacementStatus.Verified })
                    .ToArray()
            }
        };

    private static CompatibilityBaselineDocument CreateBaseline()
        => new()
        {
            Schema = "schema/baseline.schema.json",
            SchemaVersion = 1,
            BaselineId = "baseline.r00",
            RepositoryBaselineCommit = new string('a', 40),
            CaptureHeadCommit = new string('b', 40),
            Runs =
            [
                new BaselineRun
                {
                    RunId = "run.primary",
                    Project = Project,
                    Format = "nunit3",
                    ArtifactDigest = CompatibilityDigest.Sha256("primary-result"),
                    CapturedAtUtc = DateTimeOffset.Parse("2026-08-21T00:00:00Z"),
                    Total = 841,
                    Passed = 539,
                    Failed = 0,
                    Inconclusive = 0,
                    Skipped = 302
                },
                new BaselineRun
                {
                    RunId = "run.empty",
                    Project = "src/Empty/Empty.csproj",
                    Format = "nunit3",
                    ArtifactDigest = CompatibilityDigest.Sha256("empty-result"),
                    CapturedAtUtc = DateTimeOffset.Parse("2026-08-21T00:00:00Z"),
                    Total = 0,
                    Passed = 0,
                    Failed = 0,
                    Inconclusive = 0,
                    Skipped = 0
                }
            ],
            Totals = new BaselineTotals
            {
                CapturedCases = 841,
                Passed = 539,
                Failed = 0,
                Inconclusive = 0,
                Skipped = 302
            },
            CurrentSkipCount = 302,
            InitialRecordCounts = new InitialRecordCounts
            {
                Sources = 12,
                Requirements = 129,
                Cases = 302,
                Replacements = 0
            },
            ReasonCounts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["baseline-skip"] = 302
            },
            FixtureCounts = [new FixtureSkipCount { Fixture = Fixture, Skipped = 302 }],
            ZeroCaseRequirementIds = Enumerable.Range(1, 5).Select(static value => $"req.inventory.r{value:D3}").ToArray(),
            Notes = ["Synthetic direct-test baseline."]
        };

    private static CompatibilitySource CreateSource(int index)
        => new()
        {
            SchemaVersion = 1,
            SourceId = $"source.s{index:D3}",
            Title = $"Source {index}",
            EvidenceClass = EvidenceClass.GeneratedCleanRoom,
            ArtifactState = ArtifactState.Current,
            Locator = $"evidence/s{index:D3}.json",
            Revision = new SourceRevision { Kind = RevisionKind.ArtifactSha256, Value = CompatibilityDigest.Sha256($"source-{index}")[7..] },
            CaseCount = index == 0 ? 302 : 0,
            Provenance = new SourceProvenance
            {
                Independence = Independence.Independent,
                Access = EvidenceAccess.Normal,
                Currency = EvidenceCurrency.Current,
                PinState = PinState.ArtifactPinned
            },
            ProofUse = ProofUse.Eligible,
            Restrictions = ["Synthetic unit-test evidence only."]
        };

    private static CompatibilitySource CreateDecisionSource(int from)
        => new()
        {
            SchemaVersion = 1,
            SourceId = $"decision.owner.r{from:D3}",
            Title = $"Owner decision for replacement {from}",
            EvidenceClass = EvidenceClass.ArronixOwnerDecision,
            ArtifactState = ArtifactState.Current,
            Locator = $"evidence/decision-r{from:D3}.json",
            Revision = new SourceRevision
            {
                Kind = RevisionKind.ArtifactSha256,
                Value = CompatibilityDigest.Sha256($"decision-{from}")[7..]
            },
            CaseCount = 0,
            Provenance = new SourceProvenance
            {
                Independence = Independence.NotApplicable,
                Access = EvidenceAccess.Normal,
                Currency = EvidenceCurrency.Current,
                PinState = PinState.ArtifactPinned
            },
            ProofUse = ProofUse.Eligible,
            Restrictions = ["Synthetic unit-test owner decision only."]
        };

    private static CompatibilityRequirement CreateRequirement(int index)
        => new()
        {
            SchemaVersion = 1,
            RequirementId = index == 0 ? "req.primary" : $"req.inventory.r{index:D3}",
            Title = index == 0 ? "Primary behavior" : $"Inventory {index}",
            Statement = index == 0 ? "Every registered primary case remains visible." : "The inventory remains explicit.",
            Slice = index == 0 ? "test.primary" : "test.inventory",
            Scope = index == 0 ? RequirementScope.InScope : RequirementScope.InventoryControl,
            Owner = new RequirementOwner { Id = "owner.tests", State = OwnerState.Assigned },
            SourceIds = ["source.s000"],
            BaselineStatus = index == 0 ? BaselineStatus.Skipped : BaselineStatus.InventoryZero,
            CurrentReason = index == 0 ? "Baseline omission." : "No registered cases.",
            Disposition = index == 0 ? RequirementDisposition.Restore : RequirementDisposition.InventoryZero,
            Target = new RequirementTarget
            {
                ClassificationGate = "G01",
                ClosureGate = "G02",
                Prerequisites = []
            },
            ClosurePolicy = index == 0 ? ClosurePolicy.AllCases : ClosurePolicy.InventoryZero,
            CaseCount = index == 0 ? 302 : 0
        };

    private static CompatibilityRequirement CreateReplacementRequirement(
        int from,
        ReplacementOutcome outcome,
        IReadOnlyList<CompatibilitySource> sources)
        => new()
        {
            SchemaVersion = 1,
            RequirementId = $"req.target.r{from:D3}",
            Title = $"Replacement behavior for case {from}",
            Statement = outcome == ReplacementOutcome.ScopeCorrection
                ? "The corrected scope remains explicitly governed."
                : "The replacement behavior remains permanently executable.",
            Slice = "test.target",
            Scope = outcome == ReplacementOutcome.ScopeCorrection
                ? RequirementScope.InventoryControl
                : RequirementScope.InScope,
            Owner = new RequirementOwner
            {
                Id = outcome switch
                {
                    ReplacementOutcome.OwnershipCorrect => "owner.correct",
                    ReplacementOutcome.ApprovedDivergence => "owner.arronix",
                    _ => "owner.tests"
                },
                State = OwnerState.Assigned
            },
            SourceIds = sources.Select(static value => value.SourceId).ToArray(),
            BaselineStatus = BaselineStatus.MissingProof,
            CurrentReason = "Introduced as a permanent replacement witness.",
            Disposition = RequirementDisposition.Restore,
            Target = new RequirementTarget
            {
                ClassificationGate = "G02",
                ClosureGate = "G03",
                Prerequisites = []
            },
            ClosurePolicy = ClosurePolicy.AllCases,
            CaseCount = sources.Count
        };

    private static CompatibilityCase CreateCase(int index)
    {
        var fullName = $"{Fixture}.Case{index:D3}";
        return new CompatibilityCase
        {
            SchemaVersion = 1,
            CaseId = $"case.c{index:D3}",
            RequirementId = "req.primary",
            SourceId = "source.s000",
            Binding = new CaseBinding
            {
                Framework = "nunit",
                Project = Project,
                Fixture = Fixture,
                Method = $"Case{index:D3}",
                FullNameDigest = CompatibilityDigest.Sha256(fullName),
                SourceFile = SourceFile,
                SourceFileDigest = SourceDigest
            },
            Expected = new CaseExpectation
            {
                State = ExpectedState.KnownRegression,
                Kind = ExpectedKind.Invariant,
                SemanticDigest = CompatibilityDigest.Sha256($"semantic-{index}")
            },
            Baseline = new BaselineCaseObservation
            {
                RunId = "run.primary",
                Outcome = "skipped",
                ReasonCode = "baseline-skip",
                ReasonDigest = CompatibilityDigest.Sha256("baseline reason")
            },
            Disposition = new CaseDisposition
            {
                Kind = CaseDispositionKind.Restore,
                ClassificationGate = "G01",
                ClosureGate = "G02"
            }
        };
    }

    private static CompatibilityCase CreateReplacementTarget(
        int index,
        string sourceId,
        string requirementId,
        ExpectedKind expectedKind,
        string semanticDigest)
    {
        var method = $"Replacement{index:D3}";
        return new CompatibilityCase
        {
            SchemaVersion = 1,
            CaseId = $"case.target.c{index:D3}",
            RequirementId = requirementId,
            SourceId = sourceId,
            Binding = new CaseBinding
            {
                Framework = "nunit",
                Project = Project,
                Fixture = Fixture,
                Method = method,
                FullNameDigest = CompatibilityDigest.Sha256($"{Fixture}.{method}"),
                SourceFile = SourceFile,
                SourceFileDigest = SourceDigest
            },
            Expected = new CaseExpectation
            {
                State = ExpectedState.KnownRegression,
                Kind = expectedKind,
                SemanticDigest = semanticDigest
            },
            Introduced = new IntroducedCase
            {
                RegisteredAtGate = "G02",
                ExpectedResult = "passed",
                Role = IntroducedCaseRole.ReplacementWitness
            },
            Disposition = new CaseDisposition
            {
                Kind = CaseDispositionKind.Proven,
                ClassificationGate = "G02",
                ClosureGate = "G03"
            }
        };
    }

    private static NUnitTestCaseResult CreateSkippedExecution(CompatibilityCase compatibilityCase)
        => new(
            Assembly,
            compatibilityCase.Binding.Method,
            $"{Fixture}.{compatibilityCase.Binding.Method}",
            NUnitTestOutcome.Skipped);

    private static IReadOnlyDictionary<string, CompiledTestSourceVerification> AddCompiledVerifications(
        RepositorySnapshot snapshot,
        IEnumerable<CompatibilityCase> cases)
    {
        var values = new Dictionary<string, CompiledTestSourceVerification>(
            snapshot.CompiledSourceVerifications
                ?? new Dictionary<string, CompiledTestSourceVerification>(),
            StringComparer.Ordinal);
        foreach (var compatibilityCase in cases)
        {
            values[compatibilityCase.CaseId] = CompiledSuccess;
        }

        return values;
    }

    private static CompiledTestSourceVerification CompiledSuccess { get; } = new(
        true,
        "compiled-source.verified",
        "Synthetic fixture compiled-source proof.");

    private static NUnitTestCaseResult CreatePassedExecution(CompatibilityCase compatibilityCase)
        => new(
            Assembly,
            compatibilityCase.Binding.Method,
            $"{Fixture}.{compatibilityCase.Binding.Method}",
            NUnitTestOutcome.Passed);
}

internal sealed record FixtureState(
    CompatibilityLedger Ledger,
    NUnitTestRun Run,
    RepositorySnapshot Snapshot);
