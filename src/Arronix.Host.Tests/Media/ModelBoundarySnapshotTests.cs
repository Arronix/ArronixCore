using System.Collections;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Host.Tests.Media;

/// <summary>
/// The derived model the engines run is rebuilt from host-owned values before it is retained.
/// </summary>
/// <remarks>
/// The engines read this model outside any invocation lease, so a lazy or extension-defined collection in
/// it would run extension code with no ticket held, and would keep the extension's collectible context
/// alive until the kind is withdrawn. The graph here populates every nested collection-bearing path, so a
/// shallow copy cannot pass.
/// </remarks>
[TestFixture]
public class ModelBoundarySnapshotTests
{
    [Test]
    public void EveryNestedCollectionInTheModelIsRebuilt()
    {
        var model = Hostile();

        var snapshot = ModelBoundary.Snapshot(model);

        using (new AssertionScope())
        {
            ForeignTypes(snapshot).Should().BeEmpty("nothing an extension supplied may survive in the model");
            snapshot.Should().BeEquivalentTo(model, "and it must still say exactly what was derived");
        }
    }

    [Test]
    public void TheTwoDelegatesTheContractDeclaresAreKeptRatherThanCopied()
    {
        var model = Hostile();

        var snapshot = ModelBoundary.Snapshot(model);

        using (new AssertionScope())
        {
            snapshot.Respace.Should().BeSameAs(model.Respace);
            snapshot.TemplateRules[0].IsSatisfied.Should().BeSameAs(model.TemplateRules[0].IsSatisfied);
            snapshot.TemplateRules.Should().NotBeOfType<Foreign<TemplateRequirement>>(
                "the list holding them is still the host's");
        }
    }

    [Test]
    public void AShallowCopyOfTheModelIsNotEnough()
    {
        // The guard on this file: if a nested path stops being copied, the walk below finds the extension's
        // own collection type still sitting in the retained model.
        var shallow = Hostile() with { };

        ForeignTypes(shallow).Should().NotBeEmpty();
    }

