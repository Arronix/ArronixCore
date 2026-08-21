namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class RequiredTestSentinelReaderTests
{
    [Test]
    public void ReadsTheCanonicalEightColumnBinding()
    {
        var path = WriteRegistry(
            "proof.example\tsrc/Example.Tests/Example.Tests.csproj\tExample.Tests.Fixture.ProvesIt"
            + "\tExample.Tests.Fixture\tProvesIt\tsrc/Example.Tests/Fixture.cs\t"
            + CompatibilityDigest.Sha256("source")
            + "\tsrc/Example.Tests/FixtureData.cs="
            + CompatibilityDigest.Sha256("support"));
        try
        {
            var sentinel = RequiredTestSentinelReader.Read(path).Single();

            Assert.Multiple(() =>
            {
                Assert.That(sentinel.Id, Is.EqualTo("proof.example"));
                Assert.That(sentinel.Fixture, Is.EqualTo("Example.Tests.Fixture"));
                Assert.That(sentinel.Method, Is.EqualTo("ProvesIt"));
                Assert.That(sentinel.SupportDocuments, Is.EqualTo(
                [
                    new RequiredTestSupportDocument(
                        "src/Example.Tests/FixtureData.cs",
                        CompatibilityDigest.Sha256("support"))
                ]));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void RejectsAFullNameWhichDoesNotIdentifyTheDeclaredMethod()
    {
        var path = WriteRegistry(
            "proof.example\tsrc/Example.Tests/Example.Tests.csproj\tExample.Tests.Fixture.Impostor"
            + "\tExample.Tests.Fixture\tProvesIt\tsrc/Example.Tests/Fixture.cs\t"
            + CompatibilityDigest.Sha256("source") + "\t-");
        try
        {
            Assert.That(
                () => RequiredTestSentinelReader.Read(path),
                Throws.TypeOf<CompatibilityDocumentException>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void RejectsAnEmptyRegistry()
    {
        var path = WriteRegistry();
        try
        {
            Assert.That(
                () => RequiredTestSentinelReader.Read(path),
                Throws.TypeOf<CompatibilityDocumentException>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteRegistry(params string[] rows)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "arronix-required-tests-" + Guid.NewGuid().ToString("N") + ".tsv");
        File.WriteAllLines(path, [RequiredTestSentinelReader.Header, .. rows]);
        return path;
    }
}
