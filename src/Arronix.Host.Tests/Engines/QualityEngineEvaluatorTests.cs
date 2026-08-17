using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Quality;
using FluentAssertions;

// The shape contracts are experimental (ARX0013).
#pragma warning disable ARX0013

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The declared-ladder quality evaluator: weight first, revision second, cutoff by weight alone,
/// families never compared — the surveyed rules, produced by one engine instead of four models.
/// </summary>
[TestFixture]
internal sealed class QualityEngineEvaluatorTests
{
    private static readonly DeclarativeQualityEvaluator Evaluator = ParseEngineFixtures.QualityEvaluator();

    private static QualityTier Tier(string name)
    {
        foreach (var tier in ParseEngineFixtures.GradedFamily().Ladder)
        {
            if (string.Equals(tier.Name, name, StringComparison.Ordinal))
            {
                return tier;
            }
        }

        throw new InvalidOperationException($"No fixture tier is named '{name}'.");
    }

    /// <summary>
    /// THE case this engine exists to answer correctly: the movies review documented that the
    /// contract's own rank-comparing cutoff says a held WEBRip-1080p does not meet a WEBDL-1080p
    /// cutoff, while the surveyed application says the two rungs are the same quality. The declared
    /// weights make them one group, and the evaluator reproduces Radarr's answer.
    /// </summary>
    [Test]
    public void AGroupedRungMeetsACutoffSetAtItsPartner()
    {
        var cutoff = new CutoffPolicy(Tier("WEBDL-1080p"));
        var held = Tier("WEBRip-1080p");

        Evaluator.MeetsCutoff(held, cutoff).Should().BeTrue(
            because: "WEBRip-1080p and WEBDL-1080p share one declared weight, so either satisfies "
                + "a cutoff set at the other — the answer the surveyed application gives");

        // And since A4 landed, the contract's own check now agrees with the domain.
        cutoff.MeetsCutoff(held).Should().BeTrue();
    }

    [Test]
    public void NeitherGroupedRungUpgradesTheOther()
    {
        Evaluator.IsUpgrade(Tier("WEBRip-1080p"), Tier("WEBDL-1080p")).Should().BeFalse();
        Evaluator.IsUpgrade(Tier("WEBDL-1080p"), Tier("WEBRip-1080p")).Should().BeFalse();
    }

    [Test]
    public void AHigherWeightIsAnUpgradeAndALowerOneIsNot()
    {
        Evaluator.IsUpgrade(Tier("HDTV-1080p"), Tier("Bluray-1080p")).Should().BeTrue();
        Evaluator.IsUpgrade(Tier("Bluray-1080p"), Tier("HDTV-1080p")).Should().BeFalse();
    }

    /// <summary>A PROPER of the quality already held is an upgrade; the same quality again is not.</summary>
    [Test]
    public void ARevisionBreaksATieOnWeight()
    {
        var held = Tier("WEBDL-1080p");
        var proper = Tier("WEBDL-1080p") with { Revision = new QualityRevision(2, 0, false) };

        Evaluator.IsUpgrade(held, proper).Should().BeTrue();
        Evaluator.IsUpgrade(proper, held).Should().BeFalse();
        Evaluator.IsUpgrade(held, held).Should().BeFalse();
    }

    /// <summary>
    /// The revision axis the flattened encoding lost (review A5): a re-issue of a repack outranks the
    /// plain repack it corrects.
    /// </summary>
    [Test]
    public void AProperOfARepackOutranksThePlainRepack()
    {
        var repack = Tier("WEBDL-1080p") with { Revision = new QualityRevision(2, 0, true) };
        var properOfRepack = Tier("WEBDL-1080p") with { Revision = new QualityRevision(3, 0, true) };

        Evaluator.IsUpgrade(repack, properOfRepack).Should().BeTrue();
        Evaluator.IsUpgrade(properOfRepack, repack).Should().BeFalse();
    }

    /// <summary>A cutoff answers "good enough to stop looking"; the revision takes no part in it.</summary>
    [Test]
    public void TheCutoffIgnoresTheRevision()
    {
        var cutoff = new CutoffPolicy(Tier("WEBDL-1080p"));
        var held = Tier("WEBDL-1080p");
        var proper = Tier("WEBDL-1080p") with { Revision = new QualityRevision(2, 0, false) };

        Evaluator.MeetsCutoff(held, cutoff).Should().BeTrue();
        Evaluator.MeetsCutoff(proper, cutoff).Should().BeTrue();
    }

