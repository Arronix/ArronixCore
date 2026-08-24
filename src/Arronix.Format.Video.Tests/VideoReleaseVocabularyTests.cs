using Arronix.Format.Video.Contributions;

namespace Arronix.Format.Video.Tests;

[TestFixture]
public sealed class VideoReleaseVocabularyTests
{
    [Test]
    public void WebDlMeansDirectCopyOfTheHostedArtifact()
    {
        Assert.That(VideoReleaseVocabulary.TryReadLineage("WEB-DL", out var lineage), Is.True);
        Assert.That(lineage, Is.EqualTo(new VideoLineage(
            ReleaseChannel.Streaming,
            SourceCarrier.HostedFile,
            AcquisitionMethod.DirectCopy,
            TranscodeHistory.None)));
    }

    [Test]
    public void RemuxMeansDirectCopyOfDiscStreams()
    {
        Assert.That(VideoReleaseVocabulary.TryReadLineage("BLURAY-REMUX", out var lineage), Is.True);
        Assert.That(lineage, Is.EqualTo(new VideoLineage(
            ReleaseChannel.RetailPhysical,
            SourceCarrier.OpticalDisc,
            AcquisitionMethod.DirectCopy,
            TranscodeHistory.None)));
    }

    [TestCase("webrip", ReleaseChannel.Streaming, SourceCarrier.HostedFile)]
    [TestCase("bdrip", ReleaseChannel.RetailPhysical, SourceCarrier.OpticalDisc)]
    public void RipTermsEstablishAtLeastOneAdditionalTransformation(
        string token,
        ReleaseChannel channel,
        SourceCarrier carrier)
    {
        Assert.That(VideoReleaseVocabulary.TryReadLineage(token, out var lineage), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(lineage.Channel, Is.EqualTo(channel));
            Assert.That(lineage.Carrier, Is.EqualTo(carrier));
            Assert.That(lineage.Acquisition, Is.EqualTo(AcquisitionMethod.UnknownDerivedCopy));
            Assert.That(lineage.Transcoding, Is.EqualTo(TranscodeHistory.AtLeastOne));
        });
    }

    [Test]
    public void BroadcastDoesNotInventATranscodeCount()
    {
        Assert.That(VideoReleaseVocabulary.TryReadLineage("hdtv", out var lineage), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(lineage.Channel, Is.EqualTo(ReleaseChannel.Broadcast));
            Assert.That(lineage.Transcoding, Is.EqualTo(TranscodeHistory.Unknown));
        });
    }

    [Test]
    public void UnknownVocabularyStaysUnknown()
    {
        Assert.That(VideoReleaseVocabulary.TryReadLineage("someone-elses-token", out _), Is.False);
    }
}
