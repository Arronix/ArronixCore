using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Media.Typed;
using Arronix.Host.Tests.Support;
using Arronix.Plugin.Movies;
using FluentAssertions;

// The shape contracts are experimental; the reference declarations are written against them.
#pragma warning disable ARX0013
#pragma warning disable ARX0020

namespace Arronix.Host.Tests.Media;

/// <summary>
/// The four reference media kinds, put through the gate.
/// </summary>
/// <remarks>
/// <para>
/// A fixture written beside a validator agrees with it by construction. These four declarations were written
/// by someone else, from the survey evidence, and each of them exercises a structure the others do not — a
/// fused single level, a variant axis, an anchor above its unit, an ordinal sequence with a reserved value.
/// Putting them through the same gate is the only evidence available that the sixteen rules describe the
/// real problem rather than the fixtures.
/// </para>
/// <para>
/// The assertions are structural and never name a media concept: what is checked is that the roles resolve,
/// that the gate accepts the declaration, and that the acceptance table's shape holds.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class ReferenceShapeTests
{
    private static IEnumerable<TestCaseData> ReferenceShapes()
    {
        // Movies is the one reference kind whose structure is no longer written: it is derived from the
        // item type and its attributes, and it goes through the same gate as the three that are written.
        yield return new TestCaseData(MediaTypeModelFactory.Build<Movie, Movies>().Shape).SetName("Movies");
        yield return new TestCaseData(new Arronix.Plugin.Books.BooksShape().Shape).SetName("Books");
        yield return new TestCaseData(new Arronix.Plugin.Music.MusicShape().Shape).SetName("Music");
        yield return new TestCaseData(new Arronix.Plugin.Tv.TvShape().Shape).SetName("Tv");
    }

    [TestCaseSource(nameof(ReferenceShapes))]
    public void EveryReferenceShapeValidatesCleanly(MediaShape declaration)
    {
        ValidatedShape.TryValidate(declaration, out var shape, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => $"{defect.Path}: {defect.Message}")));

        shape.Should().NotBeNull();
    }

    [TestCaseSource(nameof(ReferenceShapes))]
    public void EveryReferenceShapeResolvesEveryRoleTheHostDependsOn(MediaShape declaration)
    {
        ValidatedShape.TryValidate(declaration, out var shape, out _).Should().BeTrue();

        shape!.LibraryEntry.Should().NotBeNull();
        shape.AcquisitionUnit.Should().NotBeNull();
        shape.CompletenessUnit.Should().NotBeNull();
        shape.FileAnchor.Should().NotBeNull();
        shape.FileUnit.Should().NotBeNull();
        shape.Levels.Should().NotBeEmpty();
    }

    [TestCaseSource(nameof(ReferenceShapes))]
    public void EveryReferenceShapeIsAdmittedByTheRegistry(MediaShape declaration)
    {
        var registry = TestOptions.RegistryWith();

        registry.TryRegister(ContributionFixtures.For(declaration, new FakeItemSource(declaration.Kind)), out var registered, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => $"{defect.Path}: {defect.Message}")));

        registered!.Descriptor.Levels.Should().HaveCount(declaration.Levels.Count);
        registered.Projection.Id.Should().Be(declaration.Kind);
    }

    [TestCaseSource(nameof(ReferenceShapes))]
    public void EveryReferenceShapeGivesEveryFileExtensionExactlyOneFamily(MediaShape declaration)
    {
        ValidatedShape.TryValidate(declaration, out var shape, out _).Should().BeTrue();

        foreach (var family in declaration.FormatFamilies)
        {
            foreach (var extension in family.FileExtensions)
            {
                shape!.FamilyForExtension(extension)!.FamilyId.Should().Be(family.FamilyId);
            }
        }
    }

    [TestCaseSource(nameof(ReferenceShapes))]
    public void EveryReferenceShapeDeclaresASearchWhoseTargetResolves(MediaShape declaration)
    {
        ValidatedShape.TryValidate(declaration, out var shape, out _).Should().BeTrue();

        foreach (var search in declaration.SearchKinds)
        {
            shape!.RequireSearchKind(search.SearchKindId).Should().NotBeNull();
            shape.HasLevel(search.TargetLevelId).Should().BeTrue();
        }
    }

    [Test]
    public void TheFourReferenceKindsAreStructurallyDifferentFromEachOther()
    {
        var shapes = ReferenceShapes()
            .Select(data => (MediaShape)data.Arguments[0]!)
            .ToList();

        // If they all had the same level count the acceptance test would be proving one shape four times.
        shapes.Select(shape => shape.Levels.Count).Distinct().Should().HaveCountGreaterThan(1);
        shapes.Select(shape => shape.Kind).Distinct().Should().HaveCount(4);
    }
}
