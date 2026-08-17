using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed;
using FluentAssertions;

// Every contract these tests read is experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019
#pragma warning disable ARX0020

namespace Arronix.Host.Tests.TypedMedia;

/// <summary>
/// The sections a later iteration types, and what the typed surface already buys inside them.
/// </summary>
/// <remarks>
/// These declarations are carried rather than replaced, so the thing worth asserting is not that they exist
/// but that every reference into the item inside them came from an expression: a key template, a required
/// field, a free-text query and a summary headline are all written as code and end up as the strings the
/// existing engines read. That is what makes a rename a rename instead of a load failure.
/// </remarks>
[TestFixture]
internal sealed class CarriedModelTests
{
    private static MediaKindModel Model => MediaTypeModelFactory.Build<Work, Works>().Model;

    [Test]
    public void MatchLayersDeriveTheirKeyTemplateFromAnExpression()
    {
        var layers = Model.Matching.Entry.Layers;

        Assert.Multiple(() =>
        {
            layers.Select(layer => layer.LayerId).Should().Equal("own-title", "roman-rewrite");
            layers[0].KeyTemplate.Should().Be("{title}|{originalTitle}");
            layers[0].ExpanderIds.Should().BeEmpty();
            layers[1].KeyTemplate.Should().Be("{title}");
            layers[1].ExpanderIds.Should().Equal("roman-numeral-variants");
        });
    }

    [Test]
    public void AnAgreementRuleNamesTheItemSideByExpression()
    {
        var rule = Model.Matching.Entry.Agreements.Should().ContainSingle().Subject;

        Assert.Multiple(() =>
        {
            rule.Subject.Should().Be("reading.TitleYear");
            rule.AgreesWith.Should().Equal("year");
            rule.AbsentAgrees.Should().BeTrue();
            rule.MinimumValue.Should().Be(1800);
        });
    }

    [Test]
    public void ProviderReliabilityKnowledgeIsNotCarriedAtAll() =>
        // Which catalog to trust first when several answer is host configuration over the installed
        // catalogers, not a list inside a media kind.
        Model.Matching.Entry.IdentifierOrder.Should().BeEmpty();

    [Test]
    public void UnitResolutionIsNotRestatedBecauseTheFileBindingAlreadySaidIt() =>
        Model.Matching.Entry.ScopeReplacesSearch.Should().BeTrue();

    [Test]
    public void ATierRequiringAnIdentifierRequiresARoleRatherThanAScheme()
    {
        var tier = Model.Querying.Tiers
            .Single(candidate => string.Equals(candidate.TierId, "identifier", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            // The whole of what this buys: identifier search works with whichever cataloger is installed
            // rather than only with the one the kind happened to be written against.
            tier.RequiredFields.Should().Equal("identity.primaryWork");
            tier.Arguments.Select(argument => argument.Template)
                .Should().Equal("{identity.primaryWork}", "{identity.secondaryWork}");
            tier.Arguments[1].OmitWhenAbsent.Should().BeTrue();
            tier.Arguments.Should().OnlyContain(argument => argument.Term == SearchTerm.ExternalIdentifier);
            tier.FreeTextTemplate.Should().Be("{title}");
            tier.CarryAliases.Should().BeTrue();
        });
    }

    [Test]
    public void AnInterpolatedQueryBecomesTheTemplateTheEngineReads()
    {
        var tier = Model.Querying.Tiers
            .Single(candidate => string.Equals(candidate.TierId, "text", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            tier.FreeTextTemplate.Should().Be("{title} {year}");
            tier.RequiredFields.Should().Equal("year");
            tier.FanOutPerAlias.Should().BeTrue();
            tier.Order.Should().Be(2);
        });
    }

    [Test]
    public void ASweepTierNamesNothingAndKeepsItsOrigin()
    {
        var tier = Model.Querying.Tiers
            .Single(candidate => string.Equals(candidate.TierId, "sweep", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            tier.FreeTextTemplate.Should().BeEmpty();
            tier.Arguments.Should().BeEmpty();
            tier.Origins.Should().Equal(SearchOrigin.Rss);
        });
    }

    [Test]
    public void HostSearchPolicyIsNotCarriedPerKind()
    {
        Assert.Multiple(() =>
        {
            Model.Querying.Limits.Should().BeEmpty();
            Model.Querying.Substitutions.Should().BeEmpty();
            Model.Querying.Grammar.Should().Be(CoordinateGrammar.None);
        });
    }

    [Test]
    public void AnAliasRowKeepsItsRefinements()
    {
        var alias = Model.Querying.Aliases
            .Single(candidate =>
                string.Equals(candidate.AliasId, "translated-titles", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            alias.FilterByAcceptedLanguages.Should().BeTrue();
            alias.NeverOwnQuery.Should().BeTrue();

            // The recorded limitation: a projection over a filtered list derives to the list's own path and
            // the predicate is lost, because the declaration being derived into has no slot for one.
            alias.Template.Should().Be("{alternateTitles}");
        });
    }

