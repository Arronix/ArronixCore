using System.Text.Json.Serialization;

namespace Arronix.Compatibility.Ratchet;

/// <summary>The canonical compatibility ledger loaded from its five checked-in documents.</summary>
public sealed record CompatibilityLedger(
    CompatibilityBaselineDocument Baseline,
    IReadOnlyList<CompatibilitySource> Sources,
    IReadOnlyList<CompatibilityRequirement> Requirements,
    IReadOnlyList<CompatibilityCase> Cases,
    IReadOnlyList<CompatibilityReplacement> Replacements);

/// <summary>The immutable R00 omission baseline and monotonic current skip count.</summary>
public sealed record CompatibilityBaselineDocument
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }

    public required int SchemaVersion { get; init; }

    public required string BaselineId { get; init; }

    public required string RepositoryBaselineCommit { get; init; }

    public required string CaptureHeadCommit { get; init; }

    public required IReadOnlyList<BaselineRun> Runs { get; init; }

    public required BaselineTotals Totals { get; init; }

    public required int CurrentSkipCount { get; init; }

    public required InitialRecordCounts InitialRecordCounts { get; init; }

    public required IReadOnlyDictionary<string, int> ReasonCounts { get; init; }

    public required IReadOnlyList<FixtureSkipCount> FixtureCounts { get; init; }

    public required IReadOnlyList<string> ZeroCaseRequirementIds { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }
}

public sealed record BaselineRun
{
    public required string RunId { get; init; }

    public required string Project { get; init; }

    public required string Format { get; init; }

    public required string ArtifactDigest { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required int Total { get; init; }

    public required int Passed { get; init; }

    public required int Failed { get; init; }

    public required int Inconclusive { get; init; }

    public required int Skipped { get; init; }
}

public sealed record BaselineTotals
{
    public required int CapturedCases { get; init; }

    public required int Passed { get; init; }

    public required int Failed { get; init; }

    public required int Inconclusive { get; init; }

    public required int Skipped { get; init; }
}

public sealed record InitialRecordCounts
{
    public required int Sources { get; init; }

    public required int Requirements { get; init; }

    public required int Cases { get; init; }

    public required int Replacements { get; init; }
}

public sealed record FixtureSkipCount
{
    public required string Fixture { get; init; }

    public required int Skipped { get; init; }
}

/// <summary>A provenance-qualified source of compatibility evidence.</summary>
public sealed record CompatibilitySource
{
    public required int SchemaVersion { get; init; }

    public required string SourceId { get; init; }

    public required string Title { get; init; }

    public required EvidenceClass EvidenceClass { get; init; }

    public required ArtifactState ArtifactState { get; init; }

    public string? Locator { get; init; }

    public SourceRevision? Revision { get; init; }

    public int? CaseCount { get; init; }

    public IReadOnlyList<string>? DerivedFromSourceIds { get; init; }

    public required SourceProvenance Provenance { get; init; }

    public required ProofUse ProofUse { get; init; }

    public required IReadOnlyList<string> Restrictions { get; init; }
}

public sealed record SourceRevision
{
    public required RevisionKind Kind { get; init; }

    public required string Value { get; init; }
}

public sealed record SourceProvenance
{
    public required Independence Independence { get; init; }

    public required EvidenceAccess Access { get; init; }

    public required EvidenceCurrency Currency { get; init; }

    public required PinState PinState { get; init; }
}

/// <summary>A permanent semantic compatibility requirement.</summary>
public sealed record CompatibilityRequirement
{
    public required int SchemaVersion { get; init; }

    public required string RequirementId { get; init; }

    public required string Title { get; init; }

    public required string Statement { get; init; }

    public required string Slice { get; init; }

    public required RequirementScope Scope { get; init; }

    public required RequirementOwner Owner { get; init; }

    public required IReadOnlyList<string> SourceIds { get; init; }

    public required BaselineStatus BaselineStatus { get; init; }

    public required string CurrentReason { get; init; }

    public required RequirementDisposition Disposition { get; init; }

    public required RequirementTarget Target { get; init; }

    public required ClosurePolicy ClosurePolicy { get; init; }

    public required int CaseCount { get; init; }
}

public sealed record RequirementOwner
{
    public required string Id { get; init; }

    public required OwnerState State { get; init; }
}

public sealed record RequirementTarget
{
    public required string ClassificationGate { get; init; }

    public required string ClosureGate { get; init; }

    public required IReadOnlyList<string> Prerequisites { get; init; }
}

/// <summary>A permanent semantic case and its current mutable NUnit binding.</summary>
public sealed record CompatibilityCase
{
    public required int SchemaVersion { get; init; }

    public required string CaseId { get; init; }

    public required string RequirementId { get; init; }

    public required string SourceId { get; init; }

    public required CaseBinding Binding { get; init; }

    public required CaseExpectation Expected { get; init; }

