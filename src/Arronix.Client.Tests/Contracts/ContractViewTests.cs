using System.Linq;
using Arronix.Client.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// What a view of the installed contracts commits, and what it says when a subscriber refuses.
/// </summary>
/// <remarks>
/// A transaction is numbered under the lease that ran it, and its caller resumes whenever the scheduler
/// says. What a view shows must follow the first order and not the second, and what it shows is one value:
/// the record that transaction sealed, whole or not at all.
/// </remarks>
[TestFixture]
internal sealed class ContractViewTests
{
    private const string Held = "AAAA000000000000000000000000000000000000000000000000000000000009";

    /// <summary>An overtaken transaction commits nothing, however late it arrives.</summary>
    /// <remarks>
    /// Both are completed here rather than raced, because which record wins must not depend on the order
    /// the scheduler happened to resume their callers in.
    /// </remarks>
    [Test]
    public async Task AnOvertakenTransactionCommitsNothing()
    {
        var view = new ContractView(Reloader(new InMemoryContractStore()));

        var changes = 0;
        view.Changed += (_, _) => changes++;

        var older = new TaskCompletionSource<ContractReloadResult>();
        var newer = new TaskCompletionSource<ContractReloadResult>();

        var overtaken = view.CommitAsync(older.Task);
        var newest = view.CommitAsync(newer.Task);

        newer.SetResult(Sealed(2, Held));
        await newest;

        var shown = view.Snapshot;
        var announced = changes;

        older.SetResult(Sealed(1));
        await overtaken;

        using var assertions = new AssertionScope();

        view.Snapshot.Should().BeSameAs(
            shown,
            "an overtaken transaction commits no part of what it sealed: not its keys, not its "
            + "installation and not its failures");

        shown.Sequence.Should().Be(2);
        shown.StoredKeys.Should().Equal(Held);
        changes.Should().Be(announced, "an overtaken transaction has nothing to announce");
    }

    /// <summary>
    /// A subscriber reads the snapshot this signal announces, and every refusal is still reported.
    /// </summary>
    /// <remarks>
    /// This is where the boundary is drawn. What a transaction did is committed once and then announced, so
    /// a subscriber reads the value it was told about. A subscriber refusing that signal happens after it
    /// was handed the value, so it is reported at the announcement — whole, in order, denying nothing to
    /// the subscriber after it.
    /// </remarks>
    [Test]
    public async Task ASubscriberSeesTheCommittedSnapshotAndEveryRefusalIsReported()
    {
        var view = new ContractView(Reloader(new InMemoryContractStore()));

        ContractReloadResult? seen = null;

        view.Changed += (_, _) => throw new InvalidOperationException("the first subscriber refused");
        view.Changed += (_, _) => seen = view.Snapshot;
        view.Changed += (_, _) => throw new InvalidOperationException("the third subscriber refused");

        await view.CommitAsync(Task.FromResult(Sealed(1, Held)));

        using var assertions = new AssertionScope();

        seen.Should().NotBeNull("the subscriber between two refusals was told whatever the first did");
        seen.Should().BeSameAs(
            view.Snapshot,
            "a subscriber reads the snapshot this signal announced, and nothing publishes a second one");
        seen!.StoredKeys.Should().Equal(
            new[] { Held },
            "the commit came before the announcement, not after it");

        view.Refused.Select(failure => failure.Message).Should().SatisfyRespectively(
            first => first.Should().Contain("the first subscriber refused"),
            third => third.Should().Contain(
                "the third subscriber refused",
                "keeping the last refusal loses the first exactly as completely as dropping it"));

        view.Refused.Should().OnlyContain(
            failure => failure.Stage == ContractFailureStage.Changed,
            "these are refusals of the announcement, not of anything the transaction itself did");
    }

    /// <summary>A transaction overtaken while announcing publishes nothing over the one that overtook it.</summary>
    /// <remarks>
    /// Telling subscribers is where a newer transaction can commit, because a subscriber is free to start
    /// one. This is the interleaving that separate atomics lose: a transaction that took a right to commit,
    /// then paused, would publish over the newer one that ran while it waited, and a guard that had already
    /// advanced would not stop it. Deciding and publishing are one compare-and-swap, so what stands can only
    /// move forward, and refusals reach the commit that raised them or nothing.
    /// </remarks>
    [Test]
    public async Task ATransactionOvertakenWhileAnnouncingPublishesNothingOverIt()
    {
        var view = new ContractView(Reloader(new InMemoryContractStore()));

        var overtaking = true;
        var refusals = 0;

        view.Changed += (_, _) =>
        {
            if (!overtaking)
            {
                return;
            }

            // Once, and from inside the announcement: a subscriber committing a newer transaction is the
            // only way one lands while an older one is still telling the subscribers after this.
            overtaking = false;
            view.CommitAsync(Task.FromResult(Sealed(2, Held))).GetAwaiter().GetResult();
        };

        view.Changed += (_, _) => throw new InvalidOperationException($"refusal {++refusals}");

        await view.CommitAsync(Task.FromResult(Sealed(1)));

        using var assertions = new AssertionScope();

        view.Snapshot.Sequence.Should().Be(
            2,
            "the older transaction resumed after the newer one committed, and what a view shows never "
            + "goes backwards");

        view.Refused.Should().ContainSingle()
            .Which.Message.Should().Be(
                "refusal 1",
                "the newer transaction recorded its own refusal, and the one it overtook recorded nothing "
                + "over it");
    }

    /// <summary>A reload and a discard both commit what they produced, and show it.</summary>
    [Test]
    public async Task AReloadAndADiscardEachCommitWhatTheyProduced()
    {
        var browser = new InMemoryContractStore(Held);
        var view = new ContractView(Reloader(browser));

        await view.ReloadAsync();
        var reloaded = view.Snapshot;

        await view.DiscardStoredBytesAsync();

        using var assertions = new AssertionScope();

        reloaded.Report.Should().NotBeNull("the host answered, so this reload read an installation");
        reloaded.StoredKeys.Should().BeEmpty("this host publishes nothing, so nothing held is still named");
        reloaded.Failures.Should().BeEmpty();

        view.Snapshot.Sequence.Should().Be(2, "the discard is a transaction of its own");
        view.Snapshot.Report.Should().BeSameAs(
            reloaded.Report,
            "discarding bytes reads no installation and does not invent one");
        browser.Keys.Should().BeEmpty();
        view.Refused.Should().BeEmpty();
    }

    private static ContractReloadResult Sealed(long sequence, params string[] keys)
        => new(sequence, null, keys, []);

    private static ContractReloader Reloader(InMemoryContractStore browser)
    {
        var store = browser.Open();
        var http = ContractHost.OfferingNothing();

        return new ContractReloader(
            new MediaContractLoader(http, store),
            new ContractStoreJanitor(store),
            store);
    }
}
