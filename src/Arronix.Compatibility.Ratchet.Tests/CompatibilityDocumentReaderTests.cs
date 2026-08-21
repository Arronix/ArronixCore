namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class CompatibilityDocumentReaderTests
{
    private const string SourceA = """
        {"schemaVersion":1,"sourceId":"source.a","title":"A","evidenceClass":"generated-clean-room","artifactState":"current","provenance":{"independence":"independent","access":"normal","currency":"current","pinState":"artifact-pinned"},"proofUse":"eligible","restrictions":["test"]}
        """;

    private const string SourceB = """
        {"schemaVersion":1,"sourceId":"source.b","title":"B","evidenceClass":"repository-regression","artifactState":"historical","provenance":{"independence":"not-established","access":"normal","currency":"historical","pinState":"repository-pinned"},"proofUse":"baseline-only","restrictions":["test"]}
        """;

    [Test]
    public void ParsesCanonicalHyphenatedVocabulary()
    {
        var sources = CompatibilityDocumentReader.ParseSourcesJsonLines(SourceA + "\n" + SourceB);

        Assert.Multiple(() =>
        {
            Assert.That(sources[0].EvidenceClass, Is.EqualTo(EvidenceClass.GeneratedCleanRoom));
            Assert.That(sources[1].ProofUse, Is.EqualTo(ProofUse.BaselineOnly));
        });
    }

    [Test]
    public void RejectsUnknownMembers()
    {
        var json = SourceA.Replace("\"title\":\"A\"", "\"title\":\"A\",\"titel\":\"typo\"", StringComparison.Ordinal);

        Assert.That(
            () => CompatibilityDocumentReader.ParseSourcesJsonLines(json),
            Throws.TypeOf<CompatibilityDocumentException>());
    }

    [Test]
    public void RejectsUnsortedOrDuplicatePrimaryIds()
    {
        Assert.That(
            () => CompatibilityDocumentReader.ParseSourcesJsonLines(SourceB + "\n" + SourceA),
            Throws.TypeOf<CompatibilityDocumentException>());
    }

    [Test]
    public void RejectsBlankJsonLines()
    {
        Assert.That(
            () => CompatibilityDocumentReader.ParseSourcesJsonLines(SourceA + "\n\n" + SourceB),
            Throws.TypeOf<CompatibilityDocumentException>());
    }

    [Test]
    public void AllowsTheCanonicalReplacementLedgerToBeEmpty()
    {
        Assert.That(CompatibilityDocumentReader.ParseReplacementsJsonLines(string.Empty), Is.Empty);
    }

    [Test]
    public void RejectsNumericEnumValues()
    {
        var json = SourceA.Replace("\"proofUse\":\"eligible\"", "\"proofUse\":0", StringComparison.Ordinal);

        Assert.That(
            () => CompatibilityDocumentReader.ParseSourcesJsonLines(json),
            Throws.TypeOf<CompatibilityDocumentException>());
    }
}
