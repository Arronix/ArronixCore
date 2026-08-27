using Arronix.Client.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// Which of several overlapping refreshes is allowed to commit what it read.
/// </summary>
/// <remarks>
/// The case this exists for is two notifications about one change — a contract load, then the sweep that
/// follows it — whose store reads complete in either order. The older read landing last is the failure:
/// it puts back the state the newer one had already corrected.
/// </remarks>
[TestFixture]
internal sealed class NewestWinsTests
{
    /// <summary>Every overtaken refresh is rejected and only the newest is accepted.</summary>
    /// <remarks>
    /// Three deep, because two would let an ordering comparison pass: the request before last is stale for
    /// exactly the same reason the first one is, and both may still be in flight.
    /// </remarks>
    [Test]
    public void OnlyTheNewestRequestCommits()
    {
        var refreshes = new NewestWins();

        var first = refreshes.Request();
        refreshes.IsCurrent(first).Should().BeTrue("nothing has overtaken it yet");

        var second = refreshes.Request();
        var newest = refreshes.Request();

        using var assertions = new AssertionScope();

        refreshes.IsCurrent(first).Should().BeFalse("it may finish last and must not put back stale state");
        refreshes.IsCurrent(second).Should().BeFalse("being the one before last is not being the newest");
        refreshes.IsCurrent(newest).Should().BeTrue();

        // Order of completion is not order of request, so the answer cannot depend on the order asked.
        refreshes.IsCurrent(newest).Should().BeTrue();
        refreshes.IsCurrent(second).Should().BeFalse();
        refreshes.IsCurrent(first).Should().BeFalse();
    }

    /// <summary>A refresh nothing overtook commits, so the guard does not simply refuse everything.</summary>
    [Test]
    public void ASoleRequestCommits()
    {
        var refreshes = new NewestWins();

        refreshes.IsCurrent(refreshes.Request()).Should().BeTrue();
    }
}
