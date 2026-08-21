using NUnitAssert = global::NUnit.Framework.Assert;
using NUnitDoes = global::NUnit.Framework.Does;
using NUnitIs = global::NUnit.Framework.Is;
using NUnitTestAttribute = global::NUnit.Framework.TestAttribute;
using NUnitTestFixtureAttribute = global::NUnit.Framework.TestFixtureAttribute;
using PinnedCompatibilityValidator = global::Arronix.Compatibility.Ratchet.CompatibilityValidator;

namespace Arronix.Compatibility.Ratchet.Tests;

[NUnitTestFixtureAttribute]
public class CompatibilityValidatorTests
{
    [NUnitTestAttribute]
    public void AcceptsTheExactRegisteredSkipBaseline()
    {
        var state = CompatibilityFixture.Create();
        var report = Validate(state);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void RejectsPermanentCaseRemoval()
    {
        var previous = CompatibilityFixture.Create();
        var current = previous with
        {
            Ledger = previous.Ledger with { Cases = previous.Ledger.Cases.Skip(1).ToArray() }
        };

        var report = Validate(current, previous.Ledger);
        AssertCode(report, "history.case-removed");
    }

    [NUnitTestAttribute]
    public void RejectsAStableCaseRename()
    {
        var previous = CompatibilityFixture.Create();
        var cases = previous.Ledger.Cases.ToArray();
        cases[0] = cases[0] with { CaseId = "case.renamed" };
        var current = previous with { Ledger = previous.Ledger with { Cases = cases } };

        var report = Validate(current, previous.Ledger);
        AssertCode(report, "history.case-removed");
    }

    [NUnitTestAttribute]
    public void RejectsANewUnregisteredSkipEvenWhenTheTotalDoesNotIncrease()
    {
        var state = CompatibilityFixture.Execute(CompatibilityFixture.Create(), 0, NUnitTestOutcome.Passed);
        var tests = state.Run.Tests.Append(new NUnitTestCaseResult(
            CompatibilityFixture.Assembly,
            "NewSkip",
            "Tests.Fixture.NewSkip",
            NUnitTestOutcome.Skipped)).ToArray();
        state = state with
        {
            Run = new NUnitTestRun([new NUnitProjectResult(CompatibilityFixture.Assembly, "memory", tests)])
        };

        var report = Validate(state);
        AssertCode(report, "execution.unregistered-skip");
    }

    [NUnitTestAttribute]
    public void RejectsACeilingIncrease()
    {
        var state = CompatibilityFixture.Create();
        var totals = state.Ledger.Baseline.Totals with { CapturedCases = 303, Skipped = 303 };
        state = state with
        {
            Ledger = state.Ledger with { Baseline = state.Ledger.Baseline with { Totals = totals } }
        };

        var report = Validate(state);
        AssertCode(report, "baseline.skip-ceiling-changed");
    }

    [NUnitTestAttribute]
    public void RequiresAnObservedSkipReductionToBeCommittedImmediately()
    {
        var state = CompatibilityFixture.Execute(
            CompatibilityFixture.Create(),
            0,
            NUnitTestOutcome.Passed,
            ratchetSkipCount: false);

        var report = Validate(state);
        AssertCode(report, "execution.skip-count-not-ratcheted");
    }

    [NUnitTestAttribute]
    public void AcceptsAndLocksAMonotonicSkipReduction()
    {
        var previous = CompatibilityFixture.Create();
        var current = CompatibilityFixture.Execute(previous, 0, NUnitTestOutcome.Passed);
        current = current with
        {
            Ledger = current.Ledger with
            {
                Baseline = current.Ledger.Baseline with { CurrentSkipCount = 301 }
            }
        };

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void RejectsARegressionToAPreviouslyEliminatedSkip()
    {
        var previous = CompatibilityFixture.Create();
        previous = previous with
        {
            Ledger = previous.Ledger with
            {
                Baseline = previous.Ledger.Baseline with { CurrentSkipCount = 301 }
            }
        };
        var current = CompatibilityFixture.Create();

        var report = Validate(current, previous.Ledger);
        AssertCode(report, "history.ceiling-increased");
    }

    [NUnitTestAttribute]
    public void RejectsAnyChangeToAnExistingEvidenceSource()
    {
        var previous = CompatibilityFixture.Create();
        var sources = previous.Ledger.Sources.ToArray();
        sources[0] = sources[0] with { ProofUse = ProofUse.Ineligible };
        var current = previous with { Ledger = previous.Ledger with { Sources = sources } };

        var report = Validate(current, previous.Ledger);
        AssertCode(report, "history.source-changed");
    }

    [NUnitTestAttribute]
    public void RejectsARewrittenRequirementDefinition()
    {
        var previous = CompatibilityFixture.Create();
        var requirements = previous.Ledger.Requirements.ToArray();
        requirements[0] = requirements[0] with
        {
            Title = "Weaker title",
            Statement = "A weaker assertion.",
            Owner = new RequirementOwner { Id = "owner.other", State = OwnerState.Provisional },
            Target = requirements[0].Target with { ClosureGate = "G99" }
        };
        var current = previous with { Ledger = previous.Ledger with { Requirements = requirements } };

        var report = Validate(current, previous.Ledger);
        AssertCode(report, "history.requirement-changed");
    }

    [NUnitTestAttribute]
    public void RejectsCoordinatedCaseAndSourceWeakeningEvenWhenTheCurrentSnapshotMatches()
    {
        RequireAssembly(typeof(NUnitAssert), "nunit.framework");
        RequireAssembly(typeof(PinnedCompatibilityValidator), "Arronix.Compatibility.Ratchet");

        var previous = CompatibilityFixture.Create();
        var weakenedDigest = CompatibilityDigest.Sha256("coordinated weaker test body");
        var cases = previous.Ledger.Cases.ToArray();
        cases[0] = cases[0] with
        {
            Binding = cases[0].Binding with { SourceFileDigest = weakenedDigest },
            Expected = cases[0].Expected with
            {
                SemanticDigest = CompatibilityDigest.Sha256("coordinated weaker assertion")
            }
        };
        var current = previous with
        {
            Ledger = previous.Ledger with { Cases = cases },
            Snapshot = previous.Snapshot with
            {
                FileDigests = new Dictionary<string, string>
                {
                    [CompatibilityFixture.SourceFile] = weakenedDigest
                }
            }
        };
        current = CompatibilityFixture.Execute(current, 0, NUnitTestOutcome.Passed);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "history.case-changed");
            AssertCode(report, "witness.case-not-locked");
        });
    }

    private static void RequireAssembly(global::System.Type type, string expectedName)
    {
        if (!string.Equals(type.Assembly.GetName().Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected '{type.FullName}' from '{expectedName}', but resolved it from '{type.Assembly.FullName}'.");
        }
    }

    [NUnitTestAttribute]
    public void RejectsAnIncompleteReplacement()
    {
        var state = CompatibilityFixture.RemoveExecution(CompatibilityFixture.Create(), 0);
        var replacement = CompatibilityFixture.Replacement(0, 1) with
        {
            Coverage = ReplacementCoverage.Partial,
            Status = ReplacementStatus.Verified
        };
        state = state with { Ledger = state.Ledger with { Replacements = [replacement] } };

        var report = Validate(state);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.partial-not-candidate");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void RejectsAReplacementCycle()
    {
        var state = CompatibilityFixture.Create();
        state = state with
        {
            Ledger = state.Ledger with
            {
                Replacements =
                [
                    CompatibilityFixture.Replacement(0, 1),
                    CompatibilityFixture.Replacement(1, 0)
                ]
            }
        };

        var report = Validate(state);
        AssertCode(report, "replacement.cycle");
    }

    [NUnitTestAttribute]
    public void RejectsABaselineCaseUsedAsAReplacementTarget()
    {
        var state = CompatibilityFixture.Create();
        state = state with
        {
            Ledger = state.Ledger with
            {
                Replacements = [CompatibilityFixture.Replacement(0, 1)]
            }
        };

        var report = Validate(state);
        AssertCode(report, "replacement.target-not-executable");
    }

    [NUnitTestAttribute]
    public void PassingLockedBaselineCaseNeedsNoSelfAttestedOutput()
    {
        var state = CompatibilityFixture.Execute(CompatibilityFixture.Create(), 0, NUnitTestOutcome.Passed);
        var report = Validate(state);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
            NUnitAssert.That(report.PassingWitnesses, NUnitIs.EqualTo(1));
        });
    }

    [NUnitTestAttribute]
    public void RejectsAChangedFixtureBodyAgainstTheLedgerDigest()
    {
        var state = CompatibilityFixture.Execute(CompatibilityFixture.Create(), 0, NUnitTestOutcome.Passed);
        state = state with
        {
            Snapshot = state.Snapshot with
            {
                FileDigests = new Dictionary<string, string>
                {
                    [CompatibilityFixture.SourceFile] = CompatibilityDigest.Sha256("weakened fixture")
                }
            }
        };

        var report = Validate(state);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "snapshot.source-changed");
            AssertCode(report, "witness.source-not-locked");
        });
    }

    [NUnitTestAttribute]
    public void RejectsAnExecutionWhoseCompiledMethodDoesNotComeFromTheBoundSource()
    {
        var state = CompatibilityFixture.Execute(CompatibilityFixture.Create(), 0, NUnitTestOutcome.Passed);
        var compiled = new Dictionary<string, CompiledTestSourceVerification>(
            state.Snapshot.CompiledSourceVerifications!,
            StringComparer.Ordinal)
        {
            [state.Ledger.Cases[0].CaseId] = new(
                false,
                "compiled-source.method-document-mismatch",
                "The method was compiled from an impostor document.")
        };
        state = state with
        {
            Snapshot = state.Snapshot with { CompiledSourceVerifications = compiled }
        };

        var report = Validate(state);

        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "execution.compiled-source-mismatch");
            NUnitAssert.That(report.PassingWitnesses, NUnitIs.Zero);
        });
    }

    [NUnitTestAttribute]
    public void AcceptsALockedSupportDocumentAsPartOfTheWitness()
    {
        var state = WithSupportDocument(CompatibilityFixture.Create(), CompatibilityDigest.Sha256("support"));
        state = CompatibilityFixture.Execute(state, 0, NUnitTestOutcome.Passed);

        var report = Validate(state);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
            NUnitAssert.That(report.PassingWitnesses, NUnitIs.EqualTo(1));
        });
    }

    [NUnitTestAttribute]
    public void RejectsAChangedSupportDocumentAsAWeakenedWitness()
    {
        var state = WithSupportDocument(CompatibilityFixture.Create(), CompatibilityDigest.Sha256("different"));
        state = CompatibilityFixture.Execute(state, 0, NUnitTestOutcome.Passed);

        var report = Validate(state);

        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "snapshot.source-changed");
            AssertCode(report, "witness.source-not-locked");
        });
    }

    [NUnitTestAttribute]
    public void VerifiedHistoryAnchoredReplacementClosesAnAbsentCase()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(CompatibilityFixture.Create(), 0, 302);
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void EquivalentReplacementPreservesTheSourceSemanticExpectation()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.Equivalent,
            302);

        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void EquivalentReplacementRejectsChangedSemantics()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.Equivalent,
            302);
        previous = ChangeTargetCase(previous, static target => target with
        {
            Expected = target.Expected with
            {
                SemanticDigest = CompatibilityDigest.Sha256("changed equivalent semantics")
            }
        });

        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.target-changed-semantics");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void PartitionRequiresMoreThanOneTarget()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.Partition,
            ReplacementOutcome.Equivalent,
            302);

        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.partition-target-count");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void EquivalentPartitionCannotCloseUntilAggregateSemanticsAreModeled()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.Partition,
            ReplacementOutcome.Equivalent,
            302,
            303);
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.partition-composition-unmodeled");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void PartitionRegistrationRequiresAPinnedOwnerDecision()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.Partition,
            ReplacementOutcome.Equivalent,
            302,
            303);
        previous = previous with
        {
            Ledger = previous.Ledger with
            {
                Replacements = previous.Ledger.Replacements
                    .Select(static value => value with { DecisionReference = null })
                    .ToArray()
            }
        };
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.missing-decision");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void EvidenceRecoveryCanCloseAnUnknownLostSourceWithAReviewedExpectation()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.EvidenceRecovered,
            302);
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void VerifiedReplacementChainClosesARetiredIntermediateWitness()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.Equivalent,
            302);
        previous = CompatibilityFixture.AddEquivalentReplacementCandidateFromWitness(previous, 302, 303);

        var current = CompatibilityFixture.VerifyAllReplacements(previous);
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.c000");
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.target.c302");

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void EvidenceRecoveredWitnessCanLaterBeReplacedEquivalently()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.EvidenceRecovered,
            302);
        previous = CompatibilityFixture.AddEquivalentReplacementCandidateFromWitness(previous, 302, 303);

        var current = CompatibilityFixture.VerifyAllReplacements(previous);
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.c000");
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.target.c302");

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void EquivalentRecoveredWitnessRejectsAnUnrelatedRequirementDisposition()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.EvidenceRecovered,
            302);
        previous = CompatibilityFixture.AddEquivalentReplacementCandidateFromWitness(previous, 302, 303);
        var requirements = previous.Ledger.Requirements.Select(value =>
            value.RequirementId == "req.primary"
                ? value with { Disposition = RequirementDisposition.InventoryRequired }
                : value).ToArray();
        previous = previous with
        {
            Ledger = previous.Ledger with { Requirements = requirements }
        };

        var current = CompatibilityFixture.VerifyAllReplacements(previous);
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.c000");
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.target.c302");

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.requirement-disposition-mismatch");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void ScopeCorrectionCandidateCanRetainItsLockedSemanticsByOwnerDecision()
    {
        var previous = CompatibilityFixture.AddEquivalentScopeResolutionCandidate(
            CompatibilityFixture.Create(),
            0,
            302);
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.c000");

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void RetainedScopeWitnessCanLaterBeReplacedWithoutASecondDecision()
    {
        var previous = CompatibilityFixture.AddEquivalentScopeResolutionCandidate(
            CompatibilityFixture.Create(),
            0,
            302);
        previous = CompatibilityFixture.AddEquivalentReplacementCandidateFromWitness(previous, 302, 303);

        var current = CompatibilityFixture.VerifyAllReplacements(previous);
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.c000");
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.target.c302");

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void ApprovedDivergenceNeedsChangedSemanticsAndAResolvedDecision()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.ApprovedDivergence,
            302);
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void ScopeCorrectionNeedsTheClassifiedSourceAndACorrectedRequirement()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.ScopeCorrection,
            302);
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.That(report.IsValid, NUnitIs.True, Join(report));
    }

    [NUnitTestAttribute]
    public void OwnershipCorrectionRejectsTheSameRequirementOwner()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.OwnershipCorrect,
            302);
        var sourceRequirement = previous.Ledger.Requirements
            .Single(static value => value.RequirementId == "req.primary");
        var requirements = previous.Ledger.Requirements
            .Select(requirement => requirement.RequirementId == "req.target.r000"
                ? requirement with { Owner = sourceRequirement.Owner }
                : requirement)
            .ToArray();
        previous = previous with { Ledger = previous.Ledger with { Requirements = requirements } };
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.target-requirement-transition");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void OwnershipCorrectionRejectsANonterminalTargetRequirement()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.OwnershipCorrect,
            302);
        var requirements = previous.Ledger.Requirements
            .Select(requirement => requirement.RequirementId == "req.target.r000"
                ? requirement with { Disposition = RequirementDisposition.CandidateDivergence }
                : requirement)
            .ToArray();
        previous = previous with { Ledger = previous.Ledger with { Requirements = requirements } };
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.target-requirement-transition");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void ApprovedDivergenceRejectsUnchangedSemantics()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.ApprovedDivergence,
            302);
        var sourceDigest = previous.Ledger.Cases
            .Single(static value => value.CaseId == "case.c000")
            .Expected.SemanticDigest;
        previous = ChangeTargetCase(previous, target => target with
        {
            Expected = target.Expected with { SemanticDigest = sourceDigest }
        });
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.target-unchanged-semantics");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void AnOutcomeCannotRetireADifferentSourceDisposition()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.OwnershipCorrect,
            302);
        var replacements = previous.Ledger.Replacements.ToArray();
        replacements[0] = replacements[0] with { Outcome = ReplacementOutcome.ScopeCorrection };
        previous = previous with { Ledger = previous.Ledger with { Replacements = replacements } };
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.source-disposition-mismatch");
            AssertCode(report, "replacement.requirement-disposition-mismatch");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void UnknownDecisionReferenceCannotAuthorizeAReplacement()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.ApprovedDivergence,
            302);
        var replacements = previous.Ledger.Replacements.ToArray();
        replacements[0] = replacements[0] with { DecisionReference = "decision.owner.unknown" };
        previous = previous with { Ledger = previous.Ledger with { Replacements = replacements } };
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.unresolved-decision");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void DecisionOutsideTheSourceRequirementCannotAuthorizeAReplacement()
    {
        var previous = CompatibilityFixture.AddEquivalentScopeResolutionCandidate(
            CompatibilityFixture.Create(),
            0,
            302);
        var decisionId = previous.Ledger.Replacements.Single().DecisionReference!;
        var requirements = previous.Ledger.Requirements.Select(value =>
            value.RequirementId == "req.primary"
                ? value with
                {
                    SourceIds = value.SourceIds
                        .Where(sourceId => !string.Equals(sourceId, decisionId, StringComparison.Ordinal))
                        .ToArray()
                }
                : value).ToArray();
        previous = previous with
        {
            Ledger = previous.Ledger with { Requirements = requirements }
        };
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecutionByCaseId(current, "case.c000");

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.decision-outside-requirement");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void ADecisionAddedOnlyAtVerificationCannotCloseAReplacement()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(
            CompatibilityFixture.Create(),
            0,
            ReplacementShape.OneToOne,
            ReplacementOutcome.ApprovedDivergence,
            302);
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        var originalDecision = current.Ledger.Sources
            .Single(static value => value.EvidenceClass == EvidenceClass.ArronixOwnerDecision);
        var newDecision = originalDecision with
        {
            SourceId = "decision.owner.new",
            Title = "A decision added during verification",
            Locator = "evidence/decision-new.json",
            Revision = new SourceRevision
            {
                Kind = RevisionKind.ArtifactSha256,
                Value = CompatibilityDigest.Sha256("new decision")[7..]
            }
        };
        var replacements = current.Ledger.Replacements.ToArray();
        replacements[0] = replacements[0] with { DecisionReference = newDecision.SourceId };
        var identities = new Dictionary<string, bool>(
            current.Snapshot.SourceIdentityMatches!,
            StringComparer.Ordinal)
        {
            [newDecision.SourceId] = true
        };
        current = current with
        {
            Ledger = current.Ledger with
            {
                Sources = current.Ledger.Sources.Append(newDecision).ToArray(),
                Requirements = current.Ledger.Requirements.Select(requirement =>
                    requirement.RequirementId == "req.primary"
                        ? requirement with
                        {
                            SourceIds = requirement.SourceIds.Append(newDecision.SourceId).ToArray()
                        }
                        : requirement).ToArray(),
                Replacements = replacements
            },
            Snapshot = current.Snapshot with { SourceIdentityMatches = identities }
        };
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.decision-not-history-anchored");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void ANewVerifiedReplacementCannotCloseInTheSameLedgerVersion()
    {
        var state = CompatibilityFixture.AddReplacementCandidate(CompatibilityFixture.Create(), 0, 302);
        state = CompatibilityFixture.VerifyReplacement(state, 0);
        state = CompatibilityFixture.RemoveExecution(state, 0);

        var report = Validate(state);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.not-history-anchored");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void RejectsAChangedReplacementGraphAcrossLedgerVersions()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(CompatibilityFixture.Create(), 0, 302);
        var replacements = previous.Ledger.Replacements.ToArray();
        replacements[0] = replacements[0] with { ToCaseIds = ["case.c001"] };
        var current = previous with { Ledger = previous.Ledger with { Replacements = replacements } };

        var report = Validate(current, previous.Ledger);
        AssertCode(report, "history.replacement-changed");
    }

    [NUnitTestAttribute]
    public void BaselineOnlyEvidenceCannotCloseAReplacement()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(CompatibilityFixture.Create(), 0, 302);
        previous = ChangeTargetSource(previous, static source => source with { ProofUse = ProofUse.BaselineOnly });
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "replacement.target-ineligible");
            AssertCode(report, "execution.case-disappeared");
        });
    }

    [NUnitTestAttribute]
    public void PendingIndependentReviewCannotCloseAReplacement()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(CompatibilityFixture.Create(), 0, 302);
        previous = ChangeTargetSource(previous, static source => source with
        {
            Provenance = source.Provenance with { Independence = Independence.PendingIndependentReview }
        });
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "source.restricted-used-as-proof");
            AssertCode(report, "replacement.target-ineligible");
        });
    }

    [NUnitTestAttribute]
    public void MissingOrWrongArtifactIdentityCannotCloseAReplacement()
    {
        var previous = CompatibilityFixture.AddReplacementCandidate(CompatibilityFixture.Create(), 0, 302);
        var targetSourceId = previous.Ledger.Cases.Single(static value => value.Introduced is not null).SourceId;
        var identities = new Dictionary<string, bool>(
            previous.Snapshot.SourceIdentityMatches!,
            StringComparer.Ordinal)
        {
            [targetSourceId] = false
        };
        previous = previous with
        {
            Snapshot = previous.Snapshot with { SourceIdentityMatches = identities }
        };
        var current = CompatibilityFixture.VerifyReplacement(previous, 0);
        current = CompatibilityFixture.RemoveExecution(current, 0);

        var report = Validate(current, previous.Ledger);
        NUnitAssert.Multiple(() =>
        {
            AssertCode(report, "source.identity-not-resolved");
            AssertCode(report, "replacement.target-ineligible");
        });
    }

    [NUnitTestAttribute]
    public void RejectsAnEvidenceSourceCaseCountMismatch()
    {
        var state = CompatibilityFixture.Create();
        var sources = state.Ledger.Sources.ToArray();
        sources[0] = sources[0] with { CaseCount = 301 };
        state = state with { Ledger = state.Ledger with { Sources = sources } };

        var report = Validate(state);
        AssertCode(report, "source.case-count");
    }

    [NUnitTestAttribute]
    public void RejectsARewrittenBaselineAcrossLedgerVersions()
    {
        var previous = CompatibilityFixture.Create();
        var runs = previous.Ledger.Baseline.Runs.ToArray();
        runs[0] = runs[0] with { ArtifactDigest = CompatibilityDigest.Sha256("different capture") };
        var current = previous with
        {
            Ledger = previous.Ledger with
            {
                Baseline = previous.Ledger.Baseline with { Runs = runs }
            }
        };

        var report = Validate(current, previous.Ledger);
        AssertCode(report, "history.baseline-changed");
    }

    private static FixtureState ChangeTargetSource(
        FixtureState state,
        Func<CompatibilitySource, CompatibilitySource> change)
    {
        var targetSourceId = state.Ledger.Cases.Single(static value => value.Introduced is not null).SourceId;
        var sources = state.Ledger.Sources
            .Select(source => string.Equals(source.SourceId, targetSourceId, StringComparison.Ordinal)
                ? change(source)
                : source)
            .ToArray();
        return state with { Ledger = state.Ledger with { Sources = sources } };
    }

    private static FixtureState WithSupportDocument(FixtureState state, string observedDigest)
    {
        const string supportFile = "src/Tests/Corpus.cs";
        var expectedDigest = CompatibilityDigest.Sha256("support");
        var cases = state.Ledger.Cases.ToArray();
        cases[0] = cases[0] with
        {
            Binding = cases[0].Binding with
            {
                SupportDocuments =
                [
                    new CaseSupportDocument
                    {
                        SourceFile = supportFile,
                        SourceFileDigest = expectedDigest
                    }
                ]
            }
        };
        var fileDigests = new Dictionary<string, string>(state.Snapshot.FileDigests, StringComparer.Ordinal)
        {
            [supportFile] = observedDigest
        };
        return state with
        {
            Ledger = state.Ledger with { Cases = cases },
            Snapshot = state.Snapshot with { FileDigests = fileDigests }
        };
    }

    private static FixtureState ChangeTargetCase(
        FixtureState state,
        Func<CompatibilityCase, CompatibilityCase> change)
        => ChangeTargetCase(state, static value => value.Introduced is not null, change);

    private static FixtureState ChangeTargetCase(
        FixtureState state,
        Func<CompatibilityCase, bool> predicate,
        Func<CompatibilityCase, CompatibilityCase> change)
    {
        var cases = state.Ledger.Cases
            .Select(value => predicate(value) ? change(value) : value)
            .ToArray();
        return state with { Ledger = state.Ledger with { Cases = cases } };
    }

    private static CompatibilityValidationReport Validate(
        FixtureState state,
        CompatibilityLedger? previous = null)
        => PinnedCompatibilityValidator.Validate(state.Ledger, state.Run, state.Snapshot, previous);

    private static void AssertCode(CompatibilityValidationReport report, string code)
        => NUnitAssert.That(report.Diagnostics.Select(static value => value.Code), NUnitDoes.Contain(code), Join(report));

    private static string Join(CompatibilityValidationReport report)
        => string.Join(Environment.NewLine, report.Diagnostics.Select(static value => $"{value.Code}: {value.Message}"));
}
