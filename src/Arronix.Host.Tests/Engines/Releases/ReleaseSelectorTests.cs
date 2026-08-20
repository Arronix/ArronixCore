using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;
using Arronix.Host.Engines.Releases;
using FluentAssertions;


namespace Arronix.Host.Tests.Engines.Releases;

[TestFixture]
public class ReleaseSelectorTests
{
    private static readonly ReleasePolicy<TestRelease> Policy = ReleasePolicy<TestRelease>.Compile(policy => policy
        .Prefer(release => release.Resolution));

    [Test]
    public void SelectionDoesNotDependOnIndexerResultOrder()
    {
        var target = new TestTarget("one");
        var first = Option("a", new TestRelease(1080), target);
        var second = Option("b", new TestRelease(2160), target);

        ReleaseSelector.Select([first, second], Policy).Should().Be(second);
        ReleaseSelector.Select([second, first], Policy).Should().Be(second);
    }

    [Test]
    public void AStableReleaseIdentifierBreaksAnOtherwiseCompleteTie()
    {
        var target = new TestTarget("one");
        var first = Option("a", new TestRelease(1080), target);
        var second = Option("b", new TestRelease(1080), target);

        ReleaseSelector.Select([second, first], Policy).Should().Be(second);
    }

    private static ReleaseOption<TestTarget, TestRelease> Option(
        string id,
        TestRelease release,
        TestTarget target) => new(
            new ReleaseListing(
                ReleaseId.FromString(id),
                id,
                new Uri($"https://example.invalid/{id}"),
                "test",
                MediaKindId.FromString("test"),
                1,
                DateTime.UnixEpoch),
            release,
            new TargetMatch<TestTarget>(TargetDisposition.Satisfied, [target], []));

    private sealed record TestTarget(string Value) : IReleaseTarget;

    private sealed record TestRelease(int Resolution) : IRelease
    {
        public string Title => "Test";

        public int? Year => null;

        public string? Edition => null;
    }
}
