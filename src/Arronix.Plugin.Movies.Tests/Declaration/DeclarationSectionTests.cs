
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;
using Arronix.Plugin.Movies.Definition;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Declaration;

/// <summary>
/// The derived model, section by section.
/// </summary>
/// <remarks>
/// These are the assertions the typed surface made possible. Where the string declaration could be read,
/// the model can be <i>checked against the type it came from</i>: a field identifier against a property
/// name, a key layer against an expression the compiler resolved, a summary row against a member access. In
/// return, each section below also pins what the derivation could not carry across, so the losses are
/// visible in every run rather than discovered later.
/// </remarks>
[TestFixture]
public class DeclarationSectionTests
{
    // ---- root -----------------------------------------------------------------------------------

    [Test]
    public void CatalogCandidatesCarryArtworkWithoutPrescribingALayout()
    {
        var workbench = MoviesDeclaration.Intent.Workbenches.Single(candidate =>
            string.Equals(candidate.WorkbenchId, "add-from-catalog", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(workbench.Subject, Is.EqualTo(WorkbenchSubject.CatalogCandidates));
            Assert.That(
                workbench.Columns.Single(column => column.Field.FieldId == "artwork").Field.ValueKind,
                Is.EqualTo(FieldValueKind.Artwork));
        });
    }

