#pragma warning disable ARX0021 // Quality contracts are experimental; these tests exercise the axes model.

using System.Linq;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Quality.Families;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Quality;

/// <summary>
/// The grab decision under the policy the video family ships.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every assertion here is now a statement about a policy rather than about data.</b> Under a ladder the
/// ordering was baked into a weight somebody wrote down beside each rung, so a test could only ever check
/// that the numbers were the surveyed numbers — which proves the copy is faithful and proves nothing about
/// whether the behavior is right. Under this model the ordering is a policy, the policy is the family's
/// stated opinion, and a user who disagrees with any line of it moves one chip. So what is asserted is the
/// opinion, and each failure names the axis that produced it.
/// </para>
/// <para>
/// <b>Two ignored tests are superseded rather than deleted quietly, and this note is the record.</b> They
/// asserted that a mislabel fix outranks any number of corrections, and that a repack is the same revision
/// as the correction it replaces — and they were ignored because the contract's own comparison ordered the
/// correction count first and let repack-ness break the residual tie, which is the opposite. That
/// divergence was never a contract question. It is two lines of a precedence list: the surveyed rule is
/// mislabel fixes above corrections with repack absent, the contract's rule was the reverse. The type
/// system has stopped having an opinion, the shipped policy states the surveyed one on its merits — a
/// mislabel fix says the previous file was the wrong content, a correction says it was a worse encode of
/// the right one — and <see cref="RanksAMislabelFixAboveAnyNumberOfCorrections"/> and
/// <see cref="TreatsARepackAsNoUpgradeAtAll"/> below assert exactly what the ignored pair asserted, now
/// green. The third live test that asserted the contract's opposite ordering dies with the contract's
/// comparison, and that is a decision rather than a casualty.
/// </para>
/// </remarks>
[TestFixture]
public class UpgradeDecisionTests
{
    /// <summary>
    /// <b>The pair a single ordered axis cannot hold at once.</b> A direct download and a re-encode of the
    /// same service's stream are interchangeable; a disc's own bitstream is a real upgrade over an encode
    /// of that disc. Both steps are one lossy re-encode, so a model that puts the step on one axis must
    /// choose which of the two to get wrong. Here the cliff sits on the origin axis and the equivalence is
    /// a ceiling on the generation axis: two independent controls for two independent facts, and no
    /// per-title rule anywhere.
    /// </summary>
    [Test]
    public void HoldsTheStreamEquivalenceAndTheDiscCliffOnOnePolicy()
        => Assert.Multiple(() =>
        {
            Assert.That(
                Compare(Point("WEBRip-1080p"), Point("WEBDL-1080p")),
                Is.EqualTo(QualityJudgment.Same),
                "A direct download is not an upgrade over a re-encode of the same stream.");

            Assert.That(
                Compare(Point("WEBDL-1080p"), Point("WEBRip-1080p")),
                Is.EqualTo(QualityJudgment.Same),
                "And it is not a downgrade either.");

            Assert.That(
                Compare(Point("Bluray-1080p"), Point("Remux-1080p")),
                Is.EqualTo(QualityJudgment.Better),
                "A disc's own bitstream is a real upgrade over an encode of that disc.");
        });

    /// <summary>
    /// What is left to the generation axis once the cliff has moved off it: the step that is a real drop
    /// rather than a rounding difference, which is a re-encode of an existing rip.
    /// </summary>
    [Test]
    public void StillSeparatesAReEncodeOfARipFromTheRipItself()
        => Assert.That(
            Compare(Point("Bluray-1080p"), Point("BRRip-1080p")),
            Is.EqualTo(QualityJudgment.Worse));

    /// <summary>
    /// The orderings a ladder has and a model that puts the master-against-encode step on one axis loses:
    /// an untouched transport stream above a broadcast capture, and a disc's own program stream above a rip
    /// of it.
    /// </summary>
    [TestCase("HDTV-1080p", "Raw-HD", QualityJudgment.Better)]
    [TestCase("DVDRip", "DVD", QualityJudgment.Better)]
    [TestCase("SDTV", "Bluray-1080p", QualityJudgment.Better)]
    [TestCase("Bluray-1080p", "SDTV", QualityJudgment.Worse)]
    [TestCase("Bluray-1080p", "Bluray-1080p", QualityJudgment.Same)]
    [TestCase("WEBDL-1080p", "Bluray-1080p", QualityJudgment.Better)]
    public void RanksTheMasterSignalSecond(string held, string candidate, QualityJudgment expected)
        => Assert.That(Compare(Point(held), Point(candidate)), Is.EqualTo(expected));

