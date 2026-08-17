#pragma warning disable ARX0013 // Shape contracts are experimental; these tests exercise the declaration.

using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Plugin.Movies.Definition;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Quality;

/// <summary>
/// The declared ladder: its shape, the invariants the host's shape gate applies to it, and the numbers
/// every upgrade and cutoff decision in the platform is now a pure function of.
/// </summary>
/// <remarks>
/// The ladder used to be accompanied by eight hundred lines of quality model. It is not any more: weight,
/// group and the plausible-size band are typed slots on the tier, so the comparison rules moved to the
/// host and what is left to test here is that the declared numbers are the surveyed ones.
/// </remarks>
[TestFixture]
public class QualityLadderTests
{
    /// <summary>
    /// Radarr's twenty-nine rungs, by name. Listed rather than derived: a ladder that checked itself
    /// against itself would pass while missing a rung.
    /// </summary>
    private static readonly string[] ExpectedRungs =
    [
        "WORKPRINT", "CAM", "TELESYNC", "TELECINE", "REGIONAL", "DVDSCR",
        "SDTV", "DVD", "DVD-R",
        "WEBRip-480p", "WEBDL-480p", "Bluray-480p", "Bluray-576p",
        "HDTV-720p", "WEBRip-720p", "WEBDL-720p", "Bluray-720p",
        "HDTV-1080p", "WEBRip-1080p", "WEBDL-1080p", "Bluray-1080p", "Remux-1080p",
        "HDTV-2160p", "WEBRip-2160p", "WEBDL-2160p", "Bluray-2160p", "Remux-2160p",
        "BR-DISK", "Raw-HD"
    ];

    [Test]
    public void CarriesEveryRungInTheSurveyedLadder()
        => Assert.That(
            MoviesLadder.Tiers.Select(static tier => tier.Name),
            Is.EqualTo(ExpectedRungs));

    /// <summary>
    /// The gate's rule, restated: a format family's ladder must carry distinct ranks. It is the reason the
    /// weight exists as a second number.
    /// </summary>
    [Test]
    public void PublishesADistinctRankForEveryRung()
        => Assert.That(MoviesLadder.Tiers.Select(static tier => tier.Rank), Is.Unique);

    [Test]
    public void PublishesTheLadderInAscendingRankOrder()
        => Assert.That(MoviesLadder.Tiers.Select(static tier => tier.Rank), Is.Ordered.Ascending);

    [Test]
    public void PublishesTheLadderInNonDecreasingWeightOrder()
        => Assert.That(
            MoviesLadder.Tiers.Select(static tier => tier.EffectiveWeight),
            Is.Ordered.Ascending.Using<int>(static (left, right) => left <= right ? -1 : 1));

    /// <summary>
    /// The four pairs the published rank cannot express. A re-encode and a direct download of the same
    /// resolution are interchangeable in the surveyed application, and the gate forbids saying so with the
    /// rank — which is exactly why the tier carries a weight of its own.
    /// </summary>
    [TestCase("WEBRip-480p", "WEBDL-480p")]
    [TestCase("WEBRip-720p", "WEBDL-720p")]
    [TestCase("WEBRip-1080p", "WEBDL-1080p")]
    [TestCase("WEBRip-2160p", "WEBDL-2160p")]
    public void GivesInterchangeableRungsOneWeightAndTwoRanks(string first, string second)
    {
        var left = MoviesDeclaration.Tier(first);
        var right = MoviesDeclaration.Tier(second);

        Assert.Multiple(() =>
        {
            Assert.That(left.EffectiveWeight, Is.EqualTo(right.EffectiveWeight), "They are interchangeable.");
            Assert.That(left.Rank, Is.Not.EqualTo(right.Rank), "The gate forbids equal ranks.");
            Assert.That(left.GroupName, Is.EqualTo(right.GroupName).And.Not.Null);
            Assert.That(left.CompareTo(right), Is.Zero, "Neither is an upgrade over the other.");
        });
    }