    [Test]
    public void CarriesEverySectionTheHostNeeds()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Shape, Is.Not.Null);
            Assert.That(MoviesDeclaration.Intent, Is.Not.Null);
            Assert.That(MoviesDeclaration.Carried.Parsing, Is.Null);
            Assert.That(MoviesDeclaration.Model.ParserType, Is.EqualTo(typeof(MovieReleaseParser)));
            Assert.That(MoviesDeclaration.Carried.Matching, Is.Not.Null);
            Assert.That(MoviesDeclaration.Carried.Querying, Is.Not.Null);
        });

    /// <summary>
    /// <b>The named-strategy mechanism is gone, and this is what dissolving it looks like.</b> A role
    /// identifier, a strategy identifier, a parameter dictionary, a requirement row, a host vocabulary
    /// entry and a load-time resolution rule existed so that an inert declaration could reach host
    /// behavior. A typed kind has methods, so the behavior is a method — and it is testable directly,
    /// which the parameter dictionary never was.
    /// </summary>
    [TestCase("S.W.A.T", "S.W.A.T")]
    [TestCase("U.S.A", "U.S.A")]
    [TestCase("Mission.Impossible.3", "Mission Impossible 3")]
    [TestCase("A.Team", "A Team")]
    [TestCase("Dr.No", "Dr No")]
    [TestCase("a.a", "a a")]
    public void BindsTheDottedTitleRewriteAsAMethodRatherThanAsANamedStrategy(string input, string expected)
        => Assert.Multiple(() =>
        {
            Assert.That(Movies.RespaceDottedAcronym(input), Is.EqualTo(expected));
        });

    /// <summary>
    /// Zero budgeted code escapes in the parse declaration: there is no escape identifier to declare and
    /// nothing to resolve at load, because a rewrite the kind needs is a method it already has.
    /// </summary>
    [Test]
    public void RegistersNoPerKindCodeEscape()
        => Assert.That(MoviesDeclaration.Carried.Parsing, Is.Null);

    /// <summary>
    /// <b>The hand-maintained coupling is gone.</b> The old declaration asked its author to track by hand
    /// the highest enumeration ordinal it used and to update the table whenever a row was added. Nothing
    /// asks for it now, because with a typed model the compiler already knows what the kind uses.
    /// </summary>
    [Test]
    public void MaintainsNoVocabularyTableByHand()
    {
        var members = typeof(MediaKindModel).GetProperties().Select(property => property.Name).ToList();

        Assert.That(
            members,
            Has.No.Member("Strategies").And.No.Member("RequiredVocabulary"),
            "there is no table to maintain because there is nowhere to put one");
    }

    // ---- parsing --------------------------------------------------------------------------------

    /// <summary>
    /// The media type declares the format by its typed representation. The wire descriptor carries only
    /// the stable family identity and extensions; executable representation and policy types stay on the
    /// typed runtime model.
    /// </summary>
    [Test]
    public void DeclaresVideoAsARepresentationRatherThanAHostQualityModel()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Video.Ladder, Is.Empty);
            Assert.That(MoviesDeclaration.Video.Unknown, Is.Null);
            Assert.That(MoviesDeclaration.Model.HasReleasePolicy, Is.True);
            Assert.That(
                MoviesDeclaration.Model.ReleaseType,
                Is.EqualTo(typeof(Release<Video>)));
        });

    [Test]
    public void HasNoHostInterpretedParserVocabulary()
        => Assert.That(MoviesDeclaration.Carried.Parsing, Is.Null);

    // ---- matching -------------------------------------------------------------------------------

    [Test]
    public void LayersTheTitleLookupSoAnAlternativeNeverOutranksAnActualTitle()
        => Assert.That(
            MoviesDeclaration.Carried.Matching.Entry.Layers.Select(static layer => layer.LayerId),
            Is.EqualTo(new[] { "own-title", "roman-rewrite", "alternative-titles", "translated-titles" }),
            "Declared order is the cascade: the actual title wins first and the translated spellings — "
            + "which two unrelated films very often share — lose last.");

    /// <summary>
    /// Each layer's key is an expression over the entity, so the compiler resolved it and a rename would
    /// have moved it. Three of the four derive exactly what the string surface spelled by hand.
    /// </summary>
    [Test]
    public void KeysEveryLayerOnAPropertyRatherThanOnAFieldNameNothingChecked()
    {
        var keys = MoviesDeclaration.Carried.Matching.Entry.Layers
            .ToDictionary(static layer => layer.LayerId, static layer => layer.KeyTemplate, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(keys["own-title"], Is.EqualTo("{title}|{originalTitle}"));
            Assert.That(keys["roman-rewrite"], Is.EqualTo("{title}"));
            Assert.That(keys["alternative-titles"], Is.EqualTo("{alternateTitles}"));
        });
    }

    /// <summary>
    /// <b>The one place a key came out wrong, pinned rather than hidden.</b> The translated layer's key is
    /// a projection out of a composite — the title component of each translation — and the declaration it
    /// derives into holds a template string with no way to name a component. So the key names the composite
    /// list, and an engine reading it gets whole translations where it wants their titles. The layer's
    /// <i>position</i> and its expansion are right, which is why the loss is this narrow; it closes when
    /// the match declaration is itself typed.
    /// </summary>
    [Test]
    public void CannotNameAComponentOfACompositeInAKeyTemplate()
        => Assert.That(
            MoviesDeclaration.Carried.Matching.Entry.Layers
                .Single(static layer => layer.LayerId == "translated-titles").KeyTemplate,
            Is.EqualTo("{translations}"),
            "Wanted: the title component of each translation. Available: the list.");

    /// <summary>
    /// <b>Provider-reliability knowledge has left the media kind.</b> Which catalog to trust first when
    /// several answer is a fact about installed catalogers, so the kind states no order at all.
    /// </summary>
    [Test]
    public void StatesNoIdentifierPrecedenceBecauseThatIsNotAMovieFact()
        => Assert.That(MoviesDeclaration.Carried.Matching.Entry.IdentifierOrder, Is.Empty);

    [Test]
    public void GuardsEveryMatchWithTheYearAgreementAndLetsAnAbsentYearPass()
    {
        var rule = MoviesDeclaration.Carried.Matching.Entry.Agreements.Single();

        Assert.Multiple(() =>
        {
            Assert.That(rule.Subject, Is.EqualTo("reading.TitleYear"));
            Assert.That(rule.AgreesWith, Is.EqualTo(new[] { "year", "secondaryYear" }));
            Assert.That(rule.AbsentAgrees, Is.True, "A missing year is common and harmless.");
            Assert.That(rule.MinimumValue, Is.EqualTo(1800), "Below this it is not a year.");
        });
    }

    [Test]
    public void ReplacesTheCatalogWideSearchWithACallerSuppliedScope()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Carried.Matching.Entry.ScopeReplacesSearch, Is.True);
            Assert.That(
                MoviesDeclaration.Carried.Matching.Entry.Ambiguity,
                Is.EqualTo(AmbiguityPolicy.Reject),
                "More than one surviving candidate is a refusal naming the contenders.");
        });

    /// <summary>
    /// <b>Neither row is written down, and neither has to be.</b> The unit row is derived from the
    /// structure — one coordinate space admits exactly one way to reach a unit, which the file binding
    /// already said — and the confidence table is host policy, because how far to trust an identifier
    /// against a bare title is the same question for every media kind. Movies states neither and gets both.
    /// </summary>
    [Test]
    public void StatesNoUnitRowAndNoConfidenceTableYetGetsBoth()
        => Assert.Multiple(() =>
        {
            Assert.That(
                MoviesDeclaration.Carried.Matching.Units.Single().Spaces.Single().SpaceId,
                Is.EqualTo("singleton"),
                "derived from the one coordinate space the shape carries");
            Assert.That(
                MoviesDeclaration.Carried.Matching.Confidence,
                Is.Empty,
                "host policy, supplied by MatchConfidencePolicy at the matcher rather than copied per kind");
        });

    // ---- querying -------------------------------------------------------------------------------

    [Test]
    public void PlansAnIdentifierTierBeforeATextTier()
    {
        var tiers = MoviesDeclaration.Carried.Querying.Tiers;

        Assert.Multiple(() =>
        {
            Assert.That(tiers.Single(static t => t.TierId == "identifier").Order, Is.EqualTo(1));
            Assert.That(tiers.Single(static t => t.TierId == "text").Order, Is.EqualTo(2));
            Assert.That(
                tiers.Single(static t => t.TierId == "sweep").Origins,
                Is.EqualTo(new[] { SearchOrigin.Rss }));
        });
    }

    /// <summary>
    /// <b>The second-worst coupling in the review, closed.</b> The identifier tier used to refuse to plan
    /// without one named vendor's identifier, so a library with any other cataloger installed could never
    /// be searched by identifier at all. It asks for the primary identity role now.
    /// </summary>
    [Test]
    public void AsksForTheIdentityRoleRatherThanForOneCatalogsIdentifier()
    {
        var identifier = MoviesDeclaration.Carried.Querying.Tiers
            .Single(static tier => tier.TierId == "identifier");

        Assert.Multiple(() =>
        {
            Assert.That(identifier.RequiredFields, Is.EqualTo(new[] { "identity.primaryWork" }));
            Assert.That(identifier.Arguments[0].Template, Is.EqualTo("{identity.primaryWork}"));
            Assert.That(identifier.Arguments[0].OmitWhenAbsent, Is.False);
            Assert.That(identifier.Arguments[1].Template, Is.EqualTo("{identity.secondaryWork}"));
            Assert.That(identifier.Arguments[1].OmitWhenAbsent, Is.True, "Not every movie has one.");

            foreach (var argument in identifier.Arguments)
            {
                Assert.That(argument.Scheme, Is.Null, "A tier names a role; a cataloger names a scheme.");
            }
        });
    }

    /// <summary>
    /// "Dune" alone is the worst query a movie search can make, so a text tier that cannot state a year
    /// does not plan at all. An unreleased film has nothing to search for yet.
    /// </summary>
    [Test]
    public void RefusesToPlanATextQueryWithoutAYear()
    {
        var text = MoviesDeclaration.Carried.Querying.Tiers.Single(static tier => tier.TierId == "text");

        Assert.Multiple(() =>
        {
            Assert.That(text.RequiredFields, Does.Contain("year"));
            Assert.That(text.FreeTextTemplate, Is.EqualTo("{title} {year}"));
            Assert.That(text.FanOutPerAlias, Is.True, "One query per spelling, not one carrying many.");
        });
    }

    /// <summary>
    /// Thirty translations would be thirty searches per source per sweep, so translated spellings ride as
    /// aliases only and are filtered by the acquisition's accepted languages. Both guards survive the
    /// conversion even though the spelling the row derives to does not name the component it wants.
    /// </summary>
    [Test]
    public void NeverGivesATranslatedSpellingAQueryOfItsOwn()
    {
        var aliases = MoviesDeclaration.Carried.Querying.Aliases;
        var translated = aliases.Single(static alias => alias.AliasId == "translated-titles");

        Assert.Multiple(() =>
        {
            Assert.That(
                aliases.Select(static alias => alias.Order),
                Is.Ordered.Ascending,
                "Most canonical first.");
            Assert.That(
                aliases.Select(static alias => alias.AliasId),
                Is.EqualTo(new[] { "display-title", "original-title", "alternative-titles", "translated-titles" }));
            Assert.That(translated.NeverOwnQuery, Is.True);
            Assert.That(translated.FilterByAcceptedLanguages, Is.True);
        });
    }

    /// <summary>
    /// <b>What an expression cannot carry into a template, pinned.</b> Every alias spelling used to end in
    /// a <c>:query</c> modifier naming the normalization a source's search box wants. A property expression
    /// names a property and nothing else, so the modifier is gone from every one of them. Nothing observes
    /// it yet, and it will matter the moment a real source is asked.
    /// </summary>
    [Test]
    public void LosesTheQuerySpellingModifierFromEveryAliasAndFreeTextTemplate()
    {
        var templates = MoviesDeclaration.Carried.Querying.Aliases
            .Select(static alias => alias.Template)
            .Concat(MoviesDeclaration.Carried.Querying.Tiers.Select(static tier => tier.FreeTextTemplate))
            .ToArray();

        Assert.That(templates, Has.None.Contains(":query"));
    }

    [Test]
    public void SpellsNoCoordinateBecauseAMovieAddressesItself()
        => Assert.That(MoviesDeclaration.Carried.Querying.Grammar.Spellings, Is.Empty);

    /// <summary>
    /// <b>Host search policy has left the media kind.</b> How many results an origin is worth is the same
    /// question for every kind, and answering it per kind made behavior differ between kinds for no reason
    /// anybody could name — three of the four reference kinds answered it not at all.
    /// </summary>
    [Test]
    public void StatesNoResultLimitsBecauseThoseAreNotMovieFacts()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Carried.Querying.Limits, Is.Empty);
            Assert.That(MoviesDeclaration.Carried.Querying.Substitutions, Is.Empty);
        });

    // ---- naming ---------------------------------------------------------------------------------

    [Test]
    public void DeclaresTheSurveyedDefaultTemplates()
    {
        var naming = MoviesDeclaration.Carried.Naming;

        Assert.Multiple(() =>
        {
            Assert.That(
                naming.DefaultTemplates["file"],
                Is.EqualTo("{Movie Title} ({Movie Year})"));

            // The identity stamp Plex and Jellyfin read. The surveyed application spells it with the
            // catalog's own name — a media kind writing a vendor into a folder — and the escaped braces
            // survive as literal output either way. {Movie Id} renders whichever catalog assigned the
            // primary identifier, so the same template is right for a library catalogued anywhere.
            Assert.That(
                naming.DefaultTemplates["folder"],
                Is.EqualTo("{Movie TitleThe} ({Movie Year}) <{{{Movie Id}}}>"));
            Assert.That(naming.DefaultTemplates["collection-folder"], Is.EqualTo("{Collection TitleThe}"));
        });
    }

    /// <summary>
    /// Every token in every item template is derived from an item-owned property.
    /// </summary>
    [Test]
    public void MentionsOnlyDerivedTokens()
    {
        var derived = MoviesDeclaration.Shape.Tokens
            .Select(static token => token.Name)
            .ToHashSet(StringComparer.Ordinal);

        var mentioned = MoviesDeclaration.Carried.Naming.DefaultTemplates.Values
            .SelectMany(static template => Regex.Matches(template, @"\{[A-Z][^{}]*\}", RegexOptions.None,
                TimeSpan.FromSeconds(1)))
            .Select(static match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            mentioned.Where(token => !derived.Contains(token)),
            Is.Empty);
    }

    /// <summary>
    /// One level deep, with the optional collection segment as the only nesting a movie library has. Both
    /// halves of the old two-atom condition are implied now: whether the user asked is a host-owned option
    /// about the axis, and whether the item is in a group follows from the axis being a single optional
    /// reference.
    /// </summary>
    [Test]
    public void DeclaresAFolderSpineWithOneOptionalSegment()
    {
        var naming = MoviesDeclaration.Carried.Naming;
        var rule = naming.Selection.Single();

        Assert.Multiple(() =>
        {
            Assert.That(naming.FolderSpine, Is.EqualTo("{root}/[collection-folder/]{folder}"));
            Assert.That(rule.RuleId, Is.EqualTo("group-by-collection"));
            Assert.That(rule.InsertSpineSegment, Is.EqualTo("collection-folder"));
            Assert.That(
                rule.When.All.Select(static atom => atom.Subject),
                Is.EqualTo(new[] { "options.groupBy.collection", "fields.collection" }),
                "The option is keyed by the axis rather than being a name the kind invented.");
            Assert.That(naming.MultiUnitStyles, Is.Empty, "One file per unit; nothing to join.");
        });
    }

    /// <summary>
    /// <b>The disjunction the flat surface could not hold.</b> A file name states a title and a year, or
    /// else names the original file, and the two branches exclude each other. As a per-token flag it was
    /// inexpressible in either half; as a predicate over the parsed template it is one line, and it is
    /// checkable here against templates rather than only against a validator's opinion of one.
    /// </summary>
    [Test]
    public void CarriesTheFileTemplateRuleAsAPredicateOverTheTemplate()
    {
        var rule = MoviesDeclaration.Carried.TemplateRules.Single();

        Assert.Multiple(() =>
        {
            Assert.That(rule.RuleId, Is.EqualTo("names-the-movie-or-the-original-file"));
            Assert.That(rule.Requirement, Is.Not.Empty);

            Assert.That(rule.IsSatisfied(Mentions("title", "year")), Is.True, "A title and a year.");
            Assert.That(rule.IsSatisfied(Mentions("originalTitle", "year")), Is.True, "Or the other title.");
            Assert.That(rule.IsSatisfied(Mentions(FileFact.OriginalFileName)), Is.True, "Or the file's name.");
            Assert.That(rule.IsSatisfied(Mentions(FileFact.SceneName)), Is.True, "Or the release name.");

            Assert.That(rule.IsSatisfied(Mentions("title")), Is.False, "A title alone names two remakes.");
            Assert.That(rule.IsSatisfied(Mentions("year")), Is.False);
            Assert.That(rule.IsSatisfied(new TemplateFacts([], [])), Is.False);
            Assert.That(
                rule.IsSatisfied(Mentions(["title", "year"], [FileFact.OriginalFileName])),
                Is.False,
                "The branches exclude each other, which a conjunction of flags could not have said.");
        });
    }

    [Test]
    public void FallsBackRatherThanRenderingAnEmptyName()
    {
        var fallbacks = MoviesDeclaration.Carried.Naming.Fallbacks;

        Assert.Multiple(() =>
        {
            Assert.That(
                fallbacks.Single(static rule => rule.Token.Length != 0).Order,
                Is.EqualTo(new[] { "file.sceneName", "file.originalFileName" }));
            Assert.That(
                fallbacks.Single(static rule => rule.Token.Length == 0).Order,
                Is.EqualTo(new[] { "file.originalFileName" }),
                "Losing the file to a bad template would be worse than an ugly name.");
        });
    }

    /// <summary>
    /// <b>What the fallback builder cannot say.</b> The rule Radarr actually has is about the host's own
    /// <c>{Original Title}</c> token — the scene name a file arrived under — and the builder takes a
    /// property expression, so the nearest expressible thing is the movie's original title. The fallback
    /// chain is right and the token it hangs on is not, which is one more thing the host token registry
    /// resolves.
    /// </summary>
    [Test]
    public void HangsTheSceneNameFallbackOnAPropertyBecauseItCannotNameAHostToken()
        => Assert.That(
            MoviesDeclaration.Carried.Naming.Fallbacks.Single(static rule => rule.Token.Length != 0).Token,
            Is.EqualTo("originalTitle"));

    // ---- summaries ------------------------------------------------------------------------------

    /// <summary>
    /// A notification naming the wrong remake is worse than no notification, so the headline carries the
    /// year: title-and-year is the only form that disambiguates a movie. It is an interpolation over two
    /// properties now rather than a template naming two field identifiers.
    /// </summary>
    [Test]
    public void NamesAMovieUnambiguouslyInASummary()
    {
        var notifications = MoviesDeclaration.Carried.Notifications;

        Assert.Multiple(() =>
        {
            Assert.That(notifications.HeadlineTemplate, Is.EqualTo("{title} ({year})"));
            Assert.That(notifications.BodyFieldId, Is.EqualTo("overview"));
        });
    }

    /// <summary>
    /// <b>Four kinds of row left the summary, and each for its own reason.</b> The deep link is the host's
    /// own routing scheme and cannot be a media kind's business; the catalog addresses belong to whoever
    /// owns the identifier; the occasion phrases were twelve rows of English per kind of which not one was
    /// movie-specific; and the artwork role order is a platform-wide vocabulary.
    /// </summary>
    [Test]
    public void CarriesNoDeepLinkNoVendorAddressAndNoEnglishPhraseTable()
    {
        // Stronger than it was. These four used to be asserted empty on Movies' own section; they are now
        // absent from the contract, so no kind can carry them and the assertion is about the platform.
        var members = typeof(NotificationDeclaration)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.That(
            members,
            Has.No.Member("DeepLinkTemplate")
                .And.No.Member("LinkTemplates")
                .And.No.Member("Occasions")
                .And.No.Member("ArtworkRoleOrder"),
            "a summary names the item; the host links it, addresses it and phrases it");
    }

    /// <summary>
    /// The rows that read host state — quality, total size, languages — are gone too: the host holds them
    /// for every kind and supplies them for every kind. What is left is the five that read the movie.
    /// </summary>
    [Test]
    public void KeepsOnlyTheSummaryRowsThatReadTheMovie()
        => Assert.That(
            MoviesDeclaration.Carried.Notifications.Fields.Select(static row => (row.Label, row.Template)),
            Is.EqualTo(new[]
            {
                ("Studio", "{organization}"),
                ("Genres", "{genres}"),
                ("Rated", "{certification}"),
                ("Runtime", "{runtime}"),
                ("Rating", "{ratings}"),
            }));

    [Test]
    public void SummarizesACollectionWithoutClaimingADeepLinkItDoesNotHave()
    {
        var group = MoviesDeclaration.Carried.Notifications.GroupSummaries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(group.AxisId, Is.EqualTo("collection"));
            Assert.That(group.HeadlineTemplate, Is.EqualTo("{title}"));
            Assert.That(group.Fields.Single().Template, Is.EqualTo("{memberCount}"));
        });
    }

    // ---- item projection ------------------------------------------------------------------------

    /// <summary>
    /// Filtering, sorting and text search derive from the semantics each field declares, and each of those
    /// now derives from an attribute on the property rather than from a hand-written row.
    /// </summary>
    [Test]
    public void DeclaresTheSemanticsTheHostStoreDerivesQueryBehaviorFrom()
    {
        var fields = MoviesDeclaration.Fields;

        Assert.Multiple(() =>
        {
            Assert.That(fields["title"].Semantics.HasFlag(FieldSemantics.Searchable), Is.True);
            Assert.That(
                fields["title"].Semantics.HasFlag(FieldSemantics.Sortable),
                Is.True,
                "A title is always sortable, implied rather than written, because a kind that had to "
                + "remember to say so would eventually forget.");
            Assert.That(
                fields["alternateTitles"].Semantics.HasFlag(FieldSemantics.Searchable),
                Is.True,
                "A film is findable by a spelling it is also known by.");
            Assert.That(fields["year"].Semantics.HasFlag(FieldSemantics.Filterable), Is.True);
            Assert.That(fields["genres"].Semantics.HasFlag(FieldSemantics.Groupable), Is.True);
        });
    }

    /// <summary>
    /// Every browse axis, ordering and filter names a field the structure declares — which is now true by
    /// construction, because both sides are derived from the same properties.
    /// </summary>
    [Test]
    public void OffersNoSortOrFilterOverAFieldTheShapeDoesNotDeclare()
        => Assert.Multiple(() =>
        {
            foreach (var sort in MoviesDeclaration.Intent.Sorts)
            {
                Assert.That(MoviesDeclaration.Fields, Does.ContainKey(sort.FieldId), sort.FieldId);
            }

            foreach (var filter in MoviesDeclaration.Intent.Filters)
            {
                Assert.That(MoviesDeclaration.Fields, Does.ContainKey(filter.FieldId), filter.FieldId);
            }

            foreach (var axis in MoviesDeclaration.Intent.BrowseAxes.Where(static a => a.FieldId is not null))
            {
                Assert.That(MoviesDeclaration.Fields, Does.ContainKey(axis.FieldId!), axis.AxisId);
            }
        });

    private static INamingTemplateFacts Mentions(params string[] fieldIds) => new TemplateFacts(fieldIds, []);

    private static INamingTemplateFacts Mentions(params FileFact[] facts) => new TemplateFacts([], facts);

    private static INamingTemplateFacts Mentions(string[] fieldIds, FileFact[] facts) =>
        new TemplateFacts(fieldIds, facts);

    private sealed class TemplateFacts(IReadOnlyList<string> fieldIds, IReadOnlyList<FileFact> facts)
        : INamingTemplateFacts
    {
        public bool HasField(string fieldId) => fieldIds.Contains(fieldId, StringComparer.Ordinal);

        public bool Has(FileFact fact) => facts.Contains(fact);
    }
}
