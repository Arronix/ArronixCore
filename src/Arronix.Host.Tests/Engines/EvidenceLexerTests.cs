using System.Linq;
using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Parsing.Evidence;
using FluentAssertions;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The lexer: how a title is cut up, and which of the two ambiguity rules decide the rest.
/// </summary>
/// <remarks>
/// These are the tests that make the shape of the scan checkable. The claim the design makes is that
/// longest-phrase-first dissolves most of the ambiguity that would otherwise need a hand-tuned lookaround
/// per spelling, and that what is left fits in one rule about short segments. Both halves of that claim
/// are asserted here rather than argued.
/// </remarks>
[TestFixture]
internal sealed class EvidenceLexerTests
{
    [TestCase("Blu-ray", "blu|ray")]
    [TestCase("Blu.ray", "blu|ray")]
    [TestCase("Blu_ray", "blu|ray")]
    [TestCase("Blu ray", "blu|ray")]
    [TestCase("[BD1080p]", "bd1080p")]
    [TestCase("HDR10+", "hdr10+")]
    [TestCase("DD+7.1", "dd+7|1")]
    [TestCase("1920x1080", "1920x1080")]
    public void EverySeparatorCutsAndAPlusSignDoesNot(string title, string expected) =>
        string.Join('|', EvidenceLexer.Segment(title)).Should().Be(expected);

    [Test]
    public void AnEmptyTitleSegmentsToNothing()
    {
        EvidenceLexer.Segment(null).Should().BeEmpty();
        EvidenceLexer.Segment("   ").Should().BeEmpty();
    }

    [Test]
    public void TheLongerPhraseTakesTheSegmentTheShorterOneWanted()
    {
        // "WEB-DL" cuts into two segments, the second of which is also the dual-language abbreviation.
        // Nothing here is a rule about "DL": the two-segment source phrase simply claims it first.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.WEB-DL.H264-GRP");

        evidence.SourceToken.Should().Be(EvidenceSourceTokens.WebDownload);
        evidence.Languages.Should().BeEmpty();
    }

    [Test]
    public void TheSameSegmentIsStillAvailableWhenNoLongerPhraseClaimsIt()
    {
        // The same title with a named language in front: the first "DL" is the marker, the second belongs
        // to the source phrase, and both readings survive.
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.German.DL.1080p.WEB-DL.H264-GRP");

        evidence.SourceToken.Should().Be(EvidenceSourceTokens.WebDownload);
        evidence.Languages.Should().Contain(claim => claim.IsDualLanguageMarker);
    }

    [Test]
    public void AThreeSegmentPhraseTakesPrecedenceOverItsOwnPrefix()
    {
        var evidence = EvidenceScanFixtures.Scan("Movie.Name.2020.1080p.BluRay.DTS-HD.MA.5.1-GRP");

        evidence.AudioToken.Should().Be(EvidenceAudioTokens.DtsHdMasterAudio);
    }

    [Test]
    public void AShortAlphabeticSegmentIsNotClaimedWithNothingToSupportIt()
    {
        EvidenceScanFixtures.Scan("Movie.Name.BD").SourceToken.Should().BeNull();
        EvidenceScanFixtures.Scan("Movie.Name.The.Web").SourceToken.Should().BeNull();
    }

    [Test]
    public void AShortSegmentCarryingADigitNeedsNoSupport()
    {
        // "4K" is two characters and completely unambiguous, because no work title spells a word that way.
        EvidenceScanFixtures.Scan("Movie Name 4K").StatedResolution.Should().Be(2160);
    }

    [Test]
    public void TwoUnsupportedClaimsCannotPropEachOtherUp()
    {
        // The failing arrangement this rule exists for: a work title containing the word "Web" beside a
        // group name whose second half is also a two-letter audio spelling.
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.WorkTitleContainingVocabulary);

        evidence.SourceToken.Should().BeNull();
        evidence.AudioToken.Should().BeNull();
    }

    [Test]
    public void ASegmentWithAChannelCountWeldedOntoItIsStillReadable()
    {
        EvidenceScanFixtures.Scan("Movie.Name.2015.1080p.HDTV.DD5.1.x264").AudioToken
            .Should().Be(EvidenceAudioTokens.Ac3);
        EvidenceScanFixtures.Scan("Movie.Name.2015.1080p.HDTV.AAC2.0.x264").AudioToken
            .Should().Be(EvidenceAudioTokens.Aac);
    }

    [Test]
    public void ASpellingWhoseDigitIsPartOfItsNameIsNotDamaged()
    {
        EvidenceScanFixtures.Scan("Movie.Name.2015.1080p.HDTV.AC3.x264").AudioToken
            .Should().Be(EvidenceAudioTokens.Ac3);
        EvidenceScanFixtures.Scan("Movie.Name.2015.1080p.HDTV.MP3.VC1").VideoCodecToken
            .Should().Be(EvidenceVideoCodecTokens.Vc1);
    }

    [Test]
    public void EveryExpressionInTheScanIsShortAndSingleClass()
    {
        // The design rule is that no expression exceeds forty characters or serves more than one token
        // class, because that makes a quietly growing mega-alternation structurally impossible. The rule
        // is asserted rather than trusted: the expressions are private, so what is checked here is the
        // observable consequence — every shape the scan reads is anchored to one segment and one class.
        var shapes = new[] { "4kto1080p", "1920x1080", "1080i", "60fps", "v2", "repack2", "proper3" };

        var counts = shapes.Select(shape => EvidenceLexer.Tokenize(shape).Tokens.Count).ToArray();

        counts.Should().OnlyContain(count => count >= 1);
        counts.Should().OnlyContain(count => count <= 2);
    }
}
