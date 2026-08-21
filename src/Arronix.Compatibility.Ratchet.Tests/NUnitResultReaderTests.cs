using System.Xml.Linq;

namespace Arronix.Compatibility.Ratchet.Tests;

[TestFixture]
public class NUnitResultReaderTests
{
    [Test]
    public void ReadsLeafCasesAndCountsAcrossNestedSuites()
    {
        var result = NUnitResultReader.Parse(CreateXml(
            "Example.Tests.dll",
            CreateCase("Pass", "Tests.Fixture.Pass", "Passed"),
            CreateCase("Skip", "Tests.Fixture.Skip", "Skipped")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Project, Is.EqualTo("Example.Tests.dll"));
            Assert.That(result.AssemblyPath, Is.EqualTo("Example.Tests.dll"));
            Assert.That(result.Counts, Is.EqualTo(new NUnitCounts(2, 1, 0, 1, 0)));
        });
    }

    [Test]
    public void TreatsTestOutputAsUntrustedDiagnosticsRatherThanEvidence()
    {
        var test = CreateCase("Named case", "Tests.Fixture.Named case", "Passed");
        test.Add(new XElement(
            "output",
            "ARRONIX_PROOF {\"caseId\":\"case.a\",\"semanticDigest\":\"sha256:forged\"}"));

        var observed = NUnitResultReader.Parse(CreateXml("Example.Tests.dll", test)).Tests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(observed.FullName, Is.EqualTo("Tests.Fixture.Named case"));
            Assert.That(observed.Outcome, Is.EqualTo(NUnitTestOutcome.Passed));
        });
    }

    [Test]
    public void PreservesASetNameFullNameIndependentlyOfTheMethodName()
    {
        const string fullName = "Tests.MovieTitleParserTests.t012: title rows";
        var result = NUnitResultReader.Parse(CreateXml(
            "Example.Tests.dll",
            CreateCase("t012: title rows", fullName, "Skipped")));

        Assert.That(CompatibilityDigest.Sha256(result.Tests.Single().FullName), Is.EqualTo(CompatibilityDigest.Sha256(fullName)));
    }

    [Test]
    public void RejectsDeclaredCountsWhichDoNotMatchLeaves()
    {
        var xml = CreateXml("Example.Tests.dll", CreateCase("Pass", "Tests.Fixture.Pass", "Passed"))
            .Replace("total=\"1\"", "total=\"2\"", StringComparison.Ordinal);

        Assert.That(() => NUnitResultReader.Parse(xml), Throws.TypeOf<CompatibilityDocumentException>());
    }

    [Test]
    public void AggregatesSeveralResultFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "arronix-ratchet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "a.xml"), CreateXml("A.Tests.dll", CreateCase("A", "A.Fixture.A", "Passed")));
            File.WriteAllText(Path.Combine(directory, "b.xml"), CreateXml("B.Tests.dll", CreateCase("B", "B.Fixture.B", "Skipped")));

            var run = NUnitResultReader.ReadPaths([directory]);

            Assert.That(run.Counts, Is.EqualTo(new NUnitCounts(2, 1, 0, 1, 0)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void WrapsMalformedResultFilesAsInputErrors()
    {
        var path = Path.Combine(Path.GetTempPath(), "arronix-ratchet-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(path, "<test-run>");
        try
        {
            Assert.That(() => NUnitResultReader.ReadFile(path), Throws.TypeOf<CompatibilityDocumentException>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static string CreateXml(string project, params XElement[] cases)
    {
        var outcomes = cases.Select(static value => (string?)value.Attribute("result")).ToArray();
        return new XDocument(
            new XElement(
                "test-run",
                new XAttribute("total", cases.Length),
                new XAttribute("passed", outcomes.Count(static value => value == "Passed")),
                new XAttribute("failed", outcomes.Count(static value => value == "Failed")),
                new XAttribute("skipped", outcomes.Count(static value => value == "Skipped")),
                new XAttribute("inconclusive", outcomes.Count(static value => value == "Inconclusive")),
                new XElement(
                    "test-suite",
                    new XAttribute("type", "Assembly"),
                    new XAttribute("name", project),
                    new XAttribute("fullname", project),
                    new XElement("test-suite", new XAttribute("type", "TestFixture"), cases))))
            .ToString(SaveOptions.DisableFormatting);
    }

    internal static XElement CreateCase(string name, string fullName, string result)
        => new(
            "test-case",
            new XAttribute("name", name),
            new XAttribute("fullname", fullName),
            new XAttribute("result", result));
}