    [Test]
    public void ShouldGrabHonorsTheProperHandlingPolicy()
    {
        var held = Tier("WEBDL-1080p");
        var proper = Tier("WEBDL-1080p") with { Revision = new QualityRevision(2, 0, false) };

        var prefer = new CutoffPolicy(Tier("WEBDL-1080p"));
        var accept = new CutoffPolicy(Tier("WEBDL-1080p")) { ProperHandling = ProperHandling.AcceptProper };
        var ignore = new CutoffPolicy(Tier("WEBDL-1080p")) { ProperHandling = ProperHandling.IgnoreProper };

        // Cutoff met: only PreferProper still takes the corrected issue.
        Evaluator.ShouldGrab(held, proper, prefer).Should().BeTrue();
        Evaluator.ShouldGrab(held, proper, accept).Should().BeFalse();
        Evaluator.ShouldGrab(held, proper, ignore).Should().BeFalse();

        // Cutoff not met: the corrected issue is taken, except that IgnoreProper never lets a
        // revision alone cause a grab.
        var distant = new CutoffPolicy(Tier("Remux-1080p")) { ProperHandling = ProperHandling.AcceptProper };
        var distantIgnoring = new CutoffPolicy(Tier("Remux-1080p")) { ProperHandling = ProperHandling.IgnoreProper };

        Evaluator.ShouldGrab(held, proper, distant).Should().BeTrue();
        Evaluator.ShouldGrab(held, proper, distantIgnoring).Should().BeFalse();

        // Nothing held yet: everything is worth grabbing.
        Evaluator.ShouldGrab(null, held, prefer).Should().BeTrue();

        // Cutoff met and the candidate is merely heavier: the search is over.
        Evaluator.ShouldGrab(held, Tier("Remux-1080p"), prefer).Should().BeFalse();
    }

    /// <summary>
    /// Tiers of different declared families are never compared: nothing upgrades across families, and
    /// a cross-family cutoff reads mismatch as satisfied — the declared rule that deleted an entire
    /// per-kind model.
    /// </summary>
    [Test]
    public void FamiliesAreNeverCompared()
    {
        var video = Tier("WEBDL-1080p");
        QualityTier companion = new("Archival", 2, Weight: 2);

        Evaluator.IsUpgrade(companion, video).Should().BeFalse();
        Evaluator.IsUpgrade(video, companion).Should().BeFalse();
        Evaluator.MeetsCutoff(companion, new CutoffPolicy(video)).Should().BeTrue();
    }

    [Test]
    public void EvaluatesAParsedReleaseToItsRungWithItsRevision()
    {
        var parser = ParseEngineFixtures.Parser();
        var parsed = parser.Parse("Some.Film.2019.1080p.WEB-DL.PROPER.x264-GROUP");

        var tier = Evaluator.EvaluateQuality(parsed!);

        tier.Name.Should().Be("WEBDL-1080p");
        tier.Revision.Should().Be(new QualityRevision(2, 0, false));
    }

    [Test]
    public void AnUnknownRungNameLandsOnTheUnknownTierNeverTheWorstOne()
    {
        var parsed = new ParsedRelease(Support.ShapeFixtures.Kind, "Something", Quality: "No-Such-Rung");

        Evaluator.EvaluateQuality(parsed).Name.Should().Be("Unknown");
    }

    /// <summary>
    /// The parse-to-evaluate round trip on the reference cutoff case, end to end: parse both release
    /// shapes, evaluate both, and the re-encode meets the direct-download cutoff.
    /// </summary>
    [Test]
    public void TheGroupedRungCutoffHoldsEndToEnd()
    {
        var parser = ParseEngineFixtures.Parser();

        var held = Evaluator.EvaluateQuality(
            parser.Parse("Some.Film.2019.1080p.WEBRip.x264-GROUP")!);
        var wanted = Evaluator.EvaluateQuality(
            parser.Parse("Some.Film.2019.1080p.WEB-DL.x264-GROUP")!);

        held.Name.Should().Be("WEBRip-1080p");
        wanted.Name.Should().Be("WEBDL-1080p");
        Evaluator.MeetsCutoff(held, new CutoffPolicy(wanted)).Should().BeTrue();
        Evaluator.IsUpgrade(held, wanted).Should().BeFalse();
    }

    [Test]
    public void SizePlausibilityFollowsTheDeclaredBand()
    {
        var runtime = TimeSpan.FromMinutes(100);

        // SDTV declares a 100 MB/minute ceiling.
        DeclarativeQualityEvaluator.IsPlausibleSize(Tier("SDTV"), 99L * 100 * 1024 * 1024, runtime)
            .Should().BeTrue();
        DeclarativeQualityEvaluator.IsPlausibleSize(Tier("SDTV"), 101L * 100 * 1024 * 1024, runtime)
            .Should().BeFalse();

        // Bluray-1080p declares a 5 MB/minute floor.
        DeclarativeQualityEvaluator.IsPlausibleSize(Tier("Bluray-1080p"), 100L * 1024 * 1024, runtime)
            .Should().BeFalse();

        // A rung with no ceiling accepts any size; an unknown runtime disables the check.
        DeclarativeQualityEvaluator.IsPlausibleSize(
            Tier("Remux-1080p"), 200L * 1024 * 1024 * 1024, runtime).Should().BeTrue();
        DeclarativeQualityEvaluator.IsPlausibleSize(
            Tier("SDTV"), 200L * 1024 * 1024 * 1024, TimeSpan.Zero).Should().BeTrue();
    }
}
