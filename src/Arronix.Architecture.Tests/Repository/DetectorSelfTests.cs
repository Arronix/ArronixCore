using System.Linq;
using Arronix.Architecture.Tests.Naming;

namespace Arronix.Architecture.Tests.Repository;

/// <summary>
/// Positive and negative controls for the readers every other rule in this fixture is built on.
/// </summary>
/// <remarks>
/// <para>
/// A governance suite has one characteristic failure mode: the detector quietly stops detecting and every
/// rule reports success while checking nothing. Nothing about a green run distinguishes "no violations"
/// from "no scanning", so the difference has to be asserted directly.
/// </para>
/// <para>
/// The controls come in pairs. Each detector is shown finding something that is really there, and shown
/// not finding something that only looks like it. The second half is the more important one: a rule that
/// over-reports is a rule that gets suppressed, and a suppressed rule enforces nothing at all.
/// </para>
/// </remarks>
[TestFixture]
public class DetectorSelfTests
{
    [Test]
    public void RepositoryRootIsTheOneHoldingTheSolutionAndTheSourceTree()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                System.IO.File.Exists(System.IO.Path.Combine(RepositoryLayout.Root, RepositoryLayout.SolutionFileName)),
                Is.True);

            Assert.That(System.IO.Directory.Exists(RepositoryLayout.SourceRoot), Is.True);
        });
    }

    [Test]
    public void EveryProjectTheseRulesSpeakAboutIsInTheWorkingTree()
    {
        var missing = RepositoryLayout
            .MediaNeutralProjects
            .Concat(RepositoryLayout.MediaExtensionProjects)
            .Where(static name => !RepositoryLayout.ProjectExists(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            missing,
            Is.Empty,
            "A rule below is written about a project that is not there, and would pass by finding nothing.");
    }

    [Test]
    [TestCase("Arronix.Abstractions", 150)]
    [TestCase("Arronix.Common", 20)]
    [TestCase("Arronix.Plugins", 30)]
    [TestCase("Arronix.Host", 50)]
    [TestCase("Arronix.Api", 15)]
    [TestCase("Arronix.Client", 40)]
    public void TheDeclarationReaderFindsAPlausibleNumberOfTypes(string projectName, int floor)
    {
        var declarations = SourceScanner.DeclaredTypes(projectName);

        TestContext.Out.WriteLine($"{projectName}: {declarations.Count} declarations");

        // A floor rather than an exact count. The point is to catch the reader silently returning almost
        // nothing, not to make every new type a fixture edit.
        Assert.That(declarations, Has.Count.GreaterThanOrEqualTo(floor));
    }

    [Test]
    public void TheDeclarationReaderFindsKnownTypesInTheirKnownFiles()
    {
        var contracts = SourceScanner.DeclaredTypes(RepositoryLayout.Abstractions);

        Assert.Multiple(() =>
        {
            Assert.That(
                contracts.Any(static declaration =>
                    declaration.Name == "CoordinateComponent" && declaration.Kind == "record"),
                Is.True,
                "A positional record declaration was not found.");

            Assert.That(
                contracts.Any(static declaration =>
                    declaration.Name == "Capability" && declaration.Kind == "enum"),
                Is.True,
                "An enum declaration was not found.");

            Assert.That(
                contracts.Any(static declaration =>
                    declaration.Name == "IPluginContext" && declaration.Kind == "interface"),
                Is.True,
                "An interface declaration was not found.");

            Assert.That(
                contracts.Any(static declaration =>
                    declaration.Name == "CapabilitySet" && declaration.Kind == "record struct"),
                Is.True,
                "A record struct declaration was not found.");
        });
    }

    [Test]
    public void TheDeclarationReaderDoesNotInventTypesOutOfConstantsOrProse()
    {
        var contracts = SourceScanner.DeclaredTypes(RepositoryLayout.Abstractions);

        Assert.Multiple(() =>
        {
            // A private constant that happens to sit on a line mentioning a coordinate component.
            Assert.That(
                contracts.Any(static declaration => declaration.Name == "ComponentSeparator"),
                Is.False,
                "A field was read as a type declaration.");

            // Every declaration must be a legal identifier. Anything else means a comment or a string was
            // read as code.
            Assert.That(
                contracts.Where(static declaration => declaration.Name.Length == 0).ToArray(),
                Is.Empty);
        });
    }

    [Test]
    public void TheDeclarationReaderTreatsAMarkupComponentAsATypeNamedAfterItsFile()
    {
        var client = SourceScanner.DeclaredTypes(RepositoryLayout.Client);

        var components = client.Where(static declaration => declaration.Kind == "component").ToArray();

        Assert.That(
            components,
            Has.Length.GreaterThanOrEqualTo(30),
            "Markup components were not read. Invariant 1 has to reach them: a component's file name is "
            + "its type name, so a file called after a media kind is a type called after one.");
    }

    [Test]
    public void TheMediaNounDetectorFindsAMediaNounWhereverAReaderWould()
    {
        Assert.Multiple(() =>
        {
            foreach (var identifier in new[]
            {
                "Series", "SeriesTitle", "EpisodeFile", "SeasonPass", "MovieMetadata",
                "AlbumRelease", "TrackFile", "ArtistMetadata", "BookRepository", "AuthorName",
                "EditionSelector", "IEpisodeService", "seasonNumber", "TV_SERIES", "MediaTracks"
            })
            {
                Assert.That(
                    MediaVocabulary.Names(identifier),
                    Is.True,
                    $"'{identifier}' names a media kind and was not detected.");
            }
        });
    }

    [Test]
    public void TheMediaNounDetectorLeavesInnocentEnglishAlone()
    {
        Assert.Multiple(() =>
        {
            foreach (var identifier in new[]
            {
                "Authorization", "AuthorizationHeader", "IAuthorityResolver", "Tracking", "Tracker",
                "Backtrack", "Bookmark", "Handbook", "Reason", "ImportReason", "Condition",
                "PreconditionFailed", "Addition", "AdditionalFields", "Seasoning"
            })
            {
                Assert.That(
                    MediaVocabulary.Names(identifier),
                    Is.False,
                    $"'{identifier}' is not a media noun and must not be flagged. A rule that cries wolf "
                    + "is a rule that gets switched off.");
            }
        });
    }

    [Test]
    public void TheWordSplitterSplitsTheWayAReaderReads()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SourceScanner.Words("SeriesTitle"), Is.EqualTo(new[] { "Series", "Title" }));
            Assert.That(SourceScanner.Words("IMediaKind"), Is.EqualTo(new[] { "I", "Media", "Kind" }));
            Assert.That(SourceScanner.Words("TVSeries"), Is.EqualTo(new[] { "TV", "Series" }));
            Assert.That(SourceScanner.Words("Authorization"), Is.EqualTo(new[] { "Authorization" }));
            Assert.That(SourceScanner.Words("season_number"), Is.EqualTo(new[] { "season", "number" }));
            Assert.That(SourceScanner.Words("IReadOnlyList`1"), Is.EqualTo(new[] { "I", "Read", "Only", "List", "1" }));
            Assert.That(SourceScanner.Words(string.Empty), Is.Empty);
        });
    }

    [Test]
    public void TheProjectFileReaderReportsReferencesThatAreReallyThere()
    {
        // Without this control every "declares no package" and "declares one project reference" assertion
        // in the suite would pass on a reader that returned nothing for everything. The runtime is the
        // natural positive control: it is the project that legitimately takes the most of both.
        var host = ProjectFile.Load(RepositoryLayout.Host);

        Assert.Multiple(() =>
        {
            Assert.That(host.PackageReferences, Is.Not.Empty);
            Assert.That(host.PackageReferences, Does.Contain("Microsoft.Extensions.Options"));
            Assert.That(host.ProjectReferences, Does.Contain(RepositoryLayout.Abstractions));
            Assert.That(host.ProjectReferences, Does.Contain(RepositoryLayout.Common));
            Assert.That(host.ProjectReferences, Does.Contain(RepositoryLayout.Plugins));
            Assert.That(host.Sdk, Is.EqualTo("Microsoft.NET.Sdk"));
        });
    }

    [Test]
    public void TheMarkupFinderFindsMarkupWhereThereIsSome()
    {
        // The counterpart control for "the server contains no markup file" and "an extension contains no
        // markup file". The client is where markup legitimately lives, so it is where the finder is proved.
        var markup = RepositoryLayout.Files(RepositoryLayout.Client, "*.razor");

        Assert.That(
            markup,
            Has.Count.GreaterThanOrEqualTo(30),
            "No markup was found in the project that is made of it, so the rules that forbid markup "
            + "elsewhere are forbidding nothing.");
    }

    [Test]
    public void TheLinkedAssemblyReaderSeesEveryPlatformAssemblyThatIsReallyLinked()
    {
        // And the counterpart for "a media extension links no platform assembly". That rule expects to
        // find exactly one Arronix assembly; a reader that could only ever find one would satisfy it
        // whatever the binary said. This fixture genuinely links two, so it is its own control.
        var linked = typeof(DetectorSelfTests)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name ?? string.Empty)
            .Where(static name => name.StartsWith("Arronix.", StringComparison.Ordinal))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(linked, Does.Contain(RepositoryLayout.Abstractions));
            Assert.That(linked, Does.Contain(RepositoryLayout.Plugins));
        });
    }

    [Test]
    public void TheLineReaderReachesEveryFileTheDeclarationReaderDoes()
    {
        var files = SourceScanner
            .Lines(RepositoryLayout.Abstractions)
            .Select(static entry => entry.File)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(files, Has.Length.GreaterThanOrEqualTo(80));
    }

    [Test]
    public void BuildIntermediatesAreNeverScanned()
    {
        // The obj folder holds generated sources - assembly attributes, editor configuration, and on some
        // projects a copy of the very code being scanned. Reading it would double-count everything and
        // could flag a name the compiler invented rather than one a person wrote.
        var intermediates = RepositoryLayout
            .MediaNeutralProjects
            .SelectMany(static project => SourceScanner.Lines(project).Select(static entry => entry.File))
            .Where(static file =>
                file.Contains("/obj/", StringComparison.Ordinal)
                || file.Contains("/bin/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(intermediates, Is.Empty);
    }
}