    /// <summary>
    /// <b>What the first of the two superseded tests asserted, now green.</b> A mislabel fix says the
    /// previous file was the wrong content; a correction says it was a worse encode of the right content.
    /// Wrong content dominates, whatever the correction counts are.
    /// </summary>
    [Test]
    public void RanksAMislabelFixAboveAnyNumberOfCorrections()
    {
        var manyCorrections = Point("Bluray-1080p", corrections: 8);
        var oneMislabelFix = Point("Bluray-1080p", mislabels: 1);

        Assert.Multiple(() =>
        {
            Assert.That(Compare(manyCorrections, oneMislabelFix), Is.EqualTo(QualityJudgment.Better));
            Assert.That(Compare(oneMislabelFix, manyCorrections), Is.EqualTo(QualityJudgment.Worse));
        });
    }

    /// <summary>
    /// <b>What the second of the two superseded tests asserted, now green.</b> A repack is the same encode
    /// packaged again. It is not a fidelity change, so it is absent from the shipped order entirely and
    /// re-downloading for one is something this platform will not do.
    /// </summary>
    [Test]
    public void TreatsARepackAsNoUpgradeAtAll()
    {
        var original = Point("Bluray-1080p", corrections: 1);
        var repack = Point("Bluray-1080p", corrections: 1, repacked: true);

        Assert.Multiple(() =>
        {
            Assert.That(Compare(original, repack), Is.EqualTo(QualityJudgment.Same));
            Assert.That(
                MoviesDeclaration.Policy.Precedence.Select(entry => entry.Axis.Value),
                Does.Not.Contain(nameof(VideoQuality.Repacked)));
        });
    }

    /// <summary>
    /// A correction of the held quality is an upgrade, and a better master with no correction still beats a
    /// correction of a worse one — because the correction counts sit beneath the master signal rather than
    /// beside it.
    /// </summary>
    [Test]
    public void PrefersABetterSignalOverACorrectionOfAWorseOne()
    {
        var correctedBroadcast = Point("HDTV-720p", corrections: 3, mislabels: 1);
        var disc = Point("Bluray-1080p");

        Assert.Multiple(() =>
        {
            Assert.That(Compare(correctedBroadcast, disc), Is.EqualTo(QualityJudgment.Better));
            Assert.That(Compare(disc, correctedBroadcast), Is.EqualTo(QualityJudgment.Worse));
            Assert.That(
                Compare(Point("Bluray-1080p"), Point("Bluray-1080p", corrections: 1)),
                Is.EqualTo(QualityJudgment.Better));
        });
    }

    /// <summary>
    /// The shipped order, stated so that a change to it is a decision somebody made rather than a diff
    /// nobody read.
    /// </summary>
    [Test]
    public void OrdersFiveAxesAndSaysWhichFirst()
        => Assert.That(
            MoviesDeclaration.Policy.Precedence.Select(entry => entry.Axis.Value),
            Is.EqualTo(new[]
            {
                nameof(VideoQuality.Resolution),
                nameof(VideoQuality.Origin),
                nameof(VideoQuality.Generation),
                nameof(VideoQuality.Mislabels),
                nameof(VideoQuality.Corrections),
            }));

