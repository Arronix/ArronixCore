using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Arronix.Compatibility.Ratchet;

/// <summary>Projects the canonical ledger into a stable classification inventory without evaluating compatibility proof.</summary>
public static class ClassificationReportGenerator
{
    public const string Schema = "arronix.compatibility.classification-report/v1";
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
            Schema = Schema,
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
                .Select(disposition => ClassificationRequirementCount.ForDisposition(requirements, disposition)).ToArray(),
            RequirementsByOwnerState = Enum.GetValues<OwnerState>()
                .Select(ownerState => ClassificationRequirementCount.ForOwnerState(requirements, ownerState)).ToArray(),
            SourcesByProofUse = Enum.GetValues<ProofUse>()
                .Select(proofUse => ClassificationSourceCount.ForProofUse(sources, proofUse)).ToArray(),
            SourcesByArtifactState = Enum.GetValues<ArtifactState>()
                .Select(artifactState => ClassificationSourceCount.ForArtifactState(sources, artifactState)).ToArray(),
            SourcesByEvidenceClass = Enum.GetValues<EvidenceClass>()
                .Select(evidenceClass => ClassificationSourceCount.ForEvidenceClass(sources, evidenceClass)).ToArray(),
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
            EvidenceClass = source.EvidenceClass
        };
    }
}

/// <summary>The versioned, generated classification inventory. It is not compatibility or parity proof.</summary>
public sealed record CompatibilityClassificationReport
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }
    public required int SchemaVersion { get; init; }
    public required ClassificationSkipCounts SkipCounts { get; init; }
    public required IReadOnlyList<ClassificationBaselineRun> BaselineRuns { get; init; }
    public required IReadOnlyList<ClassificationRequirementCount> RequirementsByDisposition { get; init; }
    public required IReadOnlyList<ClassificationRequirementCount> RequirementsByOwnerState { get; init; }
    public required IReadOnlyList<ClassificationSourceCount> SourcesByProofUse { get; init; }
    public required IReadOnlyList<ClassificationSourceCount> SourcesByArtifactState { get; init; }
    public required IReadOnlyList<ClassificationSourceCount> SourcesByEvidenceClass { get; init; }
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

/// <summary>An aggregate over requirement records and their declared case inventory.</summary>
public sealed record ClassificationRequirementCount
{
    public RequirementDisposition? Disposition { get; init; }
    public OwnerState? OwnerState { get; init; }
    public required int RequirementCount { get; init; }
    public required int CaseCount { get; init; }

    internal static ClassificationRequirementCount ForDisposition(
        IReadOnlyList<CompatibilityRequirement> requirements,
        RequirementDisposition disposition)
        => Create(requirements.Where(requirement => requirement.Disposition == disposition), disposition, null);

    internal static ClassificationRequirementCount ForOwnerState(
        IReadOnlyList<CompatibilityRequirement> requirements,
        OwnerState ownerState)
        => Create(requirements.Where(requirement => requirement.Owner.State == ownerState), null, ownerState);

    private static ClassificationRequirementCount Create(
        IEnumerable<CompatibilityRequirement> requirements,
        RequirementDisposition? disposition,
        OwnerState? ownerState)
    {
        var values = requirements.ToArray();
        return new ClassificationRequirementCount
        {
            Disposition = disposition,
            OwnerState = ownerState,
            RequirementCount = values.Length,
            CaseCount = values.Sum(static value => value.CaseCount)
        };
    }
}

/// <summary>An aggregate over declared compatibility sources.</summary>
public sealed record ClassificationSourceCount
{
    public ProofUse? ProofUse { get; init; }
    public ArtifactState? ArtifactState { get; init; }
    public EvidenceClass? EvidenceClass { get; init; }
    public required int SourceCount { get; init; }
    public required int DeclaredCaseCount { get; init; }

    internal static ClassificationSourceCount ForProofUse(IReadOnlyList<CompatibilitySource> sources, ProofUse proofUse)
        => Create(sources.Where(source => source.ProofUse == proofUse), proofUse, null, null);

    internal static ClassificationSourceCount ForArtifactState(
        IReadOnlyList<CompatibilitySource> sources,
        ArtifactState artifactState)
        => Create(sources.Where(source => source.ArtifactState == artifactState), null, artifactState, null);

    internal static ClassificationSourceCount ForEvidenceClass(
        IReadOnlyList<CompatibilitySource> sources,
        EvidenceClass evidenceClass)
        => Create(sources.Where(source => source.EvidenceClass == evidenceClass), null, null, evidenceClass);

    private static ClassificationSourceCount Create(
        IEnumerable<CompatibilitySource> sources,
        ProofUse? proofUse,
        ArtifactState? artifactState,
        EvidenceClass? evidenceClass)
    {
        var values = sources.ToArray();
        return new ClassificationSourceCount
        {
            ProofUse = proofUse,
            ArtifactState = artifactState,
            EvidenceClass = evidenceClass,
            SourceCount = values.Length,
            DeclaredCaseCount = values.Sum(static value => value.CaseCount ?? 0)
        };
    }
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
