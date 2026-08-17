// Exercises the experimental shape contracts.
#pragma warning disable ARX0013

using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Shape;

[TestFixture]
public class QualityRevisionTests
{
    [Test]
    public void OrderingIsVersionThenMislabelCountThenRepack()
    {
        var initial = QualityRevision.Initial;
        var repack = new QualityRevision(1, 0, true);
        var correctedRepack = new QualityRevision(2, 0, true);
        var relabeled = new QualityRevision(1, 1, false);

        Assert.Multiple(() =>
        {
            Assert.That(repack.CompareTo(initial), Is.GreaterThan(0));
            Assert.That(
                correctedRepack.CompareTo(repack),
                Is.GreaterThan(0),
                "A corrected issue of a repack must outrank the plain repack it corrects.");
            Assert.That(relabeled.CompareTo(initial), Is.GreaterThan(0));
            Assert.That(initial.CompareTo(initial), Is.Zero);
        });
    }

    [Test]
    public void TheInitialRevisionIsVersionOneAndNothingElse()
    {
        Assert.That(QualityRevision.Initial, Is.EqualTo(new QualityRevision(1, 0, false)));
    }
}
