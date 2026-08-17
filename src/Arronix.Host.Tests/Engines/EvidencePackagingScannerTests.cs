using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Parsing.Evidence;
using FluentAssertions;
using FluentAssertions.Execution;

// Quality contracts are experimental; the scan produces one of them.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The packaging and container scans.
/// </summary>
/// <remarks>
/// <para>
/// Packaging is reported only when a release states it, never inferred from the shape of a title. The
/// heuristic version of this question is a known over-firing one, and its failure mode is the worst kind:
/// a release whose <i>work name</i> happens to contain a disc word gets silently refused by a profile
/// that refuses whole discs.
/// </para>
/// <para>
/// The container is carried as a typed fact rather than as a fallback, because a reading downstream is
/// keyed on it: a bare line count inside a streaming container is a stream download and not a broadcast
/// capture. Carrying it here is what lets that be a family rule instead of three per-kind guard strings.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class EvidencePackagingScannerTests
{
    [TestCase("BDISO", EvidencePackagingTokens.DiscImage)]
    [TestCase("DVDISO", EvidencePackagingTokens.DiscImage)]
    [TestCase("BD50", EvidencePackagingTokens.DiscImage)]
    [TestCase("BR-DISK", EvidencePackagingTokens.DiscImage)]
    [TestCase("Complete.BluRay", EvidencePackagingTokens.DiscImage)]
    [TestCase("BDMV", EvidencePackagingTokens.DiscFolder)]
    [TestCase("AVCHD", EvidencePackagingTokens.DiscFolder)]
    [TestCase("VIDEO_TS", EvidencePackagingTokens.DiscFolder)]
    public void AnExplicitWholeDiscSpellingIsRead(string spelling, string token)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.{spelling}").PackagingToken.Should().Be(token);

    [Test]
    public void ADiscImageExtensionStatesPackagingToo()
    {
        var evidence = ReleaseEvidenceScanner.Scan(
            new EvidenceScanRequest { Title = "Movie.Name.2020.1080p", FileName = "Movie.Name.2020.iso" });

        using (new AssertionScope())
        {
            evidence.Container.Should().Be(".iso");
            evidence.PackagingToken.Should().Be(EvidencePackagingTokens.DiscImage);
        }
    }

    [Test]
    public void APlayableContainerStatesNoPackagingAtAll()
    {
        var evidence = ReleaseEvidenceScanner.Scan(
            new EvidenceScanRequest { Title = "Movie.Name.2020.1080p", FileName = "Movie.Name.2020.mkv" });

        using (new AssertionScope())
        {
            evidence.Container.Should().Be(".mkv");
            evidence.PackagingToken.Should().BeNull();
        }
    }

    [Test]
    public void NoPackagingIsInferredFromATitleThatMerelyMentionsADisc()
    {
        using (new AssertionScope())
        {
            EvidenceScanFixtures.Scan(EvidenceScanFixtures.NotADiscImage).PackagingToken.Should().BeNull();
            EvidenceScanFixtures.Scan(EvidenceScanFixtures.WorkTitleNotADiscImage).PackagingToken
                .Should().BeNull();
        }
    }

    [TestCase("Movie.Name.2020.1080p.WEB-DL.x264-GRP.mkv", ".mkv")]
    [TestCase("Movie.Name.2020.1080p.WEB-DL.x264-GRP.mp4", ".mp4")]
    [TestCase("[grp] Movie Name [1080p][MKV][h264]", ".mkv")]
    public void AContainerStatedInTheTitleIsRead(string title, string container)
        => EvidenceScanFixtures.Scan(title).Container.Should().Be(container);

    [Test]
    public void ARealFilesOwnExtensionOutranksAnythingTheTitleClaimed()
    {
        // A fact about a thing that exists beats a claim about a thing being offered.
        var evidence = ReleaseEvidenceScanner.Scan(
            new EvidenceScanRequest
            {
                Title = "[grp] Movie Name [1080p][MP4][h264]",
                FileName = "/library/Movie Name/Movie Name.mkv",
            });

        evidence.Container.Should().Be(".mkv");
    }

    [Test]
    public void NoContainerIsInventedWhenThereIsNoFileAndNoClaim()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.x264-GRP").Container.Should().BeNull();

    [Test]
    public void ThePerKindResidueIsCarriedThroughUntouched()
    {
        // The scan neither reads a kind's guards and tags nor knows what any of them mean. Carrying them
        // is what lets a kind contribute to its family's axes without its identifier strings crossing into
        // a contract assembly.
        var evidence = ReleaseEvidenceScanner.Scan(
            new EvidenceScanRequest
            {
                Title = "[FFF] Movie Name - 01 [BD][720p-AAC]",
                ReleaseGroup = "FFF",
                Guards = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bracket-convention" },
                Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["part"] = "01" },
                Probe = new MediaProbe { Height = 720, Width = 1280 },
            });

        using (new AssertionScope())
        {
            evidence.ReleaseGroup.Should().Be("FFF");
            evidence.Guards.Should().Contain("bracket-convention");
            evidence.Tags.Should().ContainKey("part");
            evidence.Probe!.Height.Should().Be(720);
        }
    }
}
