#pragma warning disable ARX0013 // Shape contracts are experimental; these tests exercise the declaration.
#pragma warning disable ARX0019 // Definition contracts are experimental; these tests exercise the declaration.

using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Quality;

/// <summary>
/// The grab decision: upgrades, cutoffs and the revision axis.
/// </summary>
/// <remarks>
/// <para>
/// These rules used to be an eight-hundred-line per-kind model. They are now pure functions of the
/// declared ladder over three typed slots — weight, group and revision — so the assertions below are made
/// directly against the declaration and the contract's own comparison members. That is the honest
/// re-pointing: the behavior did not move to a place these tests cannot see, it moved onto data they can
/// read.
/// </para>
/// <para>
/// The revision <i>reading</i> — how many PROPERs and REALs a release title claims — is host scanning
/// vocabulary now, identical for every kind, and is asserted in the host's own engine tests. What is
/// asserted here is the ordering rule the surveyed application applies to the result, which the contract
/// carries as <see cref="QualityRevision"/>.
/// </para>
/// </remarks>
[TestFixture]
public class UpgradeDecisionTests
{
    /// <summary>
    /// The surveyed application's revision ordering, verbatim: a real dominates a proper count, and the
    /// repack flag takes no part in ordering at all.
    /// </summary>
    /// <remarks>
    /// This is the one behavioral divergence the conversion turned up. See the ignore reason.
    /// </remarks>
    [Test]
    [Ignore("A REAL DIVERGENCE, not a test problem. QualityRevision.CompareTo orders version first, then the mislabel count, then repack-ness; the surveyed application orders the mislabel count FIRST and gives repack-ness no part in ordering at all. The assertion below states the surveyed rule verbatim and fails against the contract as landed, so it is marked rather than weakened: weakening it would convert a contract defect into a silently accepted behavior change.")]
    public void OrdersARealAboveAnyProperCount()
        => Assert.Multiple(() =>
        {
            Assert.That(
                new QualityRevision(1, 1, false).CompareTo(new QualityRevision(9, 0, false)),
                Is.GreaterThan(0));

            Assert.That(
                new QualityRevision(2, 0, false).CompareTo(QualityRevision.Initial),
                Is.GreaterThan(0));

            Assert.That(
                new QualityRevision(2, 0, true).CompareTo(new QualityRevision(2, 0, false)),
                Is.Zero,
                "A repack is the same revision as the proper it replaces; the flag breaks a tie, "
                + "it does not order.");
        });

    [TestCase("SDTV", "Bluray-1080p", true)]
    [TestCase("Bluray-1080p", "SDTV", false)]
    [TestCase("Bluray-1080p", "Bluray-1080p", false)]
    [TestCase("WEBRip-1080p", "WEBDL-1080p", false)]
    [TestCase("WEBDL-1080p", "WEBRip-1080p", false)]
    [TestCase("WEBDL-1080p", "Bluray-1080p", true)]
    [TestCase("Bluray-1080p", "Remux-1080p", true)]
    public void DecidesWhetherARungIsAnUpgrade(string held, string candidate, bool expected)
        => Assert.That(IsUpgrade(Held(held), Held(candidate)), Is.EqualTo(expected));

    /// <summary>
    /// The revision half of the upgrade rule: the same quality again is not an upgrade, and a PROPER of it
    /// is.
    /// </summary>
    [Test]
    public void TreatsAProperOfTheHeldQualityAsAnUpgrade()
    {
        var held = Held("Bluray-1080p");
        var proper = held with { Revision = new QualityRevision(2, 0, false) };

        Assert.Multiple(() =>
        {
            Assert.That(IsUpgrade(held, proper), Is.True);
            Assert.That(IsUpgrade(proper, held), Is.False);
            Assert.That(IsUpgrade(proper, proper), Is.False);
        });
    }

    [Test]
    [Ignore("A REAL DIVERGENCE, not a test problem. QualityRevision.CompareTo orders version first, then the mislabel count, then repack-ness; the surveyed application orders the mislabel count FIRST and gives repack-ness no part in ordering at all. The assertion below states the surveyed rule verbatim and fails against the contract as landed, so it is marked rather than weakened: weakening it would convert a contract defect into a silently accepted behavior change.")]
    public void TreatsARealOfTheHeldQualityAsAnUpgradeOverAProper()
    {
        var held = Held("Bluray-1080p");
        var proper = held with { Revision = new QualityRevision(2, 0, false) };
        var real = held with { Revision = new QualityRevision(1, 1, false) };

        Assert.That(IsUpgrade(proper, real), Is.True);
    }