    private static MediaKindModel Hostile()
        => new()
        {
            Parsing = new ParseDeclaration
            {
                Normalization = new NormalizationOptions
                {
                    LeadingArticles = new Foreign<string>(["the"]),
                    StopWords = new Foreign<string>(["a"]),
                    Transliterations = new Foreign<TransliterationRule>([]),
                    QueryRewrites = new Foreign<RewriteRule>([]),
                },
                PreRewrites = new Foreign<RewriteRule>([]),
                TitlePatterns = new Foreign<TitlePattern>(
                [
                    new TitlePattern
                    {
                        PatternId = "title",
                        Regex = ".*",
                        Sources = new Foreign<MatchSource>([]),
                        Captures = new Foreign<CaptureBinding>([]),
                        Guards = new Foreign<GuardRef>([]),
                    },
                ]),
                Guards = new Foreign<GuardPattern>([]),
                TokenTables = new Foreign<TokenTable>(
                [
                    new TokenTable { TableId = "t", Rows = new Foreign<TokenRow>([]) },
                ]),
                EscapeIds = new Foreign<string>([]),
            },
            Matching = new MatchDeclaration
            {
                Entry = new EntryResolution
                {
                    IdentifierOrder = new Foreign<string>(["tmdb"]),
                    Layers = new Foreign<MatchLayer>(
                    [
                        new MatchLayer
                        {
                            LayerId = "l",
                            KeyTemplate = "{title}",
                            NormalizerId = "n",
                            ExpanderIds = new Foreign<string>(["e"]),
                        },
                    ]),
                    Agreements = new Foreign<AgreementRule>(
                    [
                        new AgreementRule
                        {
                            RuleId = "a",
                            Subject = "s",
                            AgreesWith = new Foreign<string>(["b"]),
                        },
                    ]),
                },
                Units = new Foreign<UnitResolutionRule>(
                [
                    new UnitResolutionRule { Spaces = new Foreign<SpaceAttempt>([]) },
                ]),
                Confidence = new Foreign<ConfidenceRule>(
                [
                    new ConfidenceRule(
                        MatchBasis.Identifier,
                        null,
                        MatchConfidence.High,
                        new Foreign<MatchSource>([MatchSource.ReleaseName])),
                ]),
                Variant = new VariantChoiceDeclaration
                {
                    FeatureCatalogId = "c",
                    Features = new Foreign<FeatureParameter>([]),
                },
            },
            Querying = new QueryDeclaration
            {
                Tiers = new Foreign<QueryTierTemplate>(
                [
                    new QueryTierTemplate
                    {
                        TierId = "t",
                        SearchKindId = "s",
                        Origins = new Foreign<SearchOrigin>([]),
                        Arguments = new Foreign<QueryArgument>([]),
                        RequiredFields = new Foreign<string>([]),
                    },
                ]),
                Aliases = new Foreign<AliasTemplate>([]),
                Grammar = new CoordinateGrammar { Spellings = new Foreign<CoordinateSpelling>([]) },
                Limits = new Foreign<OriginLimit>([]),
                Substitutions = new Foreign<CreditSubstitution>([]),
            },
            Naming = new NamingDeclaration
            {
                FolderSpine = "{root}",
                DefaultTemplates = new ForeignMap<string, string> { ["file"] = "{title}" },
                Selection = new Foreign<TemplateSelectionRule>(
                [
                    new TemplateSelectionRule
                    {
                        RuleId = "r",
                        When = new TagPredicate(new Foreign<PredicateAtom>(
                        [
                            new PredicateAtom
                            {
                                Subject = "s",
                                Op = PredicateOp.In,
                                Values = new Foreign<string>(["v"]),
                            },
                        ])),
                    },
                ]),
                MultiUnitStyles = new Foreign<MultiUnitStyle>([]),
                Fallbacks = new Foreign<TokenFallbackRule>(
                [
                    new TokenFallbackRule { Token = "t", Order = new Foreign<string>(["a"]) },
                ]),
            },
            Notifications = new NotificationDeclaration
            {
                Fields = new Foreign<SummaryFieldRule>([]),
                GroupSummaries = new Foreign<GroupSummaryRule>(
                [
                    new GroupSummaryRule
                    {
                        AxisId = "g",
                        HeadlineTemplate = "{title}",
                        Fields = new Foreign<SummaryFieldRule>([]),
                    },
                ]),
            },
            TemplateRules = new Foreign<TemplateRequirement>(
            [
                new TemplateRequirement("rule", "requirement", static _ => true),
            ]),
            Respace = static text => text,
        };

    /// <summary>Every type reachable from the model that this assembly, rather than the contract, defines.</summary>
    private static IReadOnlyList<Type> ForeignTypes(object root)
    {
        var found = new List<Type>();
        Walk(root, found, depth: 0);
        return [.. found.Where(type => type.Assembly == typeof(ModelBoundarySnapshotTests).Assembly)];
    }

    private static void Walk(object? value, List<Type> found, int depth)
    {
        if (value is null or string or Delegate || depth > 12)
        {
            return;
        }

        found.Add(value.GetType());

        if (value is IEnumerable sequence)
        {
            foreach (var element in sequence)
            {
                Walk(element is DictionaryEntry entry ? entry.Value : element, found, depth + 1);
            }

            return;
        }

        if (value.GetType().Namespace?.StartsWith("Arronix.Abstractions", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var property in value.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
        {
            Walk(property.GetValue(value), found, depth + 1);
        }
    }

    /// <summary>A sequence defined outside the contract assembly.</summary>
    private sealed class Foreign<TValue>(IReadOnlyList<TValue> values) : IReadOnlyList<TValue>
    {
        public int Count => values.Count;

        public TValue this[int index] => values[index];

        public IEnumerator<TValue> GetEnumerator() => values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A map defined outside the contract assembly.</summary>
    private sealed class ForeignMap<TKey, TValue> : Dictionary<TKey, TValue>
        where TKey : notnull;
}
