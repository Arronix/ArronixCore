#pragma warning disable ARX0021 // Quality contracts are experimental; these tests exercise the axes model.

using System.Linq;
using Arronix.Abstractions.Quality;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Quality;

/// <summary>
/// The quality corpus, run against the shared video family.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus is no longer a title-parsing fixture, and that closes its oldest defect.</b> Sixty of its
/// rows name a season and an episode with no year, so the movie title patterns correctly decline them, and
/// the end-to-end assertion over the whole corpus had to stay ignored. It was never a title corpus: the
/// quality scan reads tokens out of any text, which is precisely the evidence that these rows are not movie
/// semantics. Under this model a quality expectation does not go through title parsing at all — the family
/// reads evidence, and the evidence comes from a scan that neither knows nor cares what a movie is. So the
/// row that used to be ignored runs over every case below, and a second fixture proves separately that the
/// parse engine reaches the same answer for the rows a movie title pattern claims.
/// </para>
/// <para>
/// <b>What the expectation column says now.</b> It is the family's own rendering of the point read, in the
/// same community vocabulary a rung name used. The difference is where the string comes from: a rung name
/// was chosen from a fixed list of thirty and a rendering is derived from what was actually read, so a
/// combination nobody tabulated renders truthfully instead of being rounded to the nearest row that exists.
/// </para>
/// </remarks>
[TestFixture]
public class QualityParserTests
{
    /// <summary>
    /// <b>The assertion the whole quality section exists to earn, and it is no longer ignored.</b> Every
    /// declared corpus case, read by the shared family and rendered in the community's words — with the
    /// residue named case by case rather than dropped.
    /// </summary>
    [Test]
    public void ReadsEveryCorpusCaseOntoItsDeclaredQuality()
    {
        var wrong = MoviesDeclaration.ExpectedQualities
            .Where(row => !QualityDivergenceRegister.All.Contains(row.Key))
            .Select(row => (row.Key, Expected: row.Value, Actual: Render(row.Key)))
            .Where(row => !string.Equals(row.Actual, row.Expected, StringComparison.Ordinal))
            .Select(row => $"'{row.Key}' declared '{row.Expected}', read '{row.Actual}'")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(wrong, Is.Empty);
    }

    /// <summary>
    /// And the same answer through the engine the host actually builds, for every corpus row a movie title
    /// pattern claims. This is what proves the wiring rather than the family: evidence scanned by the host,
    /// this kind's guards attached, the family's reading, the family's rendering.
    /// </summary>
    [Test]
    public void ReachesTheSameQualityThroughTheParseEngine()
    {
        var wrong = MoviesDeclaration.ExpectedQualities
            .Where(row => !QualityDivergenceRegister.All.Contains(row.Key))
            .Select(row => (row.Key, row.Value, Parsed: MoviesEngines.Parse(row.Key)))
            .Where(row => row.Parsed is not null)
            .Where(row => !string.Equals(row.Parsed!.Quality, row.Value, StringComparison.Ordinal))
            .Select(row => $"'{row.Key}' declared '{row.Value}', parsed '{row.Parsed!.Quality ?? "<none>"}'")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(wrong, Is.Empty);
    }

    /// <summary>
    /// <b>The register is the escape hatch, so the escape hatch is what gets bounded.</b> Three bounds, and
    /// each closes a different way of making the assertion above meaningless: an entry that names no real
    /// corpus row would be a typo silently exempting nothing, an entry whose row now agrees would be an
    /// exemption outliving its reason, and a register allowed to grow without limit would eventually
    /// exempt the corpus from itself.
    /// </summary>
    [Test]
    public void KeepsTheDivergenceRegisterHonestAndBounded()
    {
        var unknownRows = QualityDivergenceRegister.All
            .Where(title => !MoviesDeclaration.ExpectedQualities.ContainsKey(title))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var noLongerDiverging = QualityDivergenceRegister.All
            .Where(MoviesDeclaration.ExpectedQualities.ContainsKey)
            .Where(title => string.Equals(
                Render(title),
                MoviesDeclaration.ExpectedQualities[title],
                StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(unknownRows, Is.Empty, "A register entry names a corpus row.");
            Assert.That(noLongerDiverging, Is.Empty, "A register entry that agrees has stopped being one.");
            Assert.That(
                QualityDivergenceRegister.All,
                Has.Count.LessThanOrEqualTo(QualityDivergenceRegister.Cap),
                "A cap is what stops the escape hatch swallowing the test.");
            Assert.That(
                (double)QualityDivergenceRegister.All.Count / MoviesDeclaration.ExpectedQualities.Count,
                Is.LessThan(0.15),
                "If the residue is a fifth of the corpus the model has not been validated, it has been "
                + "described.");
        });
    }

