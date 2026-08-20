using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Providers;
using FluentAssertions;


namespace Arronix.Host.Tests.Providers;

/// <summary>
/// Eligibility as pure set intersection over two declarations that name none of each other's concepts.
/// </summary>
[TestFixture]
internal sealed class IndexerEligibilityTests
{
    private static SearchKind Kind(
        IReadOnlyList<SearchTerm> required,
        IReadOnlyList<CategoryId> categories) => new()
        {
            SearchKindId = "unit",
            Name = "Unit",
            TargetLevelId = MediaLevelId.FromString("unit"),
            Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
            RequiredTerms = required,
            Categories = categories,
        };

    private static SearchProfile Profile(
        IReadOnlyList<SearchTerm> terms,
        IReadOnlyList<CategoryId> categories) => new()
        {
            ProfileId = "profile",
            Terms = terms,
            Categories = categories,
        };

    [Test]
    public void AProfileCoveringEveryRequiredTermAndOneCategoryIsEligible()
        => IndexerDispatcher.IsEligible(
            Kind([SearchTerm.FreeText, SearchTerm.WorkTitle], [CategoryId.FromInt(5000)]),
            Profile([SearchTerm.FreeText, SearchTerm.WorkTitle, SearchTerm.Year], [CategoryId.FromInt(5000)]))
            .Should().BeTrue();

    [Test]
    public void AMissingRequiredTermMakesItIneligible()
        => IndexerDispatcher.IsEligible(
            Kind([SearchTerm.FreeText, SearchTerm.ExternalIdentifier], [CategoryId.FromInt(5000)]),
            Profile([SearchTerm.FreeText], [CategoryId.FromInt(5000)]))
            .Should().BeFalse();

    [Test]
    public void DisjointCategoriesMakeItIneligible()
        => IndexerDispatcher.IsEligible(
            Kind([SearchTerm.FreeText], [CategoryId.FromInt(5000)]),
            Profile([SearchTerm.FreeText], [CategoryId.FromInt(2000)]))
            .Should().BeFalse();

    [Test]
    public void AKindThatRequiresNoTermsIsServedByFreeTextAndCategoriesAlone()
        => IndexerDispatcher.IsEligible(
            Kind([], [CategoryId.FromInt(7000)]),
            Profile([SearchTerm.FreeText], [CategoryId.FromInt(7000)]))
            .Should().BeTrue();

    [Test]
    public void AProviderSpecificCategoryCannotEstablishEligibility()
    {
        // Identifiers in the reserved band mean different things at different sources, so they cannot decide
        // whether one source can answer another's question.
        var providerSpecific = CategoryId.FromInt(100_001);

        providerSpecific.IsProviderSpecific.Should().BeTrue();

        IndexerDispatcher.IsEligible(
            Kind([SearchTerm.FreeText], [providerSpecific]),
            Profile([SearchTerm.FreeText], [providerSpecific]))
            .Should().BeFalse();
    }

    [Test]
    public void AProfileWithNoCategoriesIsNeverEligible()
        => IndexerDispatcher.IsEligible(
            Kind([SearchTerm.FreeText], [CategoryId.FromInt(5000)]),
            Profile([SearchTerm.FreeText], []))
            .Should().BeFalse();

    [Test]
    public void OnlyOneCategoryNeedsToOverlap()
        => IndexerDispatcher.IsEligible(
            Kind([SearchTerm.FreeText], [CategoryId.FromInt(5000), CategoryId.FromInt(5040)]),
            Profile([SearchTerm.FreeText], [CategoryId.FromInt(2000), CategoryId.FromInt(5040)]))
            .Should().BeTrue();
}