    [Test]
    public void NamingCarriesTheTemplatesTheSpineAndTheGroupSegmentRule()
    {
        var naming = Model.Naming;

        Assert.Multiple(() =>
        {
            naming.DefaultTemplates["file"].Should().Be("{Work Title} ({Work Year})");
            naming.DefaultTemplates["workCollection-folder"].Should().Be("{WorkCollection TitleThe}");
            naming.FolderSpine.Should().Be("{root}/[workCollection-folder/]{folder}");
            naming.Selection.Should().ContainSingle()
                .Which.InsertSpineSegment.Should().Be("workCollection-folder");
            naming.MultiUnitStyles.Should().BeEmpty();
            naming.Fallbacks.Select(fallback => fallback.Token).Should().Contain("originalTitle");
        });
    }

    [Test]
    public void AFileTemplateRuleIsAPredicateRatherThanAPerTokenFlag()
    {
        // The rule is a disjunction with an exclusivity between its branches, which is precisely what a
        // per-token "is required" boolean could not express.
        var rule = Model.TemplateRules.Should().ContainSingle().Subject;

        Assert.Multiple(() =>
        {
            rule.RuleId.Should().Be("names-the-work");
            rule.IsSatisfied(Facts("title", "year")).Should().BeTrue();
            rule.IsSatisfied(Facts("originalTitle")).Should().BeTrue();
            rule.IsSatisfied(Facts("title", "year", "originalTitle")).Should().BeFalse();
            rule.IsSatisfied(Facts("title")).Should().BeFalse();
        });
    }

    [Test]
    public void TheSummaryNamesTheItemAndNoDeepLinkOrCatalogAddress()
    {
        var summary = Model.Notifications;

        Assert.Multiple(() =>
        {
            summary.HeadlineTemplate.Should().Be("{title} ({year})");
            summary.HeadlineMaxLength.Should().Be(200);
            summary.BodyFieldId.Should().Be("overview");
            summary.Fields.Should().ContainSingle().Which.Template.Should().Be("{runtime}");

            // Four members the derivation used to set to empty are gone from the contract outright: the
            // deep link, because the host's routing scheme is not a media kind's business; the catalog
            // addresses, because they belong to whoever owns the identifier; and the occasion phrases and
            // artwork role order, because they are the same for every kind and so are host-owned. Absence
            // is asserted structurally below rather than as an empty collection here, because a member
            // that cannot be set is a stronger statement than one that happens not to be.
            summary.GroupSummaries.Should().ContainSingle()
                .Which.HeadlineTemplate.Should().Be("{title}");
        });
    }

    /// <summary>
    /// The summary section has nowhere to put a route, a vendor address, an English phrase table or an
    /// artwork ordering.
    /// </summary>
    /// <remarks>
    /// The successor to four emptiness assertions. Each of those could only say that this kind declined to
    /// use a member; this says no kind can. Deep links are the host's routing scheme, catalog addresses
    /// belong to whoever owns the identifier, and the occasion phrases and artwork order were identical for
    /// every kind — twelve rows of English apiece, of which not one was about the media.
    /// </remarks>
    [Test]
    public void TheSummarySectionCannotCarryARouteAVendorAddressOrAPhraseTable()
    {
        var members = typeof(NotificationDeclaration)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        members.Should().NotContain(["DeepLinkTemplate", "LinkTemplates", "Occasions", "ArtworkRoleOrder"]);

        typeof(GroupSummaryRule)
            .GetProperties()
            .Select(property => property.Name)
            .Should().NotContain("ArtworkRoleOrder");
    }

    [Test]
    public void QualityCarriesItsDefaultsAndLeavesTheUnreachableRuleAlone()
    {
        Assert.Multiple(() =>
        {
            Model.Quality.Defaults.Should().HaveCount(2);
            Model.Quality.Defaults.Should().OnlyContain(row => row.IgnoreStatedResolution);
            Model.Quality.Fallback.Should().Be(RungFallback.RoundUp);
        });
    }

    [Test]
    public void TheReleaseModelsAndTheCorpusAreCarriedVerbatim()
    {
        Assert.Multiple(() =>
        {
            Model.Parsing.TitlePatterns.Should().ContainSingle()
                .Which.PatternId.Should().Be("title-year");

            // A strategy is a method: what used to be a binding, a role, a parameter dictionary, a
            // requirement row and a load-time resolution rule is a delegate.
            Model.Respace.Should().NotBeNull();
            Model.Respace!("S.W.A.T").Should().Be("S W A T");
        });
    }

    private static INamingTemplateFacts Facts(params string[] fieldIds) => new StubFacts(fieldIds);

    private sealed class StubFacts(IReadOnlyList<string> fieldIds) : INamingTemplateFacts
    {
        public bool HasField(string fieldId) => fieldIds.Contains(fieldId, StringComparer.Ordinal);

        public bool Has(FileFact fact) => false;
    }
}