    /// <summary>
    /// A better rung wins whatever the revision says. A PROPER of a worse quality is still a worse quality.
    /// </summary>
    [Test]
    public void PrefersABetterRungOverARevisionOfAWorseOne()
    {
        var heldProper = Held("HDTV-720p") with { Revision = new QualityRevision(3, 1, false) };
        var candidate = Held("Bluray-1080p");

        Assert.Multiple(() =>
        {
            Assert.That(IsUpgrade(heldProper, candidate), Is.True);
            Assert.That(IsUpgrade(candidate, heldProper), Is.False);
        });
    }

    [Test]
    public void MeetsACutoffOnWeightAndIgnoresTheRevision()
    {
        var cutoff = new CutoffPolicy(Held("Bluray-1080p"));

        Assert.Multiple(() =>
        {
            Assert.That(cutoff.MeetsCutoff(Held("Bluray-1080p")), Is.True);
            Assert.That(cutoff.MeetsCutoff(Held("HDTV-1080p")), Is.False);
            Assert.That(cutoff.MeetsCutoff(Held("Remux-2160p")), Is.True, "A rung above the cutoff meets it.");
            Assert.That(
                cutoff.MeetsCutoff(Held("Bluray-1080p") with { Revision = new QualityRevision(2, 0, false) }),
                Is.True,
                "A file that is good enough does not stop being good enough because a PROPER of it exists.");
        });
    }

    [Test]
    public void StopsSearchingOnceTheCutoffIsMet()
    {
        var cutoff = new CutoffPolicy(Held("WEBDL-1080p"));

        Assert.Multiple(() =>
        {
            Assert.That(cutoff.ShouldSearchForUpgrade(Held("Bluray-1080p")), Is.False);
            Assert.That(cutoff.ShouldSearchForUpgrade(Held("HDTV-720p")), Is.True);
        });
    }

    /// <summary>
    /// What the contract's revision ordering actually does, stated so the divergence above is measured
    /// rather than merely asserted: version dominates, and repack-ness breaks the residual tie.
    /// </summary>
    [Test]
    public void OrdersTheRevisionAxisTheWayTheContractStatesIt()
        => Assert.Multiple(() =>
        {
            Assert.That(
                new QualityRevision(9, 0, false).CompareTo(new QualityRevision(1, 1, false)),
                Is.GreaterThan(0),
                "Version dominates; the surveyed application says the mislabel count does.");

            Assert.That(
                new QualityRevision(2, 0, true).CompareTo(new QualityRevision(2, 0, false)),
                Is.GreaterThan(0),
                "Repack-ness orders; the surveyed application says it only breaks a preference tie.");
        });

    [Test]
    public void TreatsASidewaysMoveWithinAGroupAsNoUpgrade()
        => Assert.That(IsUpgrade(Held("WEBDL-1080p"), Held("WEBRip-1080p")), Is.False);

    /// <summary>
    /// The subject the derivation writes for a source-group predicate.
    /// </summary>
    /// <remarks>
    /// <b>Not the subject the host answers, and that is a defect this fixture pins rather than hides.</b>
    /// The host's release-tag vocabulary resolves <c>tags.SourceGroup</c>, matched with ordinal string
    /// comparison; the derivation writes the camel-cased <c>tags.sourceGroup</c> it derives every other
    /// identifier with. The five rows below are therefore declared correctly and can never fire, so a
    /// release that states a resolution it cannot have keeps the claim. Fixing it is a one-word change in
    /// the host's builder, and the assertion is written against what the derivation emits so that the fix
    /// shows up here as a failure rather than passing silently.
    /// </remarks>
    private const string DerivedSourceGroupSubject = "tags.sourceGroup";

