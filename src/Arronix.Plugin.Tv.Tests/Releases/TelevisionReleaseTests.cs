using System.Collections.Generic;
using System.Linq;
using Arronix.Abstractions.Releases;
using Arronix.Format.Video;


namespace Arronix.Plugin.Tv.Tests.Releases;

[TestFixture]
public class TelevisionReleaseTests
{
    [Test]
    public void ASeasonPackCanStateCoveredAndMissingUnitsWithoutFlatteningThem()
    {
        var first = new EpisodeTarget("Example", new EpisodeCoordinate.Ordinal(1, 1));
        var second = new EpisodeTarget("Example", new EpisodeCoordinate.Ordinal(1, 2));
        var target = new TelevisionReleaseTarget(new HashSet<EpisodeTarget> { first, second });

        var match = new TargetMatch<TelevisionReleaseTarget>(
            TargetDisposition.Partial,
            [new TelevisionReleaseTarget(new HashSet<EpisodeTarget> { first })],
            [new TelevisionReleaseTarget(new HashSet<EpisodeTarget> { second })]);

        Assert.Multiple(() =>
        {
            Assert.That(match.Covered.Single().Episodes, Does.Contain(first));
            Assert.That(match.Missing.Single().Episodes, Does.Contain(second));
            Assert.That(match.Disposition, Is.EqualTo(TargetDisposition.Partial));
            Assert.That(target.Episodes, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void TvAndMoviesCanShareVideoDefaultsWithoutSharingReleaseShapes()
    {
        var lower = Release("lower", 1080);
        var higher = Release("higher", 2160);

        Assert.That(TelevisionReleasePolicy.Default.Compare(higher, lower), Is.Positive);
    }

    [Test]
    public void AnUnknownTranscodeHistoryDoesNotMasqueradeAsNoTranscode()
    {
        var direct = Release("direct", 1080);
        var unknown = direct with
        {
            SeriesTitle = "unknown",
            Representation = direct.Representation! with
            {
                Lineage = direct.Representation.Lineage with { Transcoding = TranscodeHistory.Unknown }
            }
        };

        Assert.That(TelevisionReleasePolicy.Default.Compare(direct, unknown), Is.Positive);
    }

    private static TelevisionRelease Release(string title, int lines) => new(
        title,
        new HashSet<EpisodeCoordinate> { new EpisodeCoordinate.Ordinal(1, 1) },
        new Video
        {
            Lineage = new VideoLineage(
                ReleaseChannel.Streaming,
                SourceCarrier.HostedFile,
                AcquisitionMethod.DirectCopy,
                TranscodeHistory.None),
            Resolution = new VideoResolution(lines)
        });
}
