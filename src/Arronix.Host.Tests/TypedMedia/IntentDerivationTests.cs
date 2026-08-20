using System.Linq;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed;
using FluentAssertions;


namespace Arronix.Host.Tests.TypedMedia;

/// <summary>
/// The intent surface, derived from the entity rather than written beside it.
/// </summary>
/// <remarks>
/// The measure that matters here is how little the kind wrote. Every traversal, ordering, filter and state
/// asserted below came from an attribute and a CLR type; the fixture writes four lines of intent, and they
/// are exactly the four facts derivation cannot know.
/// </remarks>
[TestFixture]
internal sealed class IntentDerivationTests
{
    private static PluginIntentSurface Surface =>
        MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>().Intent;

    [Test]
    public void TheFlatTraversalIsTheDefaultAndTakesItsNameFromTheBuilder()
    {
        var flat = Surface.BrowseAxes.Should().ContainSingle(axis => axis.IsDefault).Subject;

        Assert.Multiple(() =>
        {
            flat.AxisId.Should().Be("all");
            flat.Name.Should().Be("All works");
            flat.Kind.Should().Be(BrowseAxisKind.Flat);
        });
    }

    [Test]
    public void EachGroupablePropertyBecomesAFacetTraversalUnlessItWasHidden()
    {
        var facets = Surface.BrowseAxes
            .Where(axis => axis.Kind == BrowseAxisKind.Facet)
            .Select(axis => axis.FieldId)
            .ToArray();

        Assert.Multiple(() =>
        {
            facets.Should().Contain(["originalLanguage", "genres", "collections"]);

            // Filterable but not worth a traversal: one of the three exceptions the kind writes.
            facets.Should().NotContain("keywords");
        });
    }

    [Test]
    public void EachTimestampBecomesASequenceTraversal() =>
        Surface.BrowseAxes
            .Where(axis => axis.Kind == BrowseAxisKind.Sequence)
            .Select(axis => axis.FieldId)
            .Should().Contain(["previewedOn", "publishedOn", "releaseDate"]);

    [Test]
    public void EachDeclaredGroupBecomesAGroupingTraversal()
    {
        var grouping = Surface.BrowseAxes
            .Should().ContainSingle(axis => axis.Kind == BrowseAxisKind.Grouping).Subject;

        Assert.Multiple(() =>
        {
            grouping.GroupingAxisId.Should().Be("collection");
            grouping.Name.Should().Be("Collections");
        });
    }

