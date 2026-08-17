using Arronix.Abstractions.Quality;
using FluentAssertions;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The distributor scan, and the arrangement rule that makes its vocabulary affordable.
/// </summary>
/// <remarks>
/// A service code is claimed only in a title that also states a streaming source. That is not a
/// convenience: without it every three-letter code has to be argued about one at a time, and a false
/// distributor claim is worse than a missing one, because a preference for one service silently starts
/// matching releases from another.
/// </remarks>
[TestFixture]
internal sealed class EvidenceDistributorScannerTests
{
    [TestCase("AMZN", EvidenceDistributorTokens.Amazon)]
    [TestCase("Amazon", EvidenceDistributorTokens.Amazon)]
    [TestCase("NF", EvidenceDistributorTokens.Netflix)]
    [TestCase("DSNP", EvidenceDistributorTokens.Disney)]
    [TestCase("HMAX", EvidenceDistributorTokens.HboMax)]
    [TestCase("HULU", EvidenceDistributorTokens.Hulu)]
    [TestCase("ATVP", EvidenceDistributorTokens.AppleTelevision)]
    [TestCase("PCOK", EvidenceDistributorTokens.Peacock)]
    [TestCase("PMTP", EvidenceDistributorTokens.Paramount)]
    [TestCase("CRAV", EvidenceDistributorTokens.Crave)]
    [TestCase("iPlayer", EvidenceDistributorTokens.Iplayer)]
    [TestCase("STARZ", EvidenceDistributorTokens.Starz)]
    public void AServiceCodeIsCapturedBesideAStreamSource(string spelling, string token)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.1080p.{spelling}.WEB-DL.DDP.5.1.H.264-GRP")
            .DistributorToken.Should().Be(token);

    [Test]
    public void AServiceCodeIsNotClaimedWithoutAStreamSource()
    {
        // The same three letters in a disc release are three letters, not a service.
        EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.AMZN.BluRay.x264-GRP").DistributorToken
            .Should().BeNull();
    }

    [Test]
    public void TheAudioQualifierThatLooksLikeAServiceCodeIsNotOne()
    {
        // "MA" is how DTS-HD Master Audio is spelled, and it appears in the corpus beside a genuine
        // stream token. It is absent from the vocabulary for that reason, and the three-segment audio
        // phrase would take the segment anyway.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.DTS-HD.MA.5.1-GRP");

        evidence.DistributorToken.Should().BeNull();
        evidence.AudioToken.Should().Be(EvidenceAudioTokens.DtsHdMasterAudio);
    }

    [Test]
    public void TwoServiceCodesInOneTitleCancel()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.AMZN.NF.WEB-DL.x264-GRP").DistributorToken
            .Should().BeNull();

    [Test]
    public void NothingStatedIsNothingClaimed()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.x264-GRP").DistributorToken
            .Should().BeNull();
}