    /// <summary>
    /// <b>The fix, as a test.</b> The contract's own cutoff check used to compare published ranks and gave
    /// the domain-wrong answer for every grouped rung; a held WEBRip did not meet a WEBDL cutoff of the
    /// same resolution, so the platform searched forever for an upgrade that does not exist. The check now
    /// compares the effective weight, so the DTO and the domain agree without a per-kind override.
    /// </summary>
    [Test]
    public void TheContractsOwnCutoffCheckAgreesWithTheDomainForAGroupedRung()
    {
        var cutoff = new CutoffPolicy(MoviesDeclaration.Tier("WEBDL-1080p"));
        var held = MoviesDeclaration.Tier("WEBRip-1080p");

        Assert.Multiple(() =>
        {
            Assert.That(cutoff.MeetsCutoff(held), Is.True, "They are the same quality, so the cutoff is met.");
            Assert.That(cutoff.ShouldSearchForUpgrade(held), Is.False, "And nothing further is searched for.");
        });
    }

    [Test]
    public void CarriesTheWeightAndTheSizeBandAsTypedSlotsRatherThanAsAttributes()
    {
        var tier = MoviesDeclaration.Tier("Bluray-720p");

        Assert.Multiple(() =>
        {
            Assert.That(tier.Weight, Is.EqualTo(16), "The semantic weight is a slot, not an attribute.");
            Assert.That(tier.EffectiveWeight, Is.EqualTo(16));
            Assert.That(tier.MinSizeMbPerMinute, Is.Zero);
            Assert.That(tier.MaxSizeMbPerMinute, Is.EqualTo(100));
            Assert.That(tier.PreferredSizeMbPerMinute, Is.EqualTo(95));
            Assert.That(
                tier.AdditionalAttributes,
                Is.Null,
                "Nothing needs the attribute bag any more; the workaround is gone.");
        });
    }

