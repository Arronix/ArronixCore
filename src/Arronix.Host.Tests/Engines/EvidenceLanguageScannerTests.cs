using System.Linq;
using Arronix.Abstractions.Quality;
using FluentAssertions.Execution;
using FluentAssertions;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The language scan, and the marker that makes it quality-bearing.
/// </summary>
/// <remarks>
/// <para>
/// The reason this scanner exists is not that somebody wants a language column. A dual-language marker
/// beside a disc source and no rip marker is how a whole class of release states that it is a bitstream
/// copy — the second audio track had to be muxed in, and muxing is what a bitstream copy is. Surfacing
/// the marker as typed evidence is what lets that be a rule about an axis instead of a guard string with
/// a nationality in its name.
/// </para>
/// <para>
/// So the assertions here are mostly about the marker: that it is reported separately from the name, that
/// a two-letter abbreviation of it is not believed on its own, and that a longer phrase can take the same
/// segment away from it.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class EvidenceLanguageScannerTests
{
    [TestCase("German", "de")]
    [TestCase("GER", "de")]
    [TestCase("Deutsch", "de")]
    [TestCase("French", "fr")]
    [TestCase("TrueFrench", "fr")]
    [TestCase("Spanish", "es")]
    [TestCase("Castellano", "es")]
    [TestCase("Italian", "it")]
    [TestCase("Japanese", "ja")]
    [TestCase("JPN", "ja")]
    [TestCase("Korean", "ko")]
    [TestCase("Chinese", "zh")]
    [TestCase("Russian", "ru")]
    [TestCase("Portuguese", "pt")]
    [TestCase("Dutch", "nl")]
    [TestCase("Swedish", "sv")]
    [TestCase("Polish", "pl")]
    [TestCase("Turkish", "tr")]
    public void ANamedLanguageIsClaimed(string spelling, string code)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.{spelling}.1080p.BluRay.x264-GRP")
            .Languages.Should().Contain(claim => claim.Language.Code == code && !claim.IsDualLanguageMarker);

    [TestCase("MULTi")]
    [TestCase("Dual")]
    [TestCase("Dual.Audio")]
    public void ASelfDescribingMarkerNeedsNoNamedLanguage(string spelling)
        => EvidenceScanFixtures.Scan($"Movie.Name.2020.{spelling}.1080p.BluRay.x264-GRP")
            .Languages.Should().Contain(claim => claim.IsDualLanguageMarker);

    [TestCase("DL")]
    [TestCase("ML")]
    public void ATwoLetterMarkerIsBelievedOnlyBesideANamedLanguage(string spelling)
    {
        using (new AssertionScope())
        {
            EvidenceScanFixtures.Scan($"Movie.Name.2020.German.{spelling}.1080p.BluRay.x264-GRP")
                .Languages.Should().Contain(claim => claim.IsDualLanguageMarker);

            EvidenceScanFixtures.Scan($"Movie.Name.2020.{spelling}.1080p.BluRay.x264-GRP")
                .Languages.Should().NotContain(claim => claim.IsDualLanguageMarker);
        }
    }

    [Test]
    public void AMarkerNamesNoLanguageBecauseThatIsWhatMakesItAMarker()
    {
        var marker = EvidenceScanFixtures.Scan(EvidenceScanFixtures.DualLanguageDiscEncode)
            .Languages.Single(claim => claim.IsDualLanguageMarker);

        marker.Language.Code.Should().Be("und");
    }

    [Test]
    public void ANamedLanguageAndItsMarkerAreTwoSeparateClaims()
    {
        var languages = EvidenceScanFixtures.Scan(EvidenceScanFixtures.DualLanguageDiscEncode).Languages;

        using (new AssertionScope())
        {
            languages.Should().HaveCount(2);
            languages.Should().ContainSingle(claim => !claim.IsDualLanguageMarker);
            languages.Should().ContainSingle(claim => claim.IsDualLanguageMarker);
        }
    }

    [Test]
    public void ALanguageIsNotClaimedTwiceWhenItIsSpelledTwice()
        => EvidenceScanFixtures.Scan("Movie.Name.2020.German.GER.1080p.BluRay.x264-GRP")
            .Languages.Where(claim => !claim.IsDualLanguageMarker).Should().HaveCount(1);

    [Test]
    public void NoTwoLetterCodeIsAdmittedAsALanguageAtAll()
    {
        // Two-letter codes collide with initialisms in every direction, and a false language makes a
        // profile that refuses a dub start refusing the original.
        using (new AssertionScope())
        {
            EvidenceScanFixtures.Scan("Movie.Name.2020.NL.1080p.BluRay.x264-GRP")
                .Languages.Should().BeEmpty();
            EvidenceScanFixtures.Scan("Movie.Name.2020.FR.1080p.BluRay.x264-GRP")
                .Languages.Should().BeEmpty();
        }
    }

    [Test]
    public void AThreeLetterCodeThatIsAlsoAnOrdinaryWordIsNotAdmitted()
    {
        // "SPA", "FIN", "MAY" and "CAT" are real codes and real words. The vocabulary declines all of
        // them, and takes the missed claims rather than the false ones.
        using (new AssertionScope())
        {
            EvidenceScanFixtures.Scan("Movie.Name.At.The.Spa.2020.1080p.BluRay.x264-GRP")
                .Languages.Should().BeEmpty();
            EvidenceScanFixtures.Scan("The.Cat.2020.1080p.BluRay.x264-GRP")
                .Languages.Should().BeEmpty();
        }
    }

    [Test]
    public void ALanguageSpellingCannotStealASegmentAPhraseAlreadyClaimed()
    {
        // The point of looking languages up only over unclaimed segments: nothing here needs to know that
        // "DL" belongs to a source phrase, because the source phrase already took it.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.DDP.5.1.H.264-GRP");

        evidence.Languages.Should().BeEmpty();
        evidence.SourceToken.Should().Be(EvidenceSourceTokens.WebDownload);
    }
}