    /// <summary>
    /// Every register entry carries a reason a reader can act on, because an entry without one is an
    /// exemption nobody can ever retire.
    /// </summary>
    [Test]
    public void GivesEveryRegisteredDivergenceAReason()
        => Assert.That(
            QualityDivergenceRegister.UnknownToTheScanner.Values
                .Concat(QualityDivergenceRegister.UnseparableByTheEvidence.Values),
            Has.All.Not.Empty);

    /// <summary>
    /// <b>The residue is a scanner-coverage problem and one modelling limit, and the shape of the register
    /// says so.</b> Twenty-one rows close by teaching the shared evidence scan a spelling; two need the
    /// scan to report whether a codec was named as a format or as an encoder. Not one of them closes by
    /// changing an axis, a policy or a rendering rule, which is the claim worth pinning: if the residue
    /// were the model's shape, this ratio would be the other way round.
    /// </summary>
    [Test]
    public void AttributesTheWholeResidueToEvidenceRatherThanToTheModel()
        => Assert.Multiple(() =>
        {
            Assert.That(QualityDivergenceRegister.UnseparableByTheEvidence, Has.Count.LessThanOrEqualTo(2));
            Assert.That(
                QualityDivergenceRegister.UnknownToTheScanner.Count,
                Is.GreaterThan(QualityDivergenceRegister.UnseparableByTheEvidence.Count * 5));
        });

    /// <summary>
    /// The corpus is the highest-value asset the surveyed application has, and re-expressing its
    /// expectations must not be a way of quietly shedding rows.
    /// </summary>
    [Test]
    public void CarriesTheWholeSurveyedQualityCorpus()
        => Assert.That(
            MoviesDeclaration.ExpectedQualities, Has.Count.GreaterThanOrEqualTo(160),
            "A conversion that sheds the corpus has not preserved behavior, it has stopped checking for it.");

    /// <summary>
    /// The generic spellings a user types into a profile, in each of the four separators a release name
    /// uses. The cheapest proof that separator normalization happens before the source scan rather than
    /// after it.
    /// </summary>
    [TestCase("SD TV", "SDTV")]
    // A broadcast token stating no resolution renders the standard-definition word whichever separator it
    // used, which is what every unspaced spelling in the corpus already read as.
    [TestCase("480p WEB-DL", "WEBDL-480p")]
    [TestCase("HD TV", "SDTV")]
    [TestCase("1080p HD TV", "HDTV-1080p")]
    [TestCase("2160p HD TV", "HDTV-2160p")]
    [TestCase("720p WEB-DL", "WEBDL-720p")]
    [TestCase("1080p WEB-DL", "WEBDL-1080p")]
    [TestCase("2160p WEB-DL", "WEBDL-2160p")]
    [TestCase("720p BluRay", "Bluray-720p")]
    [TestCase("1080p BluRay", "Bluray-1080p")]
    [TestCase("2160p BluRay", "Bluray-2160p")]
    [TestCase("1080p Remux", "Remux-1080p")]
    [TestCase("2160p Remux", "Remux-2160p")]
    public void ReadsTheSameQualityWhicheverSeparatorTheReleaseUsed(string spelling, string quality)
    {
        foreach (var separator in new[] { '-', '.', ' ', '_' })
        {
            Assert.That(Render("Some Movie 2018 " + spelling.Replace(' ', separator)), Is.EqualTo(quality));
        }
    }

    /// <summary>
    /// <b>Truthful-but-novel labels: the renderer's stance, asserted rather than argued.</b> A ladder had
    /// nowhere to put a disc bitstream below 1080 lines, so it renamed one to the nearest rung it did have
    /// and renamed an intermediate raster down to a familiar one. Both renames discarded something the
    /// release stated. What replaces the first is not a rename at all — it is a computed size gate over the
    /// real file, which tests the plausibility claim the rename was asserting.
    /// </summary>
    [TestCase("Movie.Name.2011.BluRay.480i.DD.2.0.AVC.REMUX-FraMeSToR", "Remux-480p")]
    [TestCase("Movie.Hunter.2018.720p.Blu-ray.Remux.AVC.FLAC.2.0-SiCFoI", "Remux-720p")]
    [TestCase("[SubsPlease] Movie Title (540p) [AB649D32].mkv", "WEBDL-540p")]
    public void RendersTheTruthRatherThanTheNearestRungThatExisted(string releaseTitle, string quality)
        => Assert.That(Render(releaseTitle), Is.EqualTo(quality));

