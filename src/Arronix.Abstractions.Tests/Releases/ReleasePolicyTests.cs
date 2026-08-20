using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;


namespace Arronix.Abstractions.Tests.Releases;

[TestFixture]
public class ReleasePolicyTests
{
    private static readonly ReleasePolicy<TestRelease> Policy = ReleasePolicy<TestRelease>.Compile(policy => policy
        .Require(release => !release.Rejected, "rejected")
        .Prefer(release => release.Resolution, isKnown: value => value.HasValue)
        .Prefer(release => release.Generation, preferGreater: false)
        .Facet(release => release.Revision));

    [Test]
    public void RequirementsRunBeforeRanking()
    {
        Assert.That(Policy.Admit(new TestRelease(2160, 0, 0, true)).IsAdmitted, Is.False);
    }

    [Test]
    public void CorePreferencesAreLexicographicAndFacetsOnlyBreakATie()
    {
        var lowerResolutionWithLargeFacet = new TestRelease(1080, 0, 10);
        var higherResolution = new TestRelease(2160, 4, -10);

        Assert.That(Policy.Compare(higherResolution, lowerResolutionWithLargeFacet), Is.Positive);
    }

    private sealed record TestRelease(int? Resolution, int Generation, int Revision, bool Rejected = false) : IRelease
    {
        public string Title => "Test";

        public int? Year => null;

        public string? Edition => null;
    }
}
