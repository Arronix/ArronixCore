namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class RepositorySnapshotTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ArtifactIdentityRequiresAnExistingFileWithThePinnedDigest(bool createWrongFile)
    {
        var root = Path.Combine(Path.GetTempPath(), "arronix-source-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string locator = "evidence/source.json";
            if (createWrongFile)
            {
                Directory.CreateDirectory(Path.Combine(root, "evidence"));
                File.WriteAllText(Path.Combine(root, locator), "different artifact");
            }

            var state = CompatibilityFixture.Create();
            var sources = state.Ledger.Sources.ToArray();
            sources[0] = sources[0] with
            {
                Locator = locator,
                Revision = new SourceRevision
                {
                    Kind = RevisionKind.ArtifactSha256,
                    Value = CompatibilityDigest.Sha256("expected artifact")[7..]
                }
            };
            var ledger = state.Ledger with { Sources = sources };

            var snapshot = RepositorySnapshot.Capture(root, ledger);

            Assert.That(snapshot.SourceIdentityMatches![sources[0].SourceId], Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ArtifactIdentityAcceptsTheLocatedPinnedContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "arronix-source-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "evidence"));
        try
        {
            const string locator = "evidence/source.json";
            const string content = "expected artifact";
            File.WriteAllText(Path.Combine(root, locator), content);
            var state = CompatibilityFixture.Create();
            var sources = state.Ledger.Sources.ToArray();
            sources[0] = sources[0] with
            {
                Locator = locator,
                Revision = new SourceRevision
                {
                    Kind = RevisionKind.ArtifactSha256,
                    Value = CompatibilityDigest.Sha256(content)[7..]
                }
            };
            var ledger = state.Ledger with { Sources = sources };

            var snapshot = RepositorySnapshot.Capture(root, ledger);

            Assert.That(snapshot.SourceIdentityMatches![sources[0].SourceId], Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
