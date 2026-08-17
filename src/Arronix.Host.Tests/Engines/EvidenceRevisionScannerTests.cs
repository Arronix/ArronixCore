using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The re-issue scan: one arithmetic, three independent statements.
/// </summary>
/// <remarks>
/// The three statements are kept apart on purpose. "The encode was corrected", "the content was wrong"
/// and "only the packaging changed" are different facts, and folding them into one comparable number is
/// what forces an argument about which of them outranks the others — an argument that belongs to whoever
/// writes a policy and has no business being settled in a scanner.
/// </remarks>
[TestFixture]
internal sealed class EvidenceRevisionScannerTests
{
    [TestCase("Movie.Name.2018.1080p.HDTV.x264-GRP", 1)]
    [TestCase("Movie.Name.2018.PROPER.1080p.HDTV.x264-GRP", 2)]
    [TestCase("Movie.Name.2018.PROPER2.1080p.HDTV.x264-GRP", 3)]
    [TestCase("Movie.Name.2018.REPACK.1080p.HDTV.x264-GRP", 2)]
    [TestCase("Movie.Name.2018.REPACK2.1080p.HDTV.x264-GRP", 3)]
    [TestCase("Movie.Name.2018.REPACK3.1080p.HDTV.x264-GRP", 4)]
    [TestCase("Movie.Name.2018.RERIP.1080p.HDTV.x264-GRP", 2)]
    [TestCase("[grp] Movie Name v2 [1080p]", 2)]
    [TestCase("[grp] Movie Name v3 [1080p]", 3)]
    public void ABareMarkerStatesTheSecondIssueAndANumberedMarkerStatesTheOneAfterIt(string title, int issue)
        => EvidenceScanFixtures.Scan(title).Version.Should().Be(issue);

    [Test]
    public void AFirstIssueIsOneRatherThanZero()
        => EvidenceScanFixtures.Scan("Movie.Name.2018.1080p.HDTV.x264-GRP").Version.Should().Be(1);

    [Test]
    public void OnlyARepackSpellingSaysThePackagingChanged()
    {
        using (new AssertionScope())
        {
            EvidenceScanFixtures.Scan("Movie.Name.2018.REPACK.1080p.HDTV.x264-GRP").IsRepack
                .Should().BeTrue();
            EvidenceScanFixtures.Scan("Movie.Name.2018.RERIP.1080p.HDTV.x264-GRP").IsRepack
                .Should().BeTrue();
            EvidenceScanFixtures.Scan("Movie.Name.2018.PROPER.1080p.HDTV.x264-GRP").IsRepack
                .Should().BeFalse();
        }
    }

    [Test]
    public void AMislabelFixIsCountedSeparatelyFromACorrection()
    {
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2018.REAL.PROPER.1080p.HDTV.x264-GRP");

        using (new AssertionScope())
        {
            evidence.RealCount.Should().Be(1);
            evidence.Version.Should().Be(2);
            evidence.IsRepack.Should().BeFalse();
        }
    }

    [Test]
    public void TwoMislabelFixesAreCountedTwice()
        => EvidenceScanFixtures.Scan("Movie.Name.2018.REAL.REAL.PROPER.1080p.HDTV.x264-GRP").RealCount
            .Should().Be(2);

    [Test]
    public void TheHighestStatedIssueWinsWhenTwoMarkersDisagree()
        => EvidenceScanFixtures.Scan("Movie.Name.2018.PROPER.REPACK2.1080p.HDTV.x264-GRP").Version
            .Should().Be(3);

    [Test]
    public void AnIssueNumberIsNotReadOutOfAnOrdinaryTitleWord()
    {
        using (new AssertionScope())
        {
            EvidenceScanFixtures.Scan("Movie Name Volume 2 1080p HDTV x264").Version.Should().Be(1);
            EvidenceScanFixtures.Scan("Movie.Name.S04E01.1080p.HDTV.x264").Version.Should().Be(1);
        }
    }
}