    /// <summary>
    /// A release whose origin nothing states still renders the resolution it does state. Every rule keyed
    /// on the source word leaves a known fact on the floor and then says "Unknown" over it.
    /// </summary>
    [TestCase("[NOGRP][国漫][诛仙][Movie Title 2022][19][HEVC][GB][4K]", "2160p")]
    public void RendersAKnownResolutionEvenWhenNothingNamedTheSource(string releaseTitle, string quality)
        => Assert.That(Render(releaseTitle), Is.EqualTo(quality));

    /// <summary>
    /// Nothing read means nothing claimed. A guess that is right often enough to be worth something is
    /// still a guess, and the model declines to make it — recorded as a stated loss rather than argued away.
    /// </summary>
    [TestCase("Some.Movie.S02E15")]
    [TestCase("Movie Name - 11x11 - Quickie")]
    [TestCase("Movie.Title.S01E01.The.Web.MT-dd")]
    [TestCase("Movie.2008.X264-DIMENSION")]
    public void RendersNothingWhenTheTitleClaimsNothing(string releaseTitle)
        => Assert.That(Render(releaseTitle), Is.EqualTo("Unknown"));

    /// <summary>
    /// At full detail the revision markers ride behind the standard label, space-joined with empty parts
    /// elided — which is what a file name spells.
    /// </summary>
    [TestCase("Movie.Title.2018.720p.HDTV.x264-aAF", "HDTV-720p")]
    [TestCase("Movie.Title.2018.PROPER.720p.HDTV.x264-aAF", "HDTV-720p Proper")]
    [TestCase("Movie.Title.2018.REPACK2.720p.HDTV.x264-aAF", "HDTV-720p Proper")]
    [TestCase("Movie.Title.2018.REAL.PROPER.720p.HDTV.x264-aAF", "HDTV-720p Proper REAL")]
    public void SpellsTheRevisionOnlyAtFullDetail(string releaseTitle, string full)
        => Assert.Multiple(() =>
        {
            Assert.That(Render(releaseTitle, QualityLabelDetail.Full), Is.EqualTo(full));
            Assert.That(Render(releaseTitle), Does.Not.Contain("Proper"));
        });

    /// <summary>
    /// A label is produced from a point and is never read back for a comparison — but it must survive the
    /// round trip, because a stored string and a pasted profile are both read back into points.
    /// </summary>
    [Test]
    public void ReadsEveryRenderedLabelBackIntoAPoint()
    {
        var unreadable = MoviesDeclaration.ExpectedQualities.Values
            .Distinct(StringComparer.Ordinal)
            .Where(label => !MoviesDeclaration.Quality.TryParseLabel(label, out _))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(unreadable, Is.Empty);
    }

    /// <summary>
    /// <b>Everything read back from a stored string is a claim, never a measurement.</b> Otherwise a point
    /// rebuilt from a file name would outrank the probe of the file it names.
    /// </summary>
    [Test]
    public void ReadsAStoredLabelBackAsAClaimAndNeverAsAMeasurement()
    {
        Assert.That(MoviesDeclaration.Quality.TryParseLabel("Bluray-1080p", out var point), Is.True);

        Assert.That(
            point.Readings.Where(reading => reading.IsKnown).Select(reading => reading.Source),
            Has.All.EqualTo(EvidenceSource.ReleaseTitle));
    }

    /// <summary>Reads one release title and renders what the family read.</summary>
    /// <param name="releaseTitle">The release title.</param>
    /// <param name="detail">How much of the point to spell.</param>
    /// <returns>The rendering.</returns>
    private static string Render(
        string releaseTitle,
        QualityLabelDetail detail = QualityLabelDetail.Standard) =>
        MoviesDeclaration.Quality.Label(
            MoviesDeclaration.Quality.Project(AxisReadingTests.Read(releaseTitle)),
            detail);
}
