using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Projects the canonical ledger into a stable classification inventory without evaluating compatibility proof.</summary>
public static class ClassificationReportGenerator
{
    public const string Format = "arronix.compatibility.classification-report";
    public const int SchemaVersion = 1;

    /// <summary>Builds a deterministic inventory from facts already declared in the ledger and execution result.</summary>
    public static CompatibilityClassificationReport Generate(CompatibilityLedger ledger, NUnitTestRun run)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(run);

        var sources = ledger.Sources.OrderBy(static value => value.SourceId, StringComparer.Ordinal).ToArray();
        var sourcesById = sources.ToDictionary(static value => value.SourceId, StringComparer.Ordinal);
        var requirements = ledger.Requirements.OrderBy(static value => value.RequirementId, StringComparer.Ordinal).ToArray();

        return new CompatibilityClassificationReport
        {
            Format = Format,
            SchemaVersion = SchemaVersion,
            SkipCounts = new ClassificationSkipCounts
            {
                Baseline = ledger.Baseline.Totals.Skipped,
                Run = run.Counts.Skipped,
                Current = ledger.Baseline.CurrentSkipCount
            },
            BaselineRuns = ledger.Baseline.Runs
                .OrderBy(static value => value.RunId, StringComparer.Ordinal)
                .Select(static value => new ClassificationBaselineRun
                {
                    RunId = value.RunId,
                    Project = value.Project,
                    Skipped = value.Skipped
                }).ToArray(),
            RequirementsByDisposition = Enum.GetValues<RequirementDisposition>()
                .Select(disposition => new ClassificationDispositionCount
                {
                    Disposition = disposition,
                    RequirementCount = requirements.Count(requirement => requirement.Disposition == disposition),
                    CaseCount = requirements
                        .Where(requirement => requirement.Disposition == disposition)
                        .Sum(static requirement => requirement.CaseCount)
                }).ToArray(),
            RequirementsByOwnerState = Enum.GetValues<OwnerState>()
                .Select(ownerState => new ClassificationOwnerStateCount
                {
                    OwnerState = ownerState,
                    RequirementCount = requirements.Count(requirement => requirement.Owner.State == ownerState),
                    CaseCount = requirements
                        .Where(requirement => requirement.Owner.State == ownerState)
                        .Sum(static requirement => requirement.CaseCount)
                }).ToArray(),
            SourcesByProofUse = Enum.GetValues<ProofUse>()
                .Select(proofUse => new ClassificationProofUseCount
                {
                    ProofUse = proofUse,
                    SourceCount = sources.Count(source => source.ProofUse == proofUse),
                    DeclaredCaseCount = sources
                        .Where(source => source.ProofUse == proofUse)
                        .Sum(static source => source.CaseCount ?? 0)
                }).ToArray(),
            SourcesByArtifactState = Enum.GetValues<ArtifactState>()
                .Select(artifactState => new ClassificationArtifactStateCount
                {
                    ArtifactState = artifactState,
                    SourceCount = sources.Count(source => source.ArtifactState == artifactState),
                    DeclaredCaseCount = sources
                        .Where(source => source.ArtifactState == artifactState)
                        .Sum(static source => source.CaseCount ?? 0)
                }).ToArray(),
            SourcesByEvidenceClass = Enum.GetValues<EvidenceClass>()
                .Select(evidenceClass => new ClassificationEvidenceClassCount
                {
                    EvidenceClass = evidenceClass,
                    SourceCount = sources.Count(source => source.EvidenceClass == evidenceClass),
                    DeclaredCaseCount = sources
                        .Where(source => source.EvidenceClass == evidenceClass)
                        .Sum(static source => source.CaseCount ?? 0)
                }).ToArray(),
            Requirements = requirements.Select(requirement => new ClassificationRequirement
            {
                RequirementId = requirement.RequirementId,
                Slice = requirement.Slice,
                OwnerId = requirement.Owner.Id,
                OwnerState = requirement.Owner.State,
                Disposition = requirement.Disposition,
                Target = new ClassificationTargetGates
                {
                    Classification = requirement.Target.ClassificationGate,
                    Closure = requirement.Target.ClosureGate
                },
                CaseCount = requirement.CaseCount,
                Sources = requirement.SourceIds.Order(StringComparer.Ordinal)
                    .Select(sourceId => ProjectSource(sourcesById, sourceId)).ToArray()
            }).ToArray()
        };
    }

    private static ClassificationSourceEvidence ProjectSource(
        IReadOnlyDictionary<string, CompatibilitySource> sources,
        string sourceId)
    {
        if (!sources.TryGetValue(sourceId, out var source))
        {
            throw new CompatibilityDocumentException(
                $"Requirement references unknown source '{sourceId}' while generating the classification report.");
        }

        return new ClassificationSourceEvidence
        {
            SourceId = source.SourceId,
            ProofUse = source.ProofUse,
            ArtifactState = source.ArtifactState,
            EvidenceClass = source.EvidenceClass,
            DeclaredCaseCount = source.CaseCount,
            Provenance = new ClassificationSourceProvenance
            {
                Independence = source.Provenance.Independence,
                Access = source.Provenance.Access,
                Currency = source.Provenance.Currency,
                PinState = source.Provenance.PinState
            }
        };
    }
}