    /// <summary>
    /// <b>The cutoff is a conjunction over axes, not one cell of a cross-product.</b> "Good enough at 1080
    /// lines with at most one re-encode" is two independent floors, and it is a sentence a ladder cannot
    /// say at all: naming one of thirty rungs as a cutoff silently also decides something about every other
    /// source at that resolution.
    /// </summary>
    [Test]
    public void StopsSearchingAtTwoIndependentFloors()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Policy.IsGoodEnough(Point("WEBDL-1080p")), Is.True);
            Assert.That(MoviesDeclaration.Policy.IsGoodEnough(Point("Bluray-1080p")), Is.True);
            Assert.That(MoviesDeclaration.Policy.IsGoodEnough(Point("HDTV-720p")), Is.False);
            Assert.That(
                MoviesDeclaration.Policy.IsGoodEnough(Point("BRRip-1080p")),
                Is.False,
                "Enough lines, one re-encode too many.");
        });

    [Test]
    public void GrabsAnythingWhenNothingIsHeld()
        => Assert.That(
            MoviesDeclaration.Policy.Decide(null, Point("SDTV")).Verdict,
            Is.EqualTo(GrabVerdict.Grab));

    /// <summary>
    /// Once the held file satisfies the cutoff, a genuine upgrade is still declined — and the decision says
    /// so in its own words rather than pretending the candidate was worse.
    /// </summary>
    [Test]
    public void DeclinesAGenuineUpgradeOnceTheHeldFileIsGoodEnough()
    {
        var decision = MoviesDeclaration.Policy.Decide(Point("Bluray-1080p"), Point("Remux-1080p"));

        Assert.Multiple(() =>
        {
            Assert.That(
                Compare(Point("Bluray-1080p"), Point("Remux-1080p")),
                Is.EqualTo(QualityJudgment.Better),
                "It is an upgrade.");
            Assert.That(decision.Verdict, Is.EqualTo(GrabVerdict.AlreadyGoodEnough), "And it is not taken.");
        });
    }

    [Test]
    public void GrabsAnUpgradeBelowTheCutoff()
        => Assert.Multiple(() =>
        {
            Assert.That(
                MoviesDeclaration.Policy.Decide(Point("HDTV-720p"), Point("WEBDL-1080p")).Verdict,
                Is.EqualTo(GrabVerdict.Grab));

            Assert.That(
                MoviesDeclaration.Policy.Decide(Point("HDTV-720p"), Point("WEBRip-1080p")).Verdict,
                Is.EqualTo(GrabVerdict.Grab),
                "A broadcast capture is below a stream capture, so more lines and a better master is both.");

            Assert.That(
                MoviesDeclaration.Policy.Decide(Point("WEBDL-720p"), Point("WEBRip-720p")).Verdict,
                Is.EqualTo(GrabVerdict.NotAnUpgrade),
                "A sideways move within the stream equivalence is not an upgrade.");
        });

    /// <summary>
    /// <b>A claim never outranks a measurement, and this is the loop that rule exists to close.</b> Import a
    /// release claiming a raster, measure something smaller, and the identical release reappears on the next
    /// sweep — under provenance-blind comparison it is an upgrade every time, forever. Asserted over
    /// several passes because "it terminates" is the claim, and one pass cannot show termination.
    /// </summary>
    [Test]
    public void NeverReDownloadsOnAClaimItHasAlreadyMeasured()
    {
        var candidate = MoviesDeclaration.Quality.Read(new ReleaseEvidence
        {
            Title = "Movie.Name.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTG",
            SourceToken = EvidenceSourceTokens.WebDownload,
            StatedResolution = 1080,
        });

        var imported = MoviesDeclaration.Quality.Read(new ReleaseEvidence
        {
            Title = "Movie.Name.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264-NTG",
            SourceToken = EvidenceSourceTokens.WebDownload,
            StatedResolution = 1080,
            Probe = new MediaProbe { Height = 720 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                MoviesDeclaration.Policy.Compare(imported, candidate),
                Is.EqualTo(QualityJudgment.Better),
                "The ordering is provenance-blind on purpose; folding the rule into it would break transitivity.");

            for (var pass = 1; pass <= 5; pass++)
            {
                Assert.That(
                    MoviesDeclaration.Policy.Decide(imported, candidate).Verdict,
                    Is.EqualTo(GrabVerdict.NotAnUpgrade),
                    $"pass {pass}");
            }
        });
    }

    /// <summary>
    /// A camera recording of a projection and an unfinished edit are not the film, and the refusal names
    /// the axis that produced it rather than reporting a rung number nobody can act on.
    /// </summary>
    [TestCase("CAM")]
    [TestCase("WORKPRINT")]
    public void RefusesWhatIsNotTheFilm(string spelling)
    {
        var decision = MoviesDeclaration.Policy.Decide(null, Point(spelling));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(GrabVerdict.Refused));
            Assert.That(
                decision.Reason,
                Is.EqualTo("A camera recording of a projection and an unfinished edit are not the film."),
                "A refusal says why in the words whoever wrote the requirement chose, not in an axis name.");
        });
    }

    /// <summary>
    /// The one place a preference this platform holds is written down for a person to read, rendered from
    /// the policy rather than maintained beside it.
    /// </summary>
    [Test]
    public void SaysWhatItPrefersInWordsAPersonCanRead()
    {
        var description = MoviesDeclaration.Policy.Describe();

        Assert.Multiple(() =>
        {
            Assert.That(description, Does.Contain("resolution"), "It names the axis that leads.");
            Assert.That(description, Does.Contain("2160 lines"), "And where that axis stops mattering.");
            Assert.That(description, Does.Contain("origin"), "And what decides next.");
            Assert.That(
                description,
                Does.Contain("Never re-download on a claim we have already measured"),
                "Including the one behavior a user would otherwise have to discover.");
        });
    }

    private static QualityJudgment Compare(QualityPoint held, QualityPoint candidate) =>
        MoviesDeclaration.Policy.Compare(held, candidate);

    /// <summary>
    /// Builds a point from the community's own word for it, so a test row reads the way a user would say
    /// it. The word is parsed back into a point by the family's own renderer, which is the one place a
    /// rendered string is ever read — and what it produces is a claim of release-title strength, never a
    /// measurement.
    /// </summary>
    /// <param name="label">The community's word.</param>
    /// <param name="corrections">How many corrections to add.</param>
    /// <param name="mislabels">How many mislabel fixes to add.</param>
    /// <param name="repacked">Whether the issue is a repack of the same encode.</param>
    /// <returns>The point.</returns>
    private static QualityPoint Point(
        string label,
        int corrections = 0,
        int mislabels = 0,
        bool repacked = false)
    {
        var facts = Facts(label);

        return MoviesDeclaration.Quality.Project(new VideoQuality
        {
            Origin = facts.Origin,
            Generation = facts.Generation,
            Resolution = facts.Resolution,
            DynamicRange = facts.DynamicRange,
            Audio = facts.Audio,
            Codec = facts.Codec,
            FrameRate = facts.FrameRate,
            Packaging = facts.Packaging,
            Flaws = facts.Flaws,
            Corrections = Evidence<int>.From(corrections, EvidenceSource.ReleaseTitle),
            Mislabels = Evidence<int>.From(mislabels, EvidenceSource.ReleaseTitle),
            Repacked = Evidence<Repackaging>.From(
                repacked ? Repackaging.Repacked : Repackaging.Original,
                EvidenceSource.ReleaseTitle),
        });
    }

    private static VideoQuality Facts(string label) => label switch
    {
        "SDTV" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.Broadcast),
            Generation = Count(1),
            Resolution = Count(480),
        },
        "HDTV-720p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.Broadcast),
            Generation = Count(1),
            Resolution = Count(720),
        },
        "HDTV-1080p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.Broadcast),
            Generation = Count(1),
            Resolution = Count(1080),
        },
        "Raw-HD" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.BroadcastBitstream),
            Generation = Count(0),
            Resolution = Count(1080),
        },
        "WEBDL-1080p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.Stream),
            Generation = Count(0),
            Resolution = Count(1080),
        },
        "WEBRip-1080p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.Stream),
            Generation = Count(1),
            Resolution = Count(1080),
        },
        "WEBDL-720p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.Stream),
            Generation = Count(0),
            Resolution = Count(720),
        },
        "WEBRip-720p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.Stream),
            Generation = Count(1),
            Resolution = Count(720),
        },
        "Bluray-1080p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.HighDefinitionDisc),
            Generation = Count(1),
            Resolution = Count(1080),
        },
        "BRRip-1080p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.HighDefinitionDisc),
            Generation = Count(2),
            Resolution = Count(1080),
        },
        "Remux-1080p" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.HighDefinitionDiscBitstream),
            Generation = Count(0),
            Resolution = Count(1080),
        },
        "DVD" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.StandardDefinitionDiscBitstream),
            Generation = Count(0),
            Resolution = Count(480),
        },
        "DVDRip" => new VideoQuality
        {
            Origin = Origin(VideoOrigin.StandardDefinitionDisc),
            Generation = Count(1),
            Resolution = Count(480),
        },
        "CAM" => new VideoQuality { Origin = Origin(VideoOrigin.CameraCapture), Generation = Count(1) },
        "WORKPRINT" => new VideoQuality { Origin = Origin(VideoOrigin.Workprint) },
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "No fixture point spells that."),
    };

    private static Evidence<VideoOrigin> Origin(VideoOrigin origin) =>
        Evidence<VideoOrigin>.From(origin, EvidenceSource.ReleaseTitle);

    private static Evidence<int> Count(int value) =>
        Evidence<int>.From(value, EvidenceSource.ReleaseTitle);
}