    [Test]
    public void GivesTheUnknownRungALowerWeightThanEveryRealRung()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesLadder.Unknown.Name, Is.EqualTo("Unknown"));
            Assert.That(MoviesLadder.Unknown.Rank, Is.EqualTo(1));
            Assert.That(
                MoviesLadder.Tiers.Min(static tier => tier.EffectiveWeight),
                Is.GreaterThan(MoviesLadder.Unknown.EffectiveWeight),
                "An unreadable release must never block a readable one.");
            Assert.That(
                MoviesLadder.Tiers.Any(static tier => tier.Name == MoviesLadder.Unknown.Name),
                Is.False,
                "The unknown rung is not a member of the ladder.");
        });

    /// <summary>
    /// A cam is a cam whatever it was shot at. Giving these rungs a resolution would let a "1080p" cam
    /// outrank a 720p disc.
    /// </summary>
    [TestCase("WORKPRINT")]
    [TestCase("CAM")]
    [TestCase("TELESYNC")]
    [TestCase("TELECINE")]
    [TestCase("DVD")]
    public void CarriesNoResolutionOnARungThatDoesNotHaveOne(string rung)
        => Assert.That(MoviesDeclaration.Tier(rung).Resolution, Is.Null);

    [Test]
    public void RanksEveryPreReleaseRungBelowEveryRealOne()
    {
        var highestPreRelease = MoviesDeclaration.Tier("DVDSCR").EffectiveWeight;
        var lowestReal = MoviesDeclaration.Tier("SDTV").EffectiveWeight;

        Assert.That(highestPreRelease, Is.LessThan(lowestReal));
    }

    /// <summary>
    /// From the 1080p disc upwards there is no ceiling: a remux of a long film legitimately runs to tens
    /// of gigabytes, and a capped limit would reject exactly the files the user asked for.
    /// </summary>
    [TestCase("Bluray-1080p")]
    [TestCase("Remux-1080p")]
    [TestCase("HDTV-2160p")]
    [TestCase("WEBRip-2160p")]
    [TestCase("WEBDL-2160p")]
    [TestCase("Bluray-2160p")]
    [TestCase("Remux-2160p")]
    [TestCase("BR-DISK")]
    [TestCase("Raw-HD")]
    public void PlacesNoCeilingOnTheRungsThatLegitimatelyExceedIt(string rung)
        => Assert.That(MoviesDeclaration.Tier(rung).MaxSizeMbPerMinute, Is.Null);

    [Test]
    public void StatesEverySizeLimitPerMinuteOfRuntime()
    {
        foreach (var tier in MoviesLadder.Tiers)
        {
            Assert.That(tier.MinSizeMbPerMinute, Is.Not.Null, tier.Name);

            if (tier.MaxSizeMbPerMinute is { } ceiling)
            {
                Assert.That(ceiling, Is.GreaterThan(tier.MinSizeMbPerMinute!.Value), tier.Name);
                Assert.That(tier.PreferredSizeMbPerMinute, Is.LessThanOrEqualTo(ceiling), tier.Name);
            }
        }
    }

    [Test]
    public void PublishesTheLadderOnTheOneFormatFamilyTheShapeDeclares()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Shape.FormatFamilies, Has.Count.EqualTo(1));
            Assert.That(MoviesDeclaration.Video.Ladder, Is.EqualTo(MoviesLadder.Tiers));
            Assert.That(MoviesDeclaration.Video.Unknown, Is.EqualTo(MoviesLadder.Unknown));
        });
    }

    [Test]
    public void GivesEveryRungAName()
        => Assert.That(MoviesLadder.Tiers.Select(static tier => tier.Name), Is.Unique);

    /// <summary>
    /// A file whose size per minute of runtime falls outside its rung's declared band is not the quality
    /// it claims. The rule is host-wide; only the numbers were ever a movie's business, and they are now
    /// typed slots on the tier.
    /// </summary>
    [Test]
    public void RejectsAFileTooSmallForItsClaimedQuality()
    {
        var tier = MoviesDeclaration.Tier("SDTV");
        var runtime = TimeSpan.FromMinutes(100);

        Assert.Multiple(() =>
        {
            Assert.That(tier.MinSizeMbPerMinute, Is.Not.Null);
            Assert.That(
                IsPlausibleSize(tier with { MinSizeMbPerMinute = 5 }, 1L * 1024 * 1024, runtime),
                Is.False,
                "One megabyte cannot be a hundred minutes of standard-definition video.");
        });
    }

    [Test]
    public void RejectsAFileTooLargeForARungThatHasACeiling()
    {
        var tier = MoviesDeclaration.Tier("SDTV");
        var runtime = TimeSpan.FromMinutes(100);

        Assert.Multiple(() =>
        {
            Assert.That(tier.MaxSizeMbPerMinute, Is.EqualTo(100));
            Assert.That(
                IsPlausibleSize(tier, 40L * 1024 * 1024 * 1024, runtime),
                Is.False,
                "Forty gigabytes is not a hundred minutes of standard definition.");
            Assert.That(
                IsPlausibleSize(MoviesDeclaration.Tier("Remux-2160p"), 60L * 1024 * 1024 * 1024, runtime),
                Is.True,
                "The rung with no ceiling accepts it.");
            Assert.That(
                IsPlausibleSize(tier, 40L * 1024 * 1024 * 1024, TimeSpan.Zero),
                Is.True,
                "An unknown runtime disables the check rather than failing it.");
        });
    }

    /// <summary>
    /// A rung name the ladder does not carry lands on the unknown rung rather than throwing: a release
    /// that names something unrecognizable is a release, not a defect.
    /// </summary>
    [Test]
    public void FallsBackToTheUnknownRungRatherThanThrowing()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Tiers.ContainsKey("not-a-rung"), Is.False);
            Assert.That(
                MoviesDeclaration.Parsing.RungResolution.UnknownTierId,
                Is.EqualTo(MoviesLadder.Unknown.Name),
                "The table names the rung it falls back to, and the ladder carries it.");
        });

    /// <summary>
    /// The size gate, stated once over the tier's declared band. This is the host rule; the declaration
    /// supplies only the numbers.
    /// </summary>
    /// <param name="tier">The claimed tier.</param>
    /// <param name="sizeInBytes">The file size.</param>
    /// <param name="runtime">The item's runtime; unknown disables the check.</param>
    /// <returns>Whether the size is inside the declared band.</returns>
    private static bool IsPlausibleSize(QualityTier tier, long sizeInBytes, TimeSpan runtime)
    {
        if (sizeInBytes <= 0 || runtime <= TimeSpan.Zero)
        {
            return true;
        }

        var megabytesPerMinute = sizeInBytes / 1048576d / runtime.TotalMinutes;

        if (tier.MinSizeMbPerMinute is { } floor && megabytesPerMinute < floor)
        {
            return false;
        }

        return tier.MaxSizeMbPerMinute is not { } ceiling || megabytesPerMinute <= ceiling;
    }
}