    [Test]
    public void OrderingsAreDerivedAndTheirDirectionFollowsTheType()
    {
        var sorts = Surface.Sorts.ToDictionary(sort => sort.FieldId, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            sorts.Keys.Should().BeEquivalentTo("title", "runtime", "shippedBytes", "scores");

            // Text runs from the beginning; the fixture restates it, and the restatement agrees.
            sorts["title"].DefaultDirection.Should().Be(SortDirection.Ascending);

            // A number's useful end is the large one, and nothing had to say so.
            sorts["shippedBytes"].DefaultDirection.Should().Be(SortDirection.Descending);
            sorts["runtime"].DefaultDirection.Should().Be(SortDirection.Descending);
        });
    }

    [TestCase("runtime", FilterOperators.GreaterThan | FilterOperators.LessThan | FilterOperators.Between
        | FilterOperators.IsNull)]
    [TestCase("originalLanguage", FilterOperators.Equals | FilterOperators.NotEquals | FilterOperators.In
        | FilterOperators.IsNull)]
    [TestCase("genres", FilterOperators.In | FilterOperators.Contains | FilterOperators.IsNull)]
    public void FilterOperatorsAreDerivedFromThePropertyType(string fieldId, FilterOperators expected) =>
        Surface.Filters
            .Single(filter => string.Equals(filter.FieldId, fieldId, StringComparison.Ordinal))
            .Operators.Should().Be(expected);

    [Test]
    public void OneStateIsDerivedPerMemberOfTheStatusEnumeration()
    {
        var states = Surface.States;

        Assert.Multiple(() =>
        {
            states.Select(state => state.StateId)
                .Should().Equal("withdrawn", "rumored", "announced", "previewing", "published");

            states.Should().OnlyContain(state =>
                string.Equals(state.SourceFieldId, "stage", StringComparison.Ordinal));

            // Tone is the one part a consumer cannot derive, so it is the one part written.
            Tone("published").Should().Be(StateTone.Positive);
            Tone("previewing").Should().Be(StateTone.Attention);
            Tone("withdrawn").Should().Be(StateTone.Problem);
            Tone("rumored").Should().Be(StateTone.Neutral);
        });

        StateTone Tone(string stateId) =>
            states.Single(state => string.Equals(state.StateId, stateId, StringComparison.Ordinal)).Tone;
    }

    [Test]
    public void NoExternalSurfaceIsProducedAtAll() =>
        // Every surveyed one was a catalog's own address grammar spelled inside a media kind. A surface at
        // a catalog belongs to whoever owns the identifier.
        Surface.ExternalSurfaces.Should().BeEmpty();

    [Test]
    public void AnActionOverAGroupingAxisCanNameTheAxis()
    {
        var action = Surface.Actions
            .Single(candidate =>
                string.Equals(candidate.ActionId, "collection.refresh", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            action.Scope.Should().Be(ActionScope.Group);
            action.TargetGroupAxisId.Should().Be("collection");
            action.TargetLevelId.Should().BeNull();
        });
    }

    [Test]
    public void AMonitorableGroupReceivesTheStandardGroupOperation()
    {
        var action = Surface.Actions
            .Single(candidate =>
                string.Equals(candidate.ActionId, "collection.monitor", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            action.StandardAction.Should().Be(StandardMediaAction.SetGroupMonitoring);
            action.Scope.Should().Be(ActionScope.Group);
            action.TargetGroupAxisId.Should().Be("collection");
            action.EnabledWhenFieldId.Should().BeNull();
            action.Confirmation.Should().Be(ConfirmationRequirement.Acknowledge);
            action.ConsequenceStatement.Should().NotBeNullOrWhiteSpace();
            action.Parameters.Select(parameter => parameter.StandardParameter).Should().Equal(
                StandardMediaActionParameter.Wanted,
                StandardMediaActionParameter.AddMissing);
        });
    }

    [Test]
    public void ThePlatformDerivesTheWholeStandardActionCatalog()
    {
        Surface.Actions.Select(action => action.StandardAction).Should().Contain(
        [
            StandardMediaAction.Search,
            StandardMediaAction.SearchMissing,
            StandardMediaAction.SearchCutoffUnmet,
            StandardMediaAction.Refresh,
            StandardMediaAction.Rescan,
            StandardMediaAction.SetMonitoring,
            StandardMediaAction.SetAvailability,
            StandardMediaAction.Rename,
            StandardMediaAction.Add,
            StandardMediaAction.Remove,
            StandardMediaAction.Exclude,
            StandardMediaAction.ClearExclusion,
            StandardMediaAction.SetGroupMonitoring,
            StandardMediaAction.RefreshGroups
        ]);
    }

    [Test]
    public void AnActionParameterThatNamesASelectionPolicyCarriesItsOrderedChoices()
    {
        var parameter = Surface.Actions
            .Single(candidate => string.Equals(candidate.ActionId, "add", StringComparison.Ordinal))
            .Parameters
            .Single(candidate => string.Equals(candidate.ParameterId, "stage", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            parameter.ValueKind.Should().Be(FieldValueKind.Enumerated);
            parameter.Choices.Select(choice => choice.Value)
                .Should().Equal("rumored", "announced", "previewing", "published");
            parameter.DefaultValue.Should().Be("published");
        });
    }

    [Test]
    public void AWorkbenchTakesItsColumnsFromItsRowType()
    {
        var workbench = Surface.Workbenches.Should().ContainSingle().Subject;

        Assert.Multiple(() =>
        {
            workbench.Subject.Should().Be(WorkbenchSubject.LooseFiles);
            workbench.Columns.Select(column => column.Field.FieldId).Should().Equal("path", "work", "size");
            workbench.Columns.Single(column =>
                string.Equals(column.Field.FieldId, "work", StringComparison.Ordinal))
                .Editable.Should().BeTrue();
            workbench.Columns.Single(column =>
                string.Equals(column.Field.FieldId, "size", StringComparison.Ordinal))
                .Field.ValueKind.Should().Be(FieldValueKind.ByteSize);
            workbench.CommitConsequence.Should().Be(Consequence.Destructive);
            workbench.Inputs.Should().ContainSingle().Which.ParameterId.Should().Be("files");
        });
    }
}
