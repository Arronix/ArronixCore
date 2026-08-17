// Exercises the experimental quality-axes contracts.
#pragma warning disable ARX0021

using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Tests.Quality.Support;

namespace Arronix.Abstractions.Tests.Quality;

/// <summary>
/// The decision table, row by row, including the two rows that are about acting rather than about
/// ordering.
/// </summary>
[TestFixture]
public class QualityPolicyDecideTests
{
    private static QualityPolicy Policy => AxisFixtures.ShippedVideoDefault();

    private static QualityPoint Held { get; } = AxisFixtures.Point(
        AxisFixtures.Quantity(AxisFixtures.Resolution, 720),
        AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
        AxisFixtures.Quantity(AxisFixtures.Generation, 0));

    [Test]
    public void ARefusedValueIsRefusedAndTheSentenceNamesTheAxis()
    {
        var camera = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.CameraCapture));

        var decision = Policy.Decide(null, camera);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(GrabVerdict.Refused));
            Assert.That(decision.Reason, Does.Contain("Origin"));
            Assert.That(decision.Reason, Does.Contain("CameraCapture"));
        });
    }

    [Test]
    public void ARefusalCausedBySilenceIsReportedAsMissingEvidenceRatherThanAsARefusedValue()
    {
        var policy = QualityPolicy.For(
            AxisFixtures.VideoType,
            declaration => declaration
                .Refuse(AxisFixtures.Packaging, AxisFixtures.DiscImage)
                .WhenUnknown(UnknownEvidence.Refuse));

        var silent = AxisFixtures.Point(AxisFixtures.Quantity(AxisFixtures.Resolution, 1080));

        var decision = policy.Decide(null, silent);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(GrabVerdict.EvidenceInsufficient));
            Assert.That(decision.Reason, Does.Contain("nothing in the release says"));
        });
    }

    [Test]
    public void NothingHeldIsGrabbed()
    {
        var candidate = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 480),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream));

        var decision = Policy.Decide(null, candidate);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(GrabVerdict.Grab));
            Assert.That(decision.Reason, Is.EqualTo("Nothing held."));
        });
    }

    [Test]
    public void OnceTheHeldFileSatisfiesTheCutoffAGenuineUpgradeIsStillDeclined()
    {
        var goodEnough = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDisc),
            AxisFixtures.Quantity(AxisFixtures.Generation, 1));

        var better = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDiscBitstream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        Assert.Multiple(() =>
        {
            Assert.That(
                Policy.Compare(goodEnough, better),
                Is.EqualTo(QualityJudgment.Better),
                "The ordering still says the bitstream copy is above the re-encode; that is not the "
                + "question the cutoff answers.");
            Assert.That(
                Policy.Decide(goodEnough, better).Verdict,
                Is.EqualTo(GrabVerdict.AlreadyGoodEnough),
                "A cutoff that let a better candidate through would not be a cutoff. Moving the generation "
                + "floor to zero is what a user who wants the bitstream copy does.");
        });
    }

    [Test]
    public void SomethingNoBetterThanWhatIsHeldIsNotAnUpgrade()
    {
        var sideways = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 720),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 1));

        var decision = Policy.Decide(Held, sideways);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(GrabVerdict.NotAnUpgrade));
            Assert.That(decision.Reason, Does.StartWith("Not an upgrade"));
        });
    }

    [Test]
    public void AGenuineUpgradeIsGrabbedAndTheSentenceNamesTheAxisThatDecided()
    {
        var better = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        var decision = Policy.Decide(Held, better);

        Assert.Multiple(() =>
        {
            Assert.That(decision.Verdict, Is.EqualTo(GrabVerdict.Grab));
            Assert.That(decision.Reason, Does.Contain("Resolution"));
            Assert.That(decision.Reason, Does.Contain("720"));
            Assert.That(decision.Reason, Does.Contain("1080"));
        });
    }

    [Test]
    public void AClaimNeverOutranksAMeasurement()
    {
        // The loop this closes: a title claims a resolution, we grab it, a probe measures less, the same
        // title comes round on the next pass and claims it again. Source precedence fixes the reading and
        // by itself makes the loop worse, because only the held file is ever probed.
        var claimed = AxisFixtures.Point(
            AxisReading.Of(AxisFixtures.Resolution, AxisValue.Quantity(1080), EvidenceSource.ReleaseTitle),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        var measured = AxisFixtures.Point(
            AxisReading.Of(AxisFixtures.Resolution, AxisValue.Quantity(720), EvidenceSource.ContainerProbe),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.Stream),
            AxisFixtures.Quantity(AxisFixtures.Generation, 0));

        var firstPass = Policy.Decide(null, claimed);
        var afterImport = Policy.Decide(measured, claimed);

        Assert.Multiple(() =>
        {
            Assert.That(firstPass.Verdict, Is.EqualTo(GrabVerdict.Grab), "Nothing was held, so it was taken.");
            Assert.That(
                Policy.Compare(measured, claimed),
                Is.EqualTo(QualityJudgment.Better),
                "The ordering is pointwise and provenance-blind on purpose: folding a pairwise, asymmetric "
                + "rule into it would destroy transitivity.");
            Assert.That(
                afterImport.Verdict,
                Is.EqualTo(GrabVerdict.NotAnUpgrade),
                "So the rule lives on the decision, which is where the irreversible call is made.");
            Assert.That(afterImport.Reason, Does.Contain("we measured"));
        });
    }

    [Test]
    public void TheProvenanceRuleTerminatesRatherThanAlternating()
    {
        var claimed = AxisFixtures.Point(
            AxisReading.Of(AxisFixtures.Resolution, AxisValue.Quantity(1080), EvidenceSource.ReleaseTitle));

        var measured = AxisFixtures.Point(
            AxisReading.Of(AxisFixtures.Resolution, AxisValue.Quantity(720), EvidenceSource.ContainerProbe));

        for (var pass = 0; pass < 5; pass++)
        {
            Assert.That(Policy.Decide(measured, claimed).Verdict, Is.EqualTo(GrabVerdict.NotAnUpgrade));
        }
    }

    [Test]
    public void AHeuristicMayInformTheRankingAndMayNotRefuseAnything()
    {
        var guessed = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDisc),
            AxisReading.Of(AxisFixtures.Packaging, AxisFixtures.DiscImage, EvidenceSource.Assumed));

        var stated = AxisFixtures.Point(
            AxisFixtures.Quantity(AxisFixtures.Resolution, 1080),
            AxisFixtures.Member(AxisFixtures.Origin, AxisFixtures.HighDefinitionDisc),
            AxisReading.Of(AxisFixtures.Packaging, AxisFixtures.DiscImage, EvidenceSource.ReleaseTitle));

        Assert.Multiple(() =>
        {
            Assert.That(
                Policy.Admits(guessed).IsAdmitted,
                Is.True,
                "A refusal is irreversible from the user's side, so refusing on a guess is the worst thing "
                + "this model can do.");
            Assert.That(
                Policy.Admits(stated).IsAdmitted,
                Is.False,
                "An explicit token in the title still refuses.");
        });
    }

    [Test]
    public void TheProseSaysWhatThePolicyDoesIncludingTheRuleAUserWouldNotDiscover()
    {
        var prose = Policy.Describe();

        Assert.Multiple(() =>
        {
            Assert.That(prose, Does.Contain("Prefer"));
            Assert.That(prose, Does.Contain("2160 lines"));
            Assert.That(prose, Does.Contain("as a bonus"));
            Assert.That(prose, Does.Contain("Good enough at"));
            Assert.That(
                prose,
                Does.Contain("at most 1 re-encodes"),
                "A descending axis is never described in raw magnitude: the double negative in "
                + "'at least one re-encode' is misread by most people who meet it.");
            Assert.That(prose, Does.Contain("Never take"));
            Assert.That(
                prose,
                Does.Contain("re-download on a claim we have already measured"),
                "Generated, because it is behavior a user would otherwise have to discover.");
        });
    }
}
