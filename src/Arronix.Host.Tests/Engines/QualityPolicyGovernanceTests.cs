using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Quality;
using FluentAssertions;

// The quality-axes area is experimental; governing the shipped families is what this fixture is for.
#pragma warning disable ARX0021

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// <c>ARXQ005</c> and <c>ARXQ006</c> - swept over every family the contract assembly actually ships.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QualityPolicy.For"/> already refuses both mistakes, and
/// <c>Arronix.Abstractions.Tests</c> pins each refusal against a fixture. Neither of those checks the
/// thing that ships: <c>For</c> only runs when somebody calls it, so a family whose default policy is
/// never constructed in a test is governed by nothing at all. This sweep constructs every shipped
/// family's real default policy and asserts the two rules over it, which means a family added later is
/// covered the moment it exists rather than when somebody remembers to write a test for it.
/// </para>
/// <para>
/// <c>quality-axes.md</c> QA-15 schedules these as analyzer rules. An analyzer needs an
/// <c>Arronix.Analyzers</c> project and therefore a solution edit, so they are asserted here. The
/// difference that matters: an analyzer would also govern a family compiled in somebody else's plugin,
/// and this fixture cannot. That gap is real and is recorded rather than papered over.
/// </para>
/// </remarks>
[TestFixture]
public sealed class QualityPolicyGovernanceTests
{
    /// <summary>Every <c>IQualityType&lt;TFacts&gt;</c> the contract assembly declares, with its facts type.</summary>
    private static IReadOnlyList<(Type Declaring, Type Facts)> ShippedFamilies { get; } =
    [
        .. typeof(QualityAxisId).Assembly
            .GetTypes()
            .Where(static type => type is { IsInterface: false, IsAbstract: false })
            .SelectMany(
                static type => type.GetInterfaces()
                    .Where(static face =>
                        face.IsGenericType
                        && face.GetGenericTypeDefinition() == typeof(IQualityType<>))
                    .Select(face => (Declaring: type, Facts: face.GetGenericArguments()[0])))
            .OrderBy(static pair => pair.Declaring.FullName, StringComparer.Ordinal),
    ];

    private static IEnumerable<TestCaseData> Families =>
        ShippedFamilies.Select(static pair =>
            new TestCaseData(pair.Declaring, pair.Facts).SetArgDisplayNames(pair.Declaring.Name));

    [Test]
    public void TheSweepHasAtLeastOneShippedFamilyToGovern()
    {
        // A reflection sweep that finds nothing passes every rule below. This is the guard against that.
        ShippedFamilies.Should().NotBeEmpty();
    }

    /// <summary><c>ARXQ005</c> - an axis with no order does not appear in the ordering.</summary>
    [TestCaseSource(nameof(Families))]
    public void NoShippedPolicyOrdersAnAxisThatHasNoOrder(Type declaring, Type facts)
    {
        var model = Build(declaring, facts);
        var nominal = model.Axes
            .Where(static axis => axis.Form == AxisForm.Nominal)
            .Select(static axis => axis.Id)
            .ToHashSet();

        foreach (var preference in model.DefaultPolicy.Precedence)
        {
            nominal.Should().NotContain(
                preference.Axis,
                "'{0}' has no order, so it cannot order anything - it belongs in the facet tier (ARXQ005)",
                preference.Axis);
        }
    }

    /// <summary><c>ARXQ006</c> - an axis orders or scores, never both.</summary>
    [TestCaseSource(nameof(Families))]
    public void NoShippedPolicyGivesOneAxisBothAPlaceInTheOrderingAndAFacetScore(Type declaring, Type facts)
    {
        var policy = Build(declaring, facts).DefaultPolicy;
        var ordering = policy.Precedence.Select(static preference => preference.Axis).ToHashSet();

        foreach (var score in policy.Facets.Scores)
        {
            ordering.Should().NotContain(
                score.Axis,
                "'{0}' both orders and scores; the disjointness is what the cycle-safety argument rests on "
                + "(ARXQ006)",
                score.Axis);
        }
    }

    /// <summary>
    /// The facet tier stays bounded. <c>ARXQ006</c>'s disjointness only keeps the core acyclic while the
    /// tier beneath it cannot grow into a second, unbounded ordering.
    /// </summary>
    [TestCaseSource(nameof(Families))]
    public void NoShippedPolicyExceedsTheFacetBound(Type declaring, Type facts)
    {
        var policy = Build(declaring, facts).DefaultPolicy;

        policy.Facets.Scores.Should().HaveCountLessThanOrEqualTo(FacetScoring.MaximumFacets);
    }

    private static IQualityType Build(Type declaring, Type facts)
    {
        var create = typeof(QualityTypeFactory)
            .GetMethod(nameof(QualityTypeFactory.Create), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(facts, declaring);

        return (IQualityType)create.Invoke(null, null)!;
    }
}
