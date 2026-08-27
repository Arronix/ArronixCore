using Arronix.Client.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// Which of several completed transactions is allowed to stand, and what may be attached to it.
/// </summary>
/// <remarks>
/// Transactions are numbered under the lease that runs them; their callers resume in whatever order the
/// scheduler chooses. Deciding and publishing must be one step: an older result that won a separate
/// right-to-commit and was then paused would publish over the newer one that ran while it waited, and a
/// guard that had already advanced would not stop it.
/// </remarks>
[TestFixture]
internal sealed class NewestWinsTests
{
    /// <summary>Publishing is the deciding, so there is no moment between them to be overtaken in.</summary>
    /// <remarks>
    /// The interleaving this rules out, in the order it happens: an older transaction wins, a newer one
    /// wins over it, and the older then resumes with nothing left that can move what stands.
    /// </remarks>
    [Test]
    public void WhatStandsCannotRegressWhenAnOlderTransactionResumesLast()
    {
        var commits = new NewestWins();

        var older = commits.Publish(Sealed(2));
        older.Should().NotBeNull("nothing newer had been published when it arrived");
        commits.Current.Should().BeSameAs(older, "deciding and publishing are the same step");

        var newer = commits.Publish(Sealed(3));
        newer.Should().NotBeNull();
        commits.Current.Should().BeSameAs(newer);

        // The older caller resumes here. Everything left to it is refused.
        using var assertions = new AssertionScope();

        commits.Publish(older!.Result).Should().BeNull("it cannot publish over the one that overtook it");
        commits.Attach(older, [Refusal]).Should().BeFalse("the commit it announced no longer stands");

        commits.Current.Should().BeSameAs(newer, "what stands only ever moves forward");
        commits.Current.Refused.Should().BeEmpty("an overtaken announcement attaches nothing");
    }

    /// <summary>Every overtaken transaction is refused, not only the one before last.</summary>
    /// <remarks>Three deep, because two would let an off-by-one ordering slip pass.</remarks>
    [Test]
    public void EveryOvertakenTransactionIsRefused()
    {
        var commits = new NewestWins();

        var newest = commits.Publish(Sealed(3));

        using var assertions = new AssertionScope();

        commits.Publish(Sealed(1)).Should().BeNull("it finished last and must not put back stale state");
        commits.Publish(Sealed(2)).Should().BeNull("being the one before last is not being the newest");
        commits.Publish(Sealed(3)).Should().BeNull("a number that stands is not newer than itself");
        commits.Current.Should().BeSameAs(newest);
    }

    /// <summary>An announcement's refusals reach the commit that raised it, and nothing else.</summary>
    [Test]
    public void RefusalsAttachToTheCommitThatRaisedThem()
    {
        var commits = new NewestWins();
        var published = commits.Publish(Sealed(1))!;

        using var assertions = new AssertionScope();

        commits.Attach(published, [Refusal]).Should().BeTrue();
        commits.Current.Refused.Should().ContainSingle().Which.Should().BeSameAs(Refusal);
        commits.Current.Result.Should().BeSameAs(
            published.Result,
            "attaching refusals leaves the installation this commit named exactly where it was");

        commits.Attach(published, []).Should().BeFalse(
            "the commit they were attached to is no longer the one standing");
    }

    private static readonly ContractFailure Refusal =
        new(ContractFailureStage.Changed, "a subscriber refused");

    private static ContractReloadResult Sealed(long sequence) => new(sequence, null, [], []);
}