    public BaselineCaseObservation? Baseline { get; init; }

    public IntroducedCase? Introduced { get; init; }

    public required CaseDisposition Disposition { get; init; }
}

public sealed record CaseBinding
{
    public required string Framework { get; init; }

    public required string Project { get; init; }

    public required string Fixture { get; init; }

    public required string Method { get; init; }

    public required string FullNameDigest { get; init; }

    public required string SourceFile { get; init; }

    public required string SourceFileDigest { get; init; }

    /// <summary>Gets additional compiled source documents which carry data used by the bound test method.</summary>
    public IReadOnlyList<CaseSupportDocument> SupportDocuments { get; init; } = [];
}

/// <summary>An additional source document whose compiled content is part of a compatibility witness.</summary>
public sealed record CaseSupportDocument
{
    public required string SourceFile { get; init; }

    public required string SourceFileDigest { get; init; }
}

public sealed record CaseExpectation
{
    public required ExpectedState State { get; init; }

    public ExpectedKind? Kind { get; init; }

    public string? SemanticDigest { get; init; }

    public string? GapReason { get; init; }
}

public sealed record BaselineCaseObservation
{
    public required string RunId { get; init; }

    public required string Outcome { get; init; }

    public required string ReasonCode { get; init; }

    public required string ReasonDigest { get; init; }
}

public sealed record IntroducedCase
{
    public required string RegisteredAtGate { get; init; }

    public required string ExpectedResult { get; init; }

    public required IntroducedCaseRole Role { get; init; }
}

public sealed record CaseDisposition
{
    public required CaseDispositionKind Kind { get; init; }

    public required string ClassificationGate { get; init; }

    public required string ClosureGate { get; init; }
}

/// <summary>An explicit attempt to retire one permanent case through distinct target witnesses.</summary>
public sealed record CompatibilityReplacement
{
    public required int SchemaVersion { get; init; }

    public required string ReplacementId { get; init; }

    public required string FromCaseId { get; init; }

    public required IReadOnlyList<string> ToCaseIds { get; init; }

    public required ReplacementShape Shape { get; init; }

    public required ReplacementOutcome Outcome { get; init; }

    public required ReplacementCoverage Coverage { get; init; }

    public required ReplacementStatus Status { get; init; }

    public required string Rationale { get; init; }

    public string? DecisionReference { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceClass>))]
public enum EvidenceClass
{
    [JsonStringEnumMemberName("upstream-regression")] UpstreamRegression,
    [JsonStringEnumMemberName("repository-regression")] RepositoryRegression,
    [JsonStringEnumMemberName("generated-clean-room")] GeneratedCleanRoom,
    [JsonStringEnumMemberName("sanitized-specification")] SanitizedSpecification,
    [JsonStringEnumMemberName("field-observation")] FieldObservation,
    [JsonStringEnumMemberName("platform-filesystem")] PlatformFilesystem,
    [JsonStringEnumMemberName("hostile-input")] HostileInput,
    [JsonStringEnumMemberName("arronix-owner-decision")] ArronixOwnerDecision,
    [JsonStringEnumMemberName("architecture-governance")] ArchitectureGovernance,
    [JsonStringEnumMemberName("historical-gatekeeper-record")] HistoricalGatekeeperRecord,
    [JsonStringEnumMemberName("derived-shape-inventory")] DerivedShapeInventory
}

[JsonConverter(typeof(JsonStringEnumConverter<ArtifactState>))]
public enum ArtifactState
{
    Current,
    Historical,
    Missing,
    Empty,
    Superseded
}

[JsonConverter(typeof(JsonStringEnumConverter<RevisionKind>))]
public enum RevisionKind
{
    [JsonStringEnumMemberName("repository-commit")] RepositoryCommit,
    [JsonStringEnumMemberName("artifact-sha256")] ArtifactSha256,
    Version,
    Unversioned
}

[JsonConverter(typeof(JsonStringEnumConverter<Independence>))]
public enum Independence
{
    [JsonStringEnumMemberName("not-applicable")] NotApplicable,
    [JsonStringEnumMemberName("not-established")] NotEstablished,
    [JsonStringEnumMemberName("dirty-side")] DirtySide,
    [JsonStringEnumMemberName("pending-independent-review")] PendingIndependentReview,
    Independent
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceAccess>))]
public enum EvidenceAccess
{
    Normal,
    [JsonStringEnumMemberName("gatekeeper-only")] GatekeeperOnly,
    [JsonStringEnumMemberName("surveyor-and-gatekeeper")] SurveyorAndGatekeeper
}

[JsonConverter(typeof(JsonStringEnumConverter<EvidenceCurrency>))]
public enum EvidenceCurrency
{
    [JsonStringEnumMemberName("current-baseline")] CurrentBaseline,
    Current,
    Historical,
    Superseded,
    Absent
}

