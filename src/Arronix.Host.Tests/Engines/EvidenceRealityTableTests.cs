using System.Linq;
using Arronix.Abstractions.Quality;
using FluentAssertions;
using FluentAssertions.Execution;

// Quality contracts are experimental; the scan produces one of them.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The thirty hand-mapped release shapes, asserted at the boundary the scanners own.
/// </summary>
/// <remarks>
/// <para>
/// Each row of the design's reality table states two things: what the scan has to be able to <i>see</i>,
/// and what a family then <i>reads</i> out of it. Only the first half is asserted here, because only the
/// first half is this layer's. A row whose whole point is an inference — an orphan bitstream claim
/// becoming a disc origin, a container becoming a stream origin — is asserted as the evidence that makes
/// the inference possible, and never as the inference, which would put a reading rule in a scanner test.
/// </para>
/// <para>
/// Six of the design's rows are marked as depending on scanner work that did not exist: the codec, the
/// pixel-dimension form, the un-bucketed line count, the interlace marker, the distributor capture and
/// the language claim. Those six are the reason this fixture exists, and each of them is asserted
/// explicitly below rather than left to a general test.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class EvidenceRealityTableTests
{
    [Test]
    public void Row01AnInterlacedDiscBitstreamKeepsItsRasterItsScanAndItsBitstreamClaim()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.InterlacedDiscBitstream);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.StatedResolution.Should().Be(480);
            evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.LineCount);
            evidence.ScanType.Should().Be(Abstractions.Quality.ScanType.Interlaced);
            evidence.IsRemux.Should().BeTrue();
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H264);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.Ac3);
        }
    }

    [Test]
    public void Row02ADiscBitstreamAt720LinesIsReadAtSevenHundredAndTwenty()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.DiscBitstreamAt720);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.StatedResolution.Should().Be(720);
            evidence.IsRemux.Should().BeTrue();
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.Flac);
        }
    }

    [Test]
    public void Row03ADualLanguageDiscEncodeStatesBothTheLanguageAndTheMarker()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.DualLanguageDiscEncode);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.StatedResolution.Should().Be(1080);
            evidence.IsRemux.Should().BeFalse();
            evidence.Languages.Should().Contain(claim => claim.Language.Code == "de");
            evidence.Languages.Should().Contain(claim => claim.IsDualLanguageMarker);
        }
    }

    [Test]
    public void Row04TheVeryVersatileCodecIsInTheVocabulary()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.VeryVersatileCodec);

        using (new AssertionScope())
        {
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H266);
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.UltraHighDefinitionDiscRip);
            evidence.StatedResolution.Should().Be(2160);
            evidence.DynamicRangeTokens.Should().Equal(EvidenceDynamicRangeTokens.HighDynamicRange10);
            evidence.Languages.Should().Contain(claim => claim.Language.Code == "de");
        }
    }

    [Test]
    public void Row05AMarketingRasterNameIsReadAsOneAndTheSourceStaysAbsent()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.MarketingRasterOnly);

        using (new AssertionScope())
        {
            evidence.StatedResolution.Should().Be(2160);
            evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.MarketingName);
            evidence.SourceToken.Should().BeNull();
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H265);
        }
    }

    [Test]
    public void Row06ABitstreamClaimWithNoSourceIsReportedAsExactlyThat()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.OrphanBitstreamClaim);

        using (new AssertionScope())
        {
            evidence.IsRemux.Should().BeTrue();
            evidence.SourceToken.Should().BeNull();
            evidence.StatedResolution.Should().Be(2160);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H265);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.TrueHd);
        }
    }

    [Test]
    public void Row07ABracketedBitstreamCompoundReadsTheSameAndKeepsItsContainer()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.OrphanBitstreamBracketed);

        using (new AssertionScope())
        {
            evidence.IsRemux.Should().BeTrue();
            evidence.SourceToken.Should().BeNull();
            evidence.StatedResolution.Should().Be(2160);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H265);
            evidence.DynamicRangeTokens.Should().Equal(EvidenceDynamicRangeTokens.HighDynamicRange10);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.DtsHdMasterAudio);
            evidence.Container.Should().Be(".mkv");
        }
    }

    [Test]
    public void Row08ABroadcastTransportStreamIsToldApartByItsCodec()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.BroadcastTransportStream);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.HighDefinitionBroadcast);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.Mpeg2);
            evidence.StatedResolution.Should().Be(1080);
            evidence.ScanType.Should().Be(Abstractions.Quality.ScanType.Interlaced);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.Ac3);
        }
    }

    [Test]
    public void Row09AnIntermediateRasterSurvivesUnbucketed()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.IntermediateRaster);

        using (new AssertionScope())
        {
            evidence.StatedResolution.Should().Be(540);
            evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.LineCount);
            evidence.Container.Should().Be(".mkv");
            evidence.SourceToken.Should().BeNull();
        }
    }

    [Test]
    public void Row10ABracketedStreamMarkerIsClaimedBecauseTheTitleSupportsIt()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.BracketedStreamMarker);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.Web);
            evidence.StatedResolution.Should().Be(480);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H264);
            evidence.Container.Should().Be(".mkv");
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.Aac);
        }
    }

    [Test]
    public void Row11ATwoLetterDiscAbbreviationIsClaimedNextToARaster()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.BracketedDiscAbbreviation);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.StatedResolution.Should().Be(720);
        }
    }

    [Test]
    public void Row12PixelDimensionsAreARasterClaim()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.PixelDimensions);

        using (new AssertionScope())
        {
            evidence.StatedResolution.Should().Be(1080);
            evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.Raster);
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.Flac);
            evidence.Container.Should().Be(".mkv");
        }
    }

    [Test]
    public void Row13ASourceWeldedToARasterYieldsBothAndTheIssueNumberIsRead()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.CompactSourceAndRaster);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.StatedResolution.Should().Be(1080);
            evidence.Version.Should().Be(2);
            evidence.Container.Should().Be(".mkv");
        }
    }

    [Test]
    public void Row14AnExplicitWholeDiscStatementIsRead()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.StatedDiscImage);

        using (new AssertionScope())
        {
            evidence.PackagingToken.Should().Be(EvidencePackagingTokens.DiscImage);
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
        }
    }

    [Test]
    public void Row15ATitleThatOnlyLooksLikeADiscImageStatesNoPackaging()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.NotADiscImage);

        using (new AssertionScope())
        {
            evidence.PackagingToken.Should().BeNull();
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.StatedResolution.Should().Be(1080);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.DtsHdMasterAudio);
        }
    }

    [Test]
    public void Row16AWorkTitleThatOnlyLooksLikeADiscImageStatesNoPackagingEither()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.WorkTitleNotADiscImage);

        using (new AssertionScope())
        {
            evidence.PackagingToken.Should().BeNull();
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.StatedResolution.Should().BeNull();
            evidence.IsRemux.Should().BeFalse();
            evidence.Languages.Should().NotContain(claim => claim.IsDualLanguageMarker);
        }
    }

    [Test]
    public void Row17ADualLanguageMarkerBesideARipTokenStillReportsBoth()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.DualLanguageDiscRip);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.UltraHighDefinitionDiscRip);
            evidence.StatedResolution.Should().Be(1080);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.Av1);
            evidence.DynamicRangeTokens.Should().Equal(EvidenceDynamicRangeTokens.HighDynamicRange10);
            evidence.Languages.Should().Contain(claim => claim.IsDualLanguageMarker);
        }
    }

    [Test]
    public void Row18ACompoundDownConversionTokenIsItsOwnSource()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.DiscDownConversion);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.UltraHighDefinitionDiscDownConvert);
            evidence.StatedResolution.Should().Be(1080);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H264);
        }
    }

    [Test]
    public void Row19ALineCountBeatsAMarketingNameInTheSameTitle()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.TwoRasterClaims);

        using (new AssertionScope())
        {
            evidence.StatedResolution.Should().Be(1080);
            evidence.StatedResolutionForm.Should().Be(ResolutionClaimForm.LineCount);
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.BluRayDisc);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.EnhancedAc3);
            evidence.Container.Should().Be(".mkv");
        }
    }

    [Test]
    public void Row20AHighDefinitionDvdRipIsItsOwnSource()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.HighDefinitionDvdRip);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.HighDefinitionDvdRip);
            evidence.StatedResolution.Should().Be(1080);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H264);
        }
    }

    [Test]
    public void Row21ADistributorCodeIsCapturedBesideAStreamSource()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.DistributorBesideStream);

        using (new AssertionScope())
        {
            evidence.DistributorToken.Should().Be(EvidenceDistributorTokens.Amazon);
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.WebRip);
            evidence.StatedResolution.Should().Be(1080);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H265);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.EnhancedAc3);
        }
    }

    [Test]
    public void Row22TwoDynamicRangeFormatsAreBothRead()
    {
        var scanned = EvidenceScanFixtures.Scan(EvidenceScanFixtures.TwoDynamicRanges);

        using (new AssertionScope())
        {
            scanned.DynamicRangeTokens.Should().BeEquivalentTo(
                [EvidenceDynamicRangeTokens.DolbyVision, EvidenceDynamicRangeTokens.HighDynamicRange10Plus]);
            scanned.SourceToken.Should().Be(EvidenceSourceTokens.Web);
            scanned.StatedResolution.Should().Be(2160);
            scanned.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H265);
            scanned.Languages.Should().Contain(claim => claim.Language.Code == "de");
            scanned.Languages.Should().Contain(claim => claim.IsDualLanguageMarker);
        }
    }

    [Test]
    public void Row23ASelfDescribingSeveralLanguagesMarkerNeedsNoNamedLanguage()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.SeveralLanguagesMarker);

        using (new AssertionScope())
        {
            evidence.Languages.Should().ContainSingle().Which.IsDualLanguageMarker.Should().BeTrue();
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.Web);
            evidence.StatedResolution.Should().Be(1080);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H264);
        }
    }

    [Test]
    public void Row24ABareStreamWordIsClaimedWhenARasterSupportsIt()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.SupportedBareStreamWord);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.Web);
            evidence.DistributorToken.Should().Be(EvidenceDistributorTokens.Amazon);
            evidence.StatedResolution.Should().Be(1080);
        }
    }

    [Test]
    public void Row25AReleaseSelectionMarkerContributesNothingToQuality()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.SelectionMarkerIsNotQuality);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.WebRip);
            evidence.StatedResolution.Should().Be(1080);
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H264);
            evidence.FlawTokens.Should().BeEmpty();
            evidence.PackagingToken.Should().BeNull();
            evidence.DistributorToken.Should().BeNull();
        }
    }

    [Test]
    public void Row26ASourceSplitBySeparatorsIsStillOneSource()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.SeparatedBroadcastSource);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.HighDefinitionBroadcast);
            evidence.StatedResolution.Should().Be(2160);
        }
    }

    [Test]
    public void Row27ASecondRepackIsTheThirdIssue()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.SecondRepack);

        using (new AssertionScope())
        {
            evidence.Version.Should().Be(3);
            evidence.IsRepack.Should().BeTrue();
            evidence.RealCount.Should().Be(0);
            evidence.StatedResolution.Should().Be(720);
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.HighDefinitionBroadcast);
        }
    }

    [Test]
    public void Row28BurnedInSubtitlesAreADefect()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.BurnedInSubtitles);

        using (new AssertionScope())
        {
            evidence.FlawTokens.Should().Contain(EvidenceFlawTokens.HardcodedSubtitles);
            evidence.SourceToken.Should().Be(EvidenceSourceTokens.WebRip);
            evidence.StatedResolution.Should().Be(1080);
            evidence.AudioToken.Should().Be(EvidenceAudioTokens.Aac);
        }
    }

    [Test]
    public void Row29ACodecAloneStatesOnlyACodec()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.CodecAlone);

        using (new AssertionScope())
        {
            evidence.VideoCodecToken.Should().Be(EvidenceVideoCodecTokens.H264);
            evidence.SourceToken.Should().BeNull();
            evidence.StatedResolution.Should().BeNull();
        }
    }

    [Test]
    public void Row30AWorkTitleContainingVocabularyStatesNothing()
    {
        var evidence = EvidenceScanFixtures.Scan(EvidenceScanFixtures.WorkTitleContainingVocabulary);

        using (new AssertionScope())
        {
            evidence.SourceToken.Should().BeNull();
            evidence.StatedResolution.Should().BeNull();
            evidence.VideoCodecToken.Should().BeNull();
            evidence.AudioToken.Should().BeNull();
            evidence.IsRemux.Should().BeFalse();
        }
    }

    [Test]
    public void EveryMappedRowIsScannedWithoutThrowing()
    {
        var rows = new[]
        {
            EvidenceScanFixtures.InterlacedDiscBitstream,
            EvidenceScanFixtures.DiscBitstreamAt720,
            EvidenceScanFixtures.DualLanguageDiscEncode,
            EvidenceScanFixtures.VeryVersatileCodec,
            EvidenceScanFixtures.MarketingRasterOnly,
            EvidenceScanFixtures.OrphanBitstreamClaim,
            EvidenceScanFixtures.OrphanBitstreamBracketed,
            EvidenceScanFixtures.BroadcastTransportStream,
            EvidenceScanFixtures.IntermediateRaster,
            EvidenceScanFixtures.BracketedStreamMarker,
            EvidenceScanFixtures.BracketedDiscAbbreviation,
            EvidenceScanFixtures.PixelDimensions,
            EvidenceScanFixtures.CompactSourceAndRaster,
            EvidenceScanFixtures.StatedDiscImage,
            EvidenceScanFixtures.NotADiscImage,
            EvidenceScanFixtures.WorkTitleNotADiscImage,
            EvidenceScanFixtures.DualLanguageDiscRip,
            EvidenceScanFixtures.DiscDownConversion,
            EvidenceScanFixtures.TwoRasterClaims,
            EvidenceScanFixtures.HighDefinitionDvdRip,
            EvidenceScanFixtures.DistributorBesideStream,
            EvidenceScanFixtures.TwoDynamicRanges,
            EvidenceScanFixtures.SeveralLanguagesMarker,
            EvidenceScanFixtures.SupportedBareStreamWord,
            EvidenceScanFixtures.SelectionMarkerIsNotQuality,
            EvidenceScanFixtures.SeparatedBroadcastSource,
            EvidenceScanFixtures.SecondRepack,
            EvidenceScanFixtures.BurnedInSubtitles,
            EvidenceScanFixtures.CodecAlone,
            EvidenceScanFixtures.WorkTitleContainingVocabulary,
        };

        // The count is asserted so that a row quietly dropped from the fixture fails here rather than
        // silently shrinking the acceptance test.
        rows.Should().HaveCount(30);
        rows.Select(EvidenceScanFixtures.Scan).Should().OnlyContain(evidence => evidence.Version >= 1);
    }
}
