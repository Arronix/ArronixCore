using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music.Tests.Policies;

/// <summary>
/// Exercises the four behavior seams and the naming policy against the same shape they are declared
/// alongside.
/// </summary>
[TestFixture]
public class PolicyTests
{
    private readonly MusicReleaseParser _parser = new();
    private readonly MusicReleaseMatcher _matcher = new();
    private readonly MusicQueryPlanner _planner = new();
    private readonly MusicQualityModel _quality = new();

    [Test]
    public void EverySeamServesTheSameKind()
    {
        Assert.That(_parser.MediaKind, Is.EqualTo(MusicShape.Kind));
        Assert.That(_matcher.MediaKind, Is.EqualTo(MusicShape.Kind));
        Assert.That(_planner.MediaKind, Is.EqualTo(MusicShape.Kind));
        Assert.That(_quality.MediaKind, Is.EqualTo(MusicShape.Kind));
    }

    [TestCase("Radiohead - OK Computer (1997) [FLAC]", "OK Computer", "1997", "FLAC")]
    [TestCase("Miles Davis - Kind of Blue (1959) [MP3-320]", "Kind of Blue", "1959", "MP3")]
    public void AConventionalNameReadsIntoItsParts(
        string releaseName,
        string expectedTitle,
        string expectedYear,
        string expectedCodec)
    {
        var parsed = _parser.Parse(releaseName);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Title, Is.EqualTo(expectedTitle));
        Assert.That(parsed.Year, Is.EqualTo(expectedYear));
        Assert.That(parsed.AdditionalMetadata![MusicReleaseParser.CodecKey], Is.EqualTo(expectedCodec));
        Assert.That(parsed.MediaKind, Is.EqualTo(MusicShape.Kind));
    }

    [Test]
    public void ANameWithNoCreditIsRefusedRatherThanGuessed()
    {
        Assert.That(_parser.Parse("SomeRandomString"), Is.Null);
        Assert.That(_parser.CanParse("SomeRandomString"), Is.False);
    }

    [Test]
    public async Task AMatchResolvesToAWholeRunningOrder()
    {
        var outcome = await _matcher.MatchAsync(new MatchRequest
        {
            MediaKind = MusicShape.Kind,
            Text = "Radiohead - OK Computer (1997) [FLAC]",
            Source = MatchSource.ReleaseName,
        });

        Assert.That(outcome.Units, Is.Not.Empty);
        Assert.That(outcome.Units.Select(unit => unit.Level), Is.All.EqualTo(MusicShape.RecordingLevel));
        Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.High));
    }

    [Test]
    public async Task AYearInTheNameSelectsTheLaterPressingAndSaysSo()
    {
        var outcome = await _matcher.MatchAsync(new MatchRequest
        {
            MediaKind = MusicShape.Kind,
            Text = "Radiohead - OK Computer (2017) [FLAC]",
            Source = MatchSource.ReleaseName,
        });

        // Seventeen recordings rather than twelve: the year picked the expanded pressing, and the warning
        // says that accepting this changes what completeness counts against.
        Assert.That(outcome.Units, Has.Count.EqualTo(17));
        Assert.That(outcome.Warnings, Is.Not.Empty);
    }

    [Test]
    public async Task AnUnknownTitleIsRejectedWithAReason()
    {
        var outcome = await _matcher.MatchAsync(new MatchRequest
        {
            MediaKind = MusicShape.Kind,
            Text = "Nobody - Nothing At All (2001) [FLAC]",
            Source = MatchSource.ReleaseName,
        });

        Assert.That(outcome.Units, Is.Empty);
        Assert.That(outcome.Confidence, Is.EqualTo(MatchConfidence.None));
        Assert.That(outcome.RejectionReason, Is.Not.Null);
    }

    [Test]
    public async Task TheQueryPlanLeadsWithIdentifiersAndFallsBackToText()
    {
        var plan = await _planner.PlanAsync(new AcquisitionRequest
        {
            MediaKind = MusicShape.Kind,
            SearchKindId = MusicShape.WorkSearchKindId,
            Units = [new MediaItemRef(MusicShape.Kind, MusicShape.WorkLevel, MediaItemId.FromInt64(101))],
            Origin = SearchOrigin.UserInvoked,
        });

        Assert.That(plan.Tiers, Has.Count.EqualTo(3));
        Assert.That(
            plan.Tiers[0].Queries[0].Arguments.Any(argument => argument.Term == SearchTerm.ExternalIdentifier),
            Is.True);
        Assert.That(plan.Tiers[2].Queries[0].Aliases, Is.Not.Empty);
    }

    [Test]
    public async Task EveryPlannedQueryUsesOnlyTermsTheSearchKindAdmits()
    {
        var searchKind = MusicShape.Declaration.SearchKinds
            .Single(kind => string.Equals(
                kind.SearchKindId,
                MusicShape.WorkSearchKindId,
                System.StringComparison.Ordinal));

        var admitted = searchKind.RequiredTerms.Concat(searchKind.OptionalTerms).ToHashSet();

        var plan = await _planner.PlanAsync(new AcquisitionRequest
        {
            MediaKind = MusicShape.Kind,
            SearchKindId = MusicShape.WorkSearchKindId,
            Units = [new MediaItemRef(MusicShape.Kind, MusicShape.WorkLevel, MediaItemId.FromInt64(101))],
            Origin = SearchOrigin.Automatic,
        });

        foreach (var query in plan.Tiers.SelectMany(tier => tier.Queries))
        {
            foreach (var argument in query.Arguments)
            {
                Assert.That(admitted, Does.Contain(argument.Term));
            }

            Assert.That(
                query.Categories.Select(category => category.Value).ToList(),
                Is.EquivalentTo(searchKind.Categories.Select(category => category.Value).ToList()));
        }
    }

    [Test]
    public void QualityIsReadFromTheDeclaredLadderAndNowhereElse()
    {
        var parsed = _parser.Parse("Radiohead - OK Computer (1997) [FLAC 24bit]");

        Assert.That(parsed, Is.Not.Null);

        var tier = _quality.EvaluateQuality(parsed!);

        Assert.That(MusicQualityModel.Ladder, Does.Contain(tier));
        Assert.That(_quality.IsUpgrade(MusicQualityModel.Ladder[0], tier), Is.True);
        Assert.That(_quality.IsUpgrade(tier, MusicQualityModel.Ladder[0]), Is.False);
    }

    [Test]
    public void AnUnrecognizedCopyLandsOnTheFamilysUnknownTier()
    {
        var parsed = new ParsedRelease(
            MusicShape.Kind,
            "Something",
            AdditionalMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MusicReleaseParser.CodecKey] = "SHN",
            });

        Assert.That(_quality.EvaluateQuality(parsed), Is.EqualTo(MusicQualityModel.Unknown));
        Assert.That(MusicQualityModel.Ladder, Does.Not.Contain(MusicQualityModel.Unknown));
    }

    [Test]
    public void TheCeilingIsMetOnceTheLadderIsClimbedFarEnough()
    {
        var flac = MusicQualityModel.Ladder.Single(tier => tier.Name == "FLAC");
        var mp3 = MusicQualityModel.Ladder.Single(tier => tier.Name == "MP3-320");
        var cutoff = new CutoffPolicy(mp3);

        Assert.That(_quality.MeetsCutoff(flac, cutoff), Is.True);
        Assert.That(_quality.MeetsCutoff(MusicQualityModel.Unknown, cutoff), Is.False);
    }

    [Test]
    public void TheNamingTemplateIsChosenPerItemAndNotPerKind()
    {
        // A pressing on one carrier and a pressing on two want different names, and picking between them
        // is the extension's decision rather than a single configured string.
        Assert.That(
            MusicRenamePolicy.TemplateForPressing(201),
            Is.EqualTo(MusicRenamePolicy.SingleCarrierTemplate));

        Assert.That(
            MusicRenamePolicy.TemplateForPressing(202),
            Is.EqualTo(MusicRenamePolicy.MultiCarrierTemplate));
    }

    [Test]
    public async Task NamingResolvesTheCarrierAndPositionOfARecording()
    {
        var policy = new MusicRenamePolicy();
        var tokens = await policy.ResolveTokensAsync(MediaItemId.FromInt64(1013));

        Assert.That(tokens["{Album Title}"], Is.EqualTo("OK Computer"));
        Assert.That(tokens["{Artist Name}"], Is.EqualTo("Radiohead"));
        Assert.That(tokens["{Medium Number}"], Is.EqualTo("1"));

        var name = await policy.GenerateFileNameAsync(
            MediaItemId.FromInt64(1013),
            file: null,
            MusicRenamePolicy.SingleCarrierTemplate);

        Assert.That(name, Does.Contain(" - "));
        Assert.That(name, Does.Not.Contain("{"));
    }

    [Test]
    public async Task TheLibraryPathStopsAtTheAcquisitionLevel()
    {
        var layout = new MusicLibraryLayout();
        var path = await layout.GenerateFolderPathAsync(
            MediaItemId.FromInt64(101),
            new LibraryPathSpec("/library/music", MusicLibraryLayout.DefaultFolderTemplate));

        Assert.That(path, Does.Contain("Radiohead"));
        Assert.That(path, Does.Contain("OK Computer"));

        // The pressing never appears in a path: the selection can change and no file should move for it.
        Assert.That(path, Does.Not.Contain("OKNOTOK"));
    }
}
