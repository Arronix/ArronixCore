// Exercises the declarative media-kind area and the shape vocabulary its rows key on.
#pragma warning disable ARX0019
#pragma warning disable ARX0013

using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Definition;

[TestFixture]
public class MatchDeclarationTests
{
    [Test]
    public void UnitResolutionCanDeclareATitleLookup()
    {
        // Coordinates are not the only way a release addresses a unit: when numbering fails, the release
        // may name the unit by its title, and only the store-holding match engine can run that lookup.
        var attempt = new SpaceAttempt
        {
            SpaceId = "canonical",
            Kind = SpaceAttemptKind.TitleLookup,
            NormalizerId = "strip-non-alnum-upper"
        };

        var rule = new UnitResolutionRule { Spaces = [attempt] };

        Assert.Multiple(() =>
        {
            Assert.That(rule.Spaces[0].Kind, Is.EqualTo(SpaceAttemptKind.TitleLookup));
            Assert.That(rule.Spaces[0].NormalizerId, Is.EqualTo("strip-non-alnum-upper"));
            Assert.That(rule.Expansion, Is.EqualTo(SpanExpansion.None));
        });
    }

    [Test]
    public void SpaceAttemptDefaultsToCoordinateResolution()
    {
        var attempt = new SpaceAttempt { SpaceId = "canonical" };

        Assert.That(attempt.Kind, Is.EqualTo(SpaceAttemptKind.Coordinate));
    }

    [Test]
    public void MatchLayerCanHintTheSpaceItsSpellingVerified()
    {
        // Which spelling matched carries numbering information: an entry matched through a community
        // alias should try that community's numbering space first.
        var layer = new MatchLayer
        {
            LayerId = "alias-spellings",
            KeyTemplate = "{aliasTitles}",
            NormalizerId = "strip-non-alnum-upper",
            PreferSpaceId = "community"
        };

        Assert.That(layer.PreferSpaceId, Is.EqualTo("community"));
    }

    [Test]
    public void VariantChoiceTunesHostCatalogedFeaturesRatherThanDeclaringThem()
    {
        // Weights and thresholds are data; what a feature computes is host code. The row deliberately
        // has no operator and no subject path.
        var choice = new VariantChoiceDeclaration
        {
            FeatureCatalogId = "candidate-distance",
            Features =
            [
                new FeatureParameter { FeatureId = "title-containment" },
                new FeatureParameter { FeatureId = "year-agreement", Weight = 0.5 },
                new FeatureParameter { FeatureId = "catalog-preference", Enabled = false }
            ]
        };

        Assert.Multiple(() =>
        {
            Assert.That(choice.Features[0].Enabled, Is.True);
            Assert.That(choice.Features[0].Weight, Is.EqualTo(1.0));
            Assert.That(choice.Features[0].Threshold, Is.Null);
            Assert.That(typeof(FeatureParameter).GetProperty("Operator"), Is.Null);
            Assert.That(typeof(FeatureParameter).GetProperty("Subject"), Is.Null);
        });
    }

    [Test]
    public void ConfidenceRulesKeyOnWhatTheMatchWasMadeOn()
    {
        var byIdentifier = new ConfidenceRule(MatchBasis.Identifier, null, MatchConfidence.Exact);
        var byTitleFromFiles = new ConfidenceRule(
            MatchBasis.TitleOnly,
            CoordinateConfidence.Verified,
            MatchConfidence.Medium,
            SourceIn: [MatchSource.FileName, MatchSource.FolderName]);

        Assert.Multiple(() =>
        {
            Assert.That(byIdentifier.SourceIn, Is.Null, "Null provenance means the row applies everywhere.");
            Assert.That(byTitleFromFiles.SourceIn, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void EntryResolutionDefaultsToScopeReplacingSearchAndRejectionOnAmbiguity()
    {
        var entry = new EntryResolution
        {
            IdentifierOrder = ["catalog"],
            Layers = [new MatchLayer { LayerId = "own", KeyTemplate = "{title}", NormalizerId = "plain" }]
        };

        Assert.Multiple(() =>
        {
            Assert.That(entry.ScopeReplacesSearch, Is.True);
            Assert.That(entry.Ambiguity, Is.EqualTo(AmbiguityPolicy.Reject));
        });
    }
}
