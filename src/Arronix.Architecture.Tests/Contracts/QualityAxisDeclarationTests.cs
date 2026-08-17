using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Quality;

// The quality-axes area is experimental; asserting its declaration rules is what this fixture is for.
#pragma warning disable ARX0021

namespace Arronix.Architecture.Tests.Contracts;

/// <summary>
/// <c>ARXQ001</c>-<c>ARXQ004</c> - the axis-declaration rules, swept over the contract assembly.
/// </summary>
/// <remarks>
/// <para>
/// <c>quality-axes.md</c> QA-15 schedules these as Roslyn analyzer rules. An analyzer needs an
/// <c>Arronix.Analyzers</c> project, and adding one means editing <c>Arronix.sln</c>, so the rules are
/// asserted here instead. The trade is stated rather than glossed: an analyzer would fail the build at the
/// point of the mistake and would also cover a family compiled outside this repository, whereas this
/// fixture fails afterwards and only covers families that ship here. What it buys in exchange is that it
/// exists.
/// </para>
/// <para>
/// The host's <c>QualityAxisReader</c> already restates these four rules, but it restates them
/// <i>at load time</i>, which means a family nobody registers is never checked at all. This sweep is over
/// the declarations themselves, so a facts type that is declared and not yet wired is still governed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class QualityAxisDeclarationTests
{
    /// <summary>Every quality-facts type the contract assembly declares.</summary>
    private static IReadOnlyList<Type> FactsTypes { get; } =
    [
        .. typeof(QualityAxisId).Assembly
            .GetTypes()
            .Where(static type => typeof(IQualityFacts).IsAssignableFrom(type))
            .Where(static type => type is { IsInterface: false, IsAbstract: false })
            .OrderBy(static type => type.FullName, StringComparer.Ordinal),
    ];

    private static IEnumerable<TestCaseData> Families =>
        FactsTypes.Select(static type => new TestCaseData(type).SetArgDisplayNames(type.Name));

    [Test]
    public void TheContractAssemblyShipsAtLeastOneQualityFamilyToGovern()
    {
        // Without this the four rules below pass vacuously, which is the failure mode a sweep invites.
        Assert.That(FactsTypes, Is.Not.Empty);
    }

    /// <summary><c>ARXQ001</c> - an axis is evidence of an enum, an int, a double, or a set of enums.</summary>
    [TestCaseSource(nameof(Families))]
    public void AnAxisCarriesOnlyTheFourEvidenceShapesTheDerivationTableNames(Type facts)
    {
        foreach (var property in Axes(facts))
        {
            Assert.That(
                Form(property.PropertyType),
                Is.Not.Null,
                $"{facts.Name}.{property.Name} carries [Axis] on {property.PropertyType.Name}, which has no "
                + "form in the derivation table. ARXQ001.");
        }
    }

    /// <summary><c>ARXQ002</c> - a scalar axis is not declared unordered.</summary>
    [TestCaseSource(nameof(Families))]
    public void AScalarAxisIsNeverDeclaredUnordered(Type facts)
    {
        foreach (var property in Axes(facts))
        {
            if (Form(property.PropertyType) != AxisForm.Scalar)
            {
                continue;
            }

            Assert.That(
                Declaration(property).Ordering,
                Is.Not.EqualTo(AxisOrdering.Unordered),
                $"{facts.Name}.{property.Name} is a quantity, and a quantity has an order whether or not "
                + "anyone wants one. ARXQ002.");
        }
    }

    /// <summary><c>ARXQ003</c> - a set-valued axis is not declared ordered.</summary>
    [TestCaseSource(nameof(Families))]
    public void ASetValuedAxisIsNeverDeclaredOrdered(Type facts)
    {
        foreach (var property in Axes(facts))
        {
            if (!IsSet(property.PropertyType))
            {
                continue;
            }

            Assert.That(
                Declaration(property).Ordering,
                Is.EqualTo(AxisOrdering.Unordered),
                $"{facts.Name}.{property.Name} holds several values at once, so there is no single value to "
                + "rank. ARXQ003.");
        }
    }

    /// <summary><c>ARXQ004</c> - at least one axis, and no two axes deriving one identity.</summary>
    [TestCaseSource(nameof(Families))]
    public void AFamilyDeclaresAtLeastOneAxisAndNoTwoAxesShareAnIdentity(Type facts)
    {
        var axes = Axes(facts).ToList();

        Assert.That(axes, Is.Not.Empty, $"{facts.Name} declares no axis at all. ARXQ004.");

        var ids = axes
            .Select(static property => QualityAxisId.FromProperty(property.Name))
            .ToList();

        Assert.That(
            ids.Distinct().Count(),
            Is.EqualTo(ids.Count),
            $"{facts.Name} derives one identity from two properties. ARXQ004.");
    }

    private static IEnumerable<PropertyInfo> Axes(Type facts) =>
        facts.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetCustomAttribute<AxisAttribute>() is not null);

    private static AxisAttribute Declaration(PropertyInfo property) =>
        property.GetCustomAttribute<AxisAttribute>()!;

    private static bool IsSet(Type declared) =>
        declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(EvidenceSet<>);

    /// <summary>
    /// The derivation table of <c>quality-axes.md</c> §1.3, restated. Null means "no form", which is the
    /// <c>ARXQ001</c> failure.
    /// </summary>
    private static AxisForm? Form(Type declared)
    {
        if (!declared.IsGenericType)
        {
            return null;
        }

        var definition = declared.GetGenericTypeDefinition();
        var argument = declared.GetGenericArguments()[0];

        if (definition == typeof(EvidenceSet<>))
        {
            return argument.IsEnum ? AxisForm.Nominal : null;
        }

        if (definition != typeof(Evidence<>))
        {
            return null;
        }

        if (argument.IsEnum)
        {
            return AxisForm.Ordinal;
        }

        return argument == typeof(int) || argument == typeof(double) ? AxisForm.Scalar : null;
    }
}
