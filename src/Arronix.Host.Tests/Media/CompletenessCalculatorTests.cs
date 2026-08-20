using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Storage;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Media;

/// <summary>
/// Counting what is present against what is wanted, entirely from declarations.
/// </summary>
/// <remarks>
/// There is no completeness contract for an extension to implement because there is nothing left for one to
/// decide, and these tests are the argument: the variant-relative count and the excluded reserved value both
/// fall out of declarations the shape already makes.
/// </remarks>
[TestFixture]
internal sealed class CompletenessCalculatorTests
{
    private static (CompletenessCalculator Calculator, IMediaStore Store, ValidatedShape Shape) Build()
    {
        var kinds = TestOptions.RegistryWith(ContributionFixtures.For(ShapeFixtures.Layered()));
        var store = new InMemoryMediaStore(kinds);

        return (new CompletenessCalculator(store), store, kinds.All[0].Shape);
    }

    private static CompletenessCandidate Candidate(int id, long group, long index, bool wanted, int? variant = null)
        => new(
            ShapeFixtures.Item(ShapeFixtures.Part, id),
            ShapeFixtures.At(group, index),
            wanted,
            variant is { } chosen ? ShapeFixtures.Item(ShapeFixtures.Variant, chosen) : null);

    [Test]
    public async Task NothingPresentMeansEverythingWantedIsMissing()
    {
        var (calculator, _, shape) = Build();

        var progress = await calculator.ComputeAsync(
            shape,
            [Candidate(1, 1, 1, true), Candidate(2, 1, 2, true)],
            null);

        progress.Total.Should().Be(2);
        progress.Want.Should().Be(2);
        progress.Have.Should().Be(0);
    }

    [Test]
    public async Task AUnitWithAFileCounts()
    {
        var (calculator, store, shape) = Build();
        var unit = ShapeFixtures.Item(ShapeFixtures.Part, 1);

        var file = await store.UpsertFileAsync(new MediaFileRecord
        {
            Id = MediaFileId.FromInt64(0),
            Anchor = ShapeFixtures.Item(ShapeFixtures.Work, 1),
            Path = "/library/one.mkv",
            Size = 1024,
        });

        await store.LinkAsync(new UnitFileLink(unit, file, null));

        var progress = await calculator.ComputeAsync(shape, [Candidate(1, 1, 1, true)], null);

        progress.Have.Should().Be(1);
        progress.SizeOnDisk.Should().Be(1024);
    }

    [Test]
    public async Task AUnitNobodyWantsIsCountedInTheTotalAndNotInTheWant()
    {
        var (calculator, _, shape) = Build();

        var progress = await calculator.ComputeAsync(
            shape,
            [Candidate(1, 1, 1, true), Candidate(2, 1, 2, false)],
            null);

        progress.Total.Should().Be(2);
        progress.Want.Should().Be(1);
    }

    [Test]
    public async Task AReservedSequenceValueIsExcludedAltogether()
    {
        var (calculator, _, shape) = Build();

        // The fixture declares the zero group as an exception excluded from completeness, which is the
        // declared form of every hard-coded "greater than zero" test in a surveyed application's statistics.
        var progress = await calculator.ComputeAsync(
            shape,
            [Candidate(1, 0, 1, true), Candidate(2, 1, 1, true)],
            null);

        progress.Total.Should().Be(1);
        progress.Want.Should().Be(1);
    }

    [Test]
    public async Task CompletenessIsCountedAgainstTheChosenManifestationOnly()
    {
        var (calculator, _, shape) = Build();
        var chosen = ShapeFixtures.Item(ShapeFixtures.Variant, 10);

        var progress = await calculator.ComputeAsync(
            shape,
            [
                Candidate(1, 1, 1, true, variant: 10),
                Candidate(2, 1, 2, true, variant: 10),
                Candidate(3, 1, 1, true, variant: 20),
                Candidate(4, 1, 2, true, variant: 20),
                Candidate(5, 1, 3, true, variant: 20),
            ],
            chosen);

        progress.Total.Should().Be(2);
    }

    [Test]
    public async Task WithoutAChosenManifestationEveryCandidateCounts()
    {
        var (calculator, _, shape) = Build();

        var progress = await calculator.ComputeAsync(
            shape,
            [Candidate(1, 1, 1, true, variant: 10), Candidate(2, 1, 1, true, variant: 20)],
            null);

        progress.Total.Should().Be(2);
    }

    [Test]
    public async Task AFileSatisfyingSeveralUnitsIsCountedOnceOnDisk()
    {
        var (calculator, store, shape) = Build();

        var file = await store.UpsertFileAsync(new MediaFileRecord
        {
            Id = MediaFileId.FromInt64(0),
            Anchor = ShapeFixtures.Item(ShapeFixtures.Work, 1),
            Path = "/library/double.mkv",
            Size = 2048,
        });

        await store.LinkAsync(new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Part, 1), file, null));
        await store.LinkAsync(new UnitFileLink(ShapeFixtures.Item(ShapeFixtures.Part, 2), file, null));

        var progress = await calculator.ComputeAsync(
            shape,
            [Candidate(1, 1, 1, true), Candidate(2, 1, 2, true)],
            null);

        progress.Have.Should().Be(2);

        // Counting the bytes once per unit would report a library several times its real size.
        progress.SizeOnDisk.Should().Be(2048);
    }

    [Test]
    public async Task NoCandidatesMeansNothingToReport()
    {
        var (calculator, _, shape) = Build();

        var progress = await calculator.ComputeAsync(shape, [], null);

        progress.Total.Should().Be(0);
        progress.Have.Should().Be(0);
        progress.Want.Should().Be(0);
        progress.SizeOnDisk.Should().Be(0);
    }
}