[JsonConverter(typeof(JsonStringEnumConverter<PinState>))]
public enum PinState
{
    [JsonStringEnumMemberName("repository-pinned")] RepositoryPinned,
    [JsonStringEnumMemberName("artifact-pinned")] ArtifactPinned,
    Unpinned,
    Missing
}

[JsonConverter(typeof(JsonStringEnumConverter<ProofUse>))]
public enum ProofUse
{
    Eligible,
    [JsonStringEnumMemberName("baseline-only")] BaselineOnly,
    Ineligible
}

[JsonConverter(typeof(JsonStringEnumConverter<RequirementScope>))]
public enum RequirementScope
{
    [JsonStringEnumMemberName("in-scope")] InScope,
    [JsonStringEnumMemberName("inventory-control")] InventoryControl
}

[JsonConverter(typeof(JsonStringEnumConverter<OwnerState>))]
public enum OwnerState
{
    Assigned,
    Provisional,
    Unresolved
}

[JsonConverter(typeof(JsonStringEnumConverter<BaselineStatus>))]
public enum BaselineStatus
{
    Skipped,
    [JsonStringEnumMemberName("missing-proof")] MissingProof,
    [JsonStringEnumMemberName("recorded-exception")] RecordedException,
    [JsonStringEnumMemberName("inventory-zero")] InventoryZero
}

[JsonConverter(typeof(JsonStringEnumConverter<RequirementDisposition>))]
public enum RequirementDisposition
{
    Restore,
    [JsonStringEnumMemberName("ownership-correct-replacement")] OwnershipCorrectReplacement,
    [JsonStringEnumMemberName("evidence-gap")] EvidenceGap,
    [JsonStringEnumMemberName("candidate-divergence")] CandidateDivergence,
    [JsonStringEnumMemberName("scope-correction-candidate")] ScopeCorrectionCandidate,
    [JsonStringEnumMemberName("inventory-required")] InventoryRequired,
    [JsonStringEnumMemberName("inventory-zero")] InventoryZero
}

[JsonConverter(typeof(JsonStringEnumConverter<ClosurePolicy>))]
public enum ClosurePolicy
{
    [JsonStringEnumMemberName("all-cases")] AllCases,
    [JsonStringEnumMemberName("inventory-complete")] InventoryComplete,
    [JsonStringEnumMemberName("inventory-nonzero")] InventoryNonzero,
    [JsonStringEnumMemberName("owner-decision")] OwnerDecision,
    [JsonStringEnumMemberName("inventory-zero")] InventoryZero
}

[JsonConverter(typeof(JsonStringEnumConverter<ExpectedState>))]
public enum ExpectedState
{
    [JsonStringEnumMemberName("known-regression")] KnownRegression,
    [JsonStringEnumMemberName("unknown-lost")] UnknownLost
}

[JsonConverter(typeof(JsonStringEnumConverter<ExpectedKind>))]
public enum ExpectedKind
{
    Invariant,
    [JsonStringEnumMemberName("structured-value")] StructuredValue,
    Rejection,
    Diagnostic,
    Governance
}

[JsonConverter(typeof(JsonStringEnumConverter<CaseDispositionKind>))]
public enum CaseDispositionKind
{
    Restore,
    [JsonStringEnumMemberName("ownership-correct-replacement")] OwnershipCorrectReplacement,
    [JsonStringEnumMemberName("evidence-gap")] EvidenceGap,
    [JsonStringEnumMemberName("candidate-divergence")] CandidateDivergence,
    [JsonStringEnumMemberName("scope-correction-candidate")] ScopeCorrectionCandidate,
    Proven
}

[JsonConverter(typeof(JsonStringEnumConverter<IntroducedCaseRole>))]
public enum IntroducedCaseRole
{
    Coverage,
    [JsonStringEnumMemberName("replacement-witness")] ReplacementWitness
}

[JsonConverter(typeof(JsonStringEnumConverter<ReplacementShape>))]
public enum ReplacementShape
{
    [JsonStringEnumMemberName("one-to-one")] OneToOne,
    Partition
}

[JsonConverter(typeof(JsonStringEnumConverter<ReplacementOutcome>))]
public enum ReplacementOutcome
{
    Equivalent,
    [JsonStringEnumMemberName("ownership-correct")] OwnershipCorrect,
    [JsonStringEnumMemberName("evidence-recovered")] EvidenceRecovered,
    [JsonStringEnumMemberName("approved-divergence")] ApprovedDivergence,
    [JsonStringEnumMemberName("scope-correction")] ScopeCorrection
}

[JsonConverter(typeof(JsonStringEnumConverter<ReplacementCoverage>))]
public enum ReplacementCoverage
{
    Partial,
    Full
}

[JsonConverter(typeof(JsonStringEnumConverter<ReplacementStatus>))]
public enum ReplacementStatus
{
    Candidate,
    Approved,
    Verified
}