/// <summary>The versioned, generated classification inventory. It is not compatibility or parity proof.</summary>
public sealed record CompatibilityClassificationReport
{
    public required string Format { get; init; }
    public required int SchemaVersion { get; init; }
    public required ClassificationSkipCounts SkipCounts { get; init; }
    public required IReadOnlyList<ClassificationBaselineRun> BaselineRuns { get; init; }
    public required IReadOnlyList<ClassificationDispositionCount> RequirementsByDisposition { get; init; }
    public required IReadOnlyList<ClassificationOwnerStateCount> RequirementsByOwnerState { get; init; }
    public required IReadOnlyList<ClassificationProofUseCount> SourcesByProofUse { get; init; }
    public required IReadOnlyList<ClassificationArtifactStateCount> SourcesByArtifactState { get; init; }
    public required IReadOnlyList<ClassificationEvidenceClassCount> SourcesByEvidenceClass { get; init; }
    public required IReadOnlyList<ClassificationRequirement> Requirements { get; init; }
}

/// <summary>The three declared skip counts; the run count is observed, not a proof outcome.</summary>
public sealed record ClassificationSkipCounts
{
    public required int Baseline { get; init; }
    public required int Run { get; init; }
    public required int Current { get; init; }
}

/// <summary>One immutable baseline capture's declared skipped-case count.</summary>
public sealed record ClassificationBaselineRun
{
    public required string RunId { get; init; }
    public required string Project { get; init; }
    public required int Skipped { get; init; }
}

/// <summary>An aggregate over requirements in one declared disposition.</summary>
public sealed record ClassificationDispositionCount
{
    public required RequirementDisposition Disposition { get; init; }
    public required int RequirementCount { get; init; }
    public required int CaseCount { get; init; }
}

/// <summary>An aggregate over requirements in one declared owner state.</summary>
public sealed record ClassificationOwnerStateCount
{
    public required OwnerState OwnerState { get; init; }
    public required int RequirementCount { get; init; }
    public required int CaseCount { get; init; }
}

/// <summary>An aggregate over sources with one declared proof-use status.</summary>
public sealed record ClassificationProofUseCount
{
    public required ProofUse ProofUse { get; init; }
    public required int SourceCount { get; init; }
    public required int DeclaredCaseCount { get; init; }
}

/// <summary>An aggregate over sources with one declared artifact state.</summary>
public sealed record ClassificationArtifactStateCount
{
    public required ArtifactState ArtifactState { get; init; }
    public required int SourceCount { get; init; }
    public required int DeclaredCaseCount { get; init; }
}

/// <summary>An aggregate over sources with one declared evidence class.</summary>
public sealed record ClassificationEvidenceClassCount
{
    public required EvidenceClass EvidenceClass { get; init; }
    public required int SourceCount { get; init; }
    public required int DeclaredCaseCount { get; init; }
}

/// <summary>One stable requirement row projected directly from the canonical ledger.</summary>
public sealed record ClassificationRequirement
{
    public required string RequirementId { get; init; }
    public required string Slice { get; init; }
    public required string OwnerId { get; init; }
    public required OwnerState OwnerState { get; init; }
    public required RequirementDisposition Disposition { get; init; }
    public required ClassificationTargetGates Target { get; init; }
    public required int CaseCount { get; init; }
    public required IReadOnlyList<ClassificationSourceEvidence> Sources { get; init; }
}

/// <summary>The declared classification and closure gates for a requirement.</summary>
public sealed record ClassificationTargetGates
{
    public required string Classification { get; init; }
    public required string Closure { get; init; }
}

/// <summary>The exact declared source status attached to a requirement; it does not promote that source to proof.</summary>
public sealed record ClassificationSourceEvidence
{
    public required string SourceId { get; init; }
    public required ProofUse ProofUse { get; init; }
    public required ArtifactState ArtifactState { get; init; }
    public required EvidenceClass EvidenceClass { get; init; }
    public required int? DeclaredCaseCount { get; init; }
    public required ClassificationSourceProvenance Provenance { get; init; }
}

/// <summary>The declared provenance facts for a source; this record does not judge their sufficiency.</summary>
public sealed record ClassificationSourceProvenance
{
    public required Independence Independence { get; init; }
    public required EvidenceAccess Access { get; init; }
    public required EvidenceCurrency Currency { get; init; }
    public required PinState PinState { get; init; }
}

/// <summary>Writes a report by replacing the target only after a complete JSON payload is available.</summary>
public static class ClassificationReportWriter
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    /// <summary>Atomically writes a complete report to the requested path.</summary>
    public static void Write(string path, CompatibilityClassificationReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"The report path '{path}' has no parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, Utf8.GetBytes(JsonSerializer.Serialize(report, SerializerOptions)));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>Removes a stale report when validation did not produce a valid classification artifact.</summary>
    public static void Delete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.Delete(Path.GetFullPath(path));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }
}