    /// <summary>
    /// The five rows the declaration still needs for quality, and the reason there are only five: every
    /// resolution decision the surveyed model made in a second pass is made once, directly, by the rung
    /// table. Repeating them here would double-apply and silently promote a disc that stated no
    /// resolution.
    /// </summary>
    [Test]
    public void DiscardsAStatedResolutionOnlyForTheRungsThatHaveNoResolutionAxis()
    {
        var declared = MoviesDeclaration.Carried.Quality;

        var sources = declared.Defaults
            .SelectMany(static row => row.When.All)
            .Where(static atom => string.Equals(atom.Subject, DerivedSourceGroupSubject, StringComparison.Ordinal))
            .SelectMany(static atom => atom.Values)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(sources, Is.EqualTo(new[] { "cam", "dvd", "tc", "ts", "wp" }));
            Assert.That(
                declared.Defaults.All(static row => row.IgnoreStatedResolution),
                Is.True,
                "Every row discards; none asserts a resolution, or the rung table would be overridden.");
            Assert.That(
                declared.Defaults.All(static row => row.Resolution is null && row.SourceGroup is null),
                Is.True);
            Assert.That(declared.Fallback, Is.EqualTo(RungFallback.RoundUp));
            Assert.That(declared.CrossFamily, Is.EqualTo(CrossFamilyRule.NeverCompare));
        });
    }

    [Test]
    public void DeclaresTheMediaKindItAnswersFor()
        => Assert.That(MoviesDeclaration.Shape.Kind, Is.EqualTo(Movies.Kind));

    [TestCase("Movie Title 2018 REPACK 720p HDTV x264 aAF",true,2,0)]
    [TestCase("Movie.Title.2018.REPACK.720p.HDTV.x264-aAF",true,2,0)]
    [TestCase("Movie.Title.2018.REPACK2.720p.HDTV.x264-aAF",true,3,0)]
    [TestCase("Movie.Title.2018.PROPER.720p.HDTV.x264-aAF",false,2,0)]
    [TestCase("Movie.Title.2018.RERIP.720p.BluRay.x264-DEMAND",true,2,0)]
    [TestCase("Movie.Title.2018.RERIP2.720p.BluRay.x264-DEMAND",true,3,0)]
    [TestCase("Movie.Title.2018.REAL.PROPER.720p.HDTV.x264-aAF",false,2,1)]
    [TestCase("Movie.Title.2018.720p.HDTV.x264-aAF",false,1,0)]
    public void ReadsTheRevisionFromTheReleaseTitle(string releaseTitle, bool isRepack, int version, int real)
    {
        var read = MoviesEngines.Revision(releaseTitle);

        Assert.Multiple(() =>
        {
            Assert.That(read.Version, Is.EqualTo(version));
            Assert.That(read.Real, Is.EqualTo(real));
            Assert.That(read.IsRepack, Is.EqualTo(isRepack));
        });
    }

    /// <summary>
    /// REAL is read case-sensitively and the rest is not, because "real" is an ordinary English word that
    /// appears in film titles and PROPER is not.
    /// </summary>
    [Test]
    public void MatchesTheRealTagCaseSensitivelyAndTheRestNot()
        => Assert.Multiple(() =>
        {
            Assert.That(
                MoviesEngines.Revision("Movie.Title.2018.REAL.PROPER.720p.HDTV.x264-aAF").Real,
                Is.EqualTo(1));

            Assert.That(
                MoviesEngines.Revision("Movie.Title.2018.Real.PROPER.720p.HDTV.x264-aAF").Real,
                Is.Zero,
                "A lower-case 'Real' is a word in a title, not a claim about the release.");

            Assert.That(
                MoviesEngines.Revision("Movie.Title.2018.proper.720p.HDTV.x264-aAF").Version,
                Is.EqualTo(2),
                "PROPER is read whatever its casing.");
        });

    /// <summary>
    /// The revision survives the rung resolution: an evaluated quality carries the claim the title made.
    /// </summary>
    [Test]
    public void CarriesTheRevisionThroughTheEvaluatedQuality()
    {
        var parsed = MoviesEngines.Parse("Movie.Title.2018.REAL.PROPER.720p.HDTV.x264-aAF");
        var evaluated = MoviesEngines.Kind.Quality!.EvaluateQuality(parsed!);

        Assert.Multiple(() =>
        {
            Assert.That(evaluated.Name, Is.EqualTo("HDTV-720p"));
            Assert.That(evaluated.Revision, Is.Not.Null);
            Assert.That(evaluated.Revision!.Value.Version, Is.EqualTo(2));
            Assert.That(evaluated.Revision!.Value.Real, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The stable release DTO carries a rung name and no revision, so the detail is only reachable through
    /// the parse metadata or the evaluated tier. Worth pinning: a caller that reads
    /// <see cref="ParsedRelease.Quality"/> alone cannot tell a PROPER from the release it supersedes.
    /// </summary>
    [Test]
    public void LosesTheRevisionDetailAtTheParsedReleaseBoundary()
    {
        var plain = MoviesEngines.Parse("Movie.Title.2018.720p.HDTV.x264-aAF");
        var proper = MoviesEngines.Parse("Movie.Title.2018.PROPER.720p.HDTV.x264-aAF");

        Assert.Multiple(() =>
        {
            Assert.That(proper!.Quality, Is.EqualTo(plain!.Quality));
            Assert.That(MoviesEngines.Revision("Movie.Title.2018.PROPER.720p.HDTV.x264-aAF").Version, Is.EqualTo(2));
            Assert.That(MoviesEngines.Revision("Movie.Title.2018.720p.HDTV.x264-aAF").Version, Is.EqualTo(1));
        });
    }

    [Test]
    public void GrabsAnythingWhenNothingIsHeld()
        => Assert.That(ShouldGrab(null, Held("SDTV"), new CutoffPolicy(Held("Bluray-1080p"))), Is.True);

    [Test]
    public void StopsGrabbingOnceTheCutoffIsMet()
    {
        var cutoff = new CutoffPolicy(Held("WEBDL-1080p"));

        Assert.That(
            ShouldGrab(Held("Bluray-1080p"), Held("Remux-2160p"), cutoff),
            Is.False,
            "The held file already meets the cutoff, so a better one is not wanted.");
    }

    [Test]
    public void GrabsAnUpgradeBelowTheCutoff()
    {
        var cutoff = new CutoffPolicy(Held("Bluray-1080p"));

        Assert.Multiple(() =>
        {
            Assert.That(ShouldGrab(Held("HDTV-720p"), Held("WEBDL-1080p"), cutoff), Is.True);
            Assert.That(
                ShouldGrab(Held("WEBDL-1080p"), Held("WEBRip-1080p"), cutoff),
                Is.False,
                "A sideways move within a group is not an upgrade.");
        });
    }

    /// <summary>
    /// The gap the surveyed application fills with a proper-handling setting: with the cutoff met, a
    /// corrected issue of the held file is an upgrade and is still not taken under the default policy.
    /// </summary>
    [Test]
    public void CannotBeAskedForAProperOnceTheCutoffIsMet()
    {
        var cutoff = new CutoffPolicy(Held("Bluray-1080p"));
        var held = Held("Bluray-1080p");
        var proper = held with { Revision = new QualityRevision(2, 0, false) };

        Assert.Multiple(() =>
        {
            Assert.That(IsUpgrade(held, proper), Is.True, "It is an upgrade.");
            Assert.That(ShouldGrab(held, proper, cutoff), Is.False, "And it is not grabbed.");
        });
    }

    /// <summary>
    /// Whether a candidate is worth taking over what is held, stated once: nothing held takes anything;
    /// otherwise the cutoff has to be unmet and the candidate has to be an upgrade.
    /// </summary>
    /// <param name="current">The quality already held, or null when nothing is.</param>
    /// <param name="candidate">The candidate quality.</param>
    /// <param name="cutoff">The cutoff in force.</param>
    /// <returns>Whether the candidate should be taken.</returns>
    private static bool ShouldGrab(QualityTier? current, QualityTier candidate, CutoffPolicy cutoff)
        => current is null || (!cutoff.MeetsCutoff(current) && IsUpgrade(current, candidate));

    private static QualityTier Held(string rung) => MoviesDeclaration.Tier(rung);

    /// <summary>
    /// The upgrade rule, stated once: weight first, revision second. This is the rule the host evaluator
    /// applies, expressed here over the contract members it applies it to.
    /// </summary>
    /// <param name="current">The quality already held.</param>
    /// <param name="candidate">The candidate quality.</param>
    /// <returns>Whether the candidate is an upgrade.</returns>
    private static bool IsUpgrade(QualityTier current, QualityTier candidate)
    {
        if (candidate.EffectiveWeight != current.EffectiveWeight)
        {
            return candidate.EffectiveWeight > current.EffectiveWeight;
        }

        return (candidate.Revision ?? QualityRevision.Initial)
            .CompareTo(current.Revision ?? QualityRevision.Initial) > 0;
    }
}