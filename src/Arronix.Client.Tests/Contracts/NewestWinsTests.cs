using Arronix.Client.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// Which of several completed transactions is allowed to commit what it produced.
/// </summary>
/// <remarks>
/// Transactions are numbered under the lease that runs them; their callers resume in whatever order the
/// scheduler chooses. An older one landing last is the failure — it puts back the state a newer one had
/// already corrected.
/// </remarks>
[TestFixture]
internal sealed class NewestWinsTests
{
    /// <summary>Every overtaken transaction is rejected and only the newest is accepted.</summary>
    /// <remarks>
    /// Three deep, because two would let an ordering slip pass: the one before last is stale for exactly
    /// the same reason the first is, and both may still be in flight.
    /// </remarks>
    [Test]
    public void OnlyTheNewestCompletedTransactionCommits()
    {
        var commits = new NewestWins();

        using var assertions = new AssertionScope();

        commits.Accepts(3).Should().BeTrue("nothing has committed yet");
        commits.IsNewest(3).Should().BeTrue();

        commits.Accepts(1).Should().BeFalse("it finished last and must not put back stale state");
        commits.Accepts(2).Should().BeFalse("being the one before last is not being the newest");
        commits.IsNewest(3).Should().BeTrue("a rejected result commits nothing, so it moves nothing");

        commits.Accepts(4).Should().BeTrue();
        commits.IsNewest(3).Should().BeFalse("a newer result has committed over it");
        commits.IsNewest(4).Should().BeTrue();
    }

    /// <summary>The same number twice is the same transaction, and commits once.</summary>
    [Test]
    public void OneTransactionCommitsOnce()
    {
        var commits = new NewestWins();

        using var assertions = new AssertionScope();

        commits.Accepts(1).Should().BeTrue();
        commits.Accepts(1).Should().BeFalse("a number that has committed is not newer than itself");
    }
}
