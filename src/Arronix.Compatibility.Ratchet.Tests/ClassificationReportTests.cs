namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class ClassificationReportTests
{
    [Test]
    public void R00ProjectionRetainsTheDeclaredG08InventoryFacts()
    {
        var ledger = CompatibilityDocumentReader.ReadLedger(Path.Combine(RepositoryRoot, "verification", "compatibility"));
        var report = ClassificationReportGenerator.Generate(ledger, CreateSkippedRun(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(report.Format, Is.EqualTo(ClassificationReportGenerator.Format));
            Assert.That(report.SchemaVersion, Is.EqualTo(1));
            Assert.That(report.SkipCounts, Is.EqualTo(new ClassificationSkipCounts
            {
                Baseline = 302,
                Run = 302,
                Current = 302
            }));
            Assert.That(report.BaselineRuns.Single(value => value.RunId == "run.r00.movies-nunit").Skipped, Is.EqualTo(301));
            Assert.That(report.BaselineRuns.Single(value => value.RunId == "run.r00.architecture-nunit").Skipped, Is.EqualTo(1));
            Assert.That(ByDisposition(report, RequirementDisposition.Restore).CaseCount, Is.EqualTo(158));
            Assert.That(ByDisposition(report, RequirementDisposition.OwnershipCorrectReplacement).CaseCount, Is.EqualTo(114));
            Assert.That(ByDisposition(report, RequirementDisposition.EvidenceGap).CaseCount, Is.EqualTo(20));
            Assert.That(ByDisposition(report, RequirementDisposition.CandidateDivergence).CaseCount, Is.EqualTo(9));
            Assert.That(ByDisposition(report, RequirementDisposition.ScopeCorrectionCandidate).CaseCount, Is.EqualTo(1));
            Assert.That(report.Requirements.Count, Is.EqualTo(129));
            Assert.That(report.Requirements.Sum(static value => value.CaseCount), Is.EqualTo(302));
            Assert.That(report.Requirements.Count(static value => value.CaseCount == 0), Is.EqualTo(5));
        });
    }

    [Test]
    public void ProjectionIsStableSortedAndDoesNotPromoteDeclaredStatuses()
    {
        var ledger = CompatibilityDocumentReader.ReadLedger(Path.Combine(RepositoryRoot, "verification", "compatibility"));
        var report = ClassificationReportGenerator.Generate(ledger, CreateSkippedRun(ledger));

        var baselineOnly = report.Requirements.First(value =>
            value.Sources.Any(source => source.ProofUse == ProofUse.BaselineOnly));
        var candidate = report.Requirements.First(value =>
            value.Disposition == RequirementDisposition.CandidateDivergence);
        var declaredSource = ledger.Sources.Single(source =>
            source.SourceId == baselineOnly.Sources.First(source => source.ProofUse == ProofUse.BaselineOnly).SourceId);
        var projectedSource = baselineOnly.Sources.First(source => source.SourceId == declaredSource.SourceId);

        Assert.Multiple(() =>
        {
            Assert.That(
                report.Requirements.Select(static value => value.RequirementId).SequenceEqual(
                    report.Requirements.Select(static value => value.RequirementId).Order(StringComparer.Ordinal)),
                Is.True);
            Assert.That(report.Requirements.All(value =>
                value.Sources.Select(static source => source.SourceId).SequenceEqual(
                    value.Sources.Select(static source => source.SourceId).Order(StringComparer.Ordinal))), Is.True);
            Assert.That(baselineOnly.Sources.Any(source => source.ProofUse == ProofUse.BaselineOnly), Is.True);
            Assert.That(projectedSource.DeclaredCaseCount, Is.EqualTo(declaredSource.CaseCount));
            Assert.That(projectedSource.Provenance, Is.EqualTo(new ClassificationSourceProvenance
            {
                Independence = declaredSource.Provenance.Independence,
                Access = declaredSource.Provenance.Access,
                Currency = declaredSource.Provenance.Currency,
                PinState = declaredSource.Provenance.PinState
            }));
            Assert.That(candidate.Disposition, Is.EqualTo(RequirementDisposition.CandidateDivergence));
            Assert.That(report.Requirements.Any(value => value.OwnerState == OwnerState.Provisional), Is.True);
            Assert.That(report.Requirements.Any(value => value.OwnerState == OwnerState.Unresolved), Is.True);
            Assert.That(report.Requirements.Any(value =>
                value.CaseCount == 0 && value.Disposition == RequirementDisposition.InventoryZero), Is.True);
        });
    }

    [Test]
    public void WriterProducesByteStableCompleteJson()
    {
        var report = ClassificationReportGenerator.Generate(CompatibilityFixture.Create().Ledger, CompatibilityFixture.Create().Run);
        var directory = Path.Combine(Path.GetTempPath(), "arronix-classification-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "first.json");
            var second = Path.Combine(directory, "second.json");

            ClassificationReportWriter.Write(first, report);
            ClassificationReportWriter.Write(second, report);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(first), Is.EqualTo(File.ReadAllText(second)));
                Assert.That(Directory.EnumerateFiles(directory, "*.tmp"), Is.Empty);
                Assert.That(File.ReadAllText(first), Does.Contain("\"format\""));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void WriterCanRemoveAStaleArtifactWhenValidationFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "arronix-classification-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "classification-report.json");
            File.WriteAllText(path, "previously-valid-report");

            ClassificationReportWriter.Delete(path);

            Assert.That(File.Exists(path), Is.False);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void UnknownSourceCannotBeInventedIntoTheProjection()
    {
        var state = CompatibilityFixture.Create();
        var requirements = state.Ledger.Requirements.ToArray();
        requirements[0] = requirements[0] with { SourceIds = ["source.not-declared"] };
        var ledger = state.Ledger with { Requirements = requirements };

        Assert.That(
            () => ClassificationReportGenerator.Generate(ledger, state.Run),
            Throws.TypeOf<CompatibilityDocumentException>());
    }

    private static ClassificationDispositionCount ByDisposition(
        CompatibilityClassificationReport report,
        RequirementDisposition disposition)
        => report.RequirementsByDisposition.Single(value => value.Disposition == disposition);

    private static NUnitTestRun CreateSkippedRun(CompatibilityLedger ledger)
        => new(
        [
            new NUnitProjectResult(
                "classification-report",
                "in-memory",
                ledger.Cases.Select(static value => new NUnitTestCaseResult(
                    "classification-report",
                    value.CaseId,
                    value.CaseId,
                    NUnitTestOutcome.Skipped)).ToArray())
        ]);

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the Arronix repository root.");
        }
    }
}
