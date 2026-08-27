using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// What a view of the installed contracts commits, and what it says when it cannot.
/// </summary>
/// <remarks>
/// Every refresh here is started by a notification and awaited by nobody, which is what makes both
/// properties matter: an overtaken read must not land on top of the newest state, and a failure must be
/// somewhere it can be read rather than on a task no one observes.
/// </remarks>
[TestFixture]
internal sealed class ContractViewTests
{
    private const string Held = "AAAA000000000000000000000000000000000000000000000000000000000009";

    /// <summary>An overtaken refresh commits neither its state nor its failure, and announces nothing.</summary>
    /// <remarks>
    /// The first refresh reads a store that still holds an address, then completes late. Committing it
    /// would put back what the newer refresh saw evicted, pair those keys with a report from another
    /// moment, and answer a failure the newest refresh had already replaced.
    /// </remarks>
    [Test]
    public async Task AnOvertakenRefreshCommitsNothing()
    {
        var browser = new InMemoryContractStore(Held);
        var view = View(browser, out var loader);

        var changes = 0;
        view.Changed += (_, _) => changes++;
        view.Changed += (_, _) => throw new InvalidOperationException("the newest refresh was refused");

        var release = browser.HoldNextListing(out var listing);
        var overtaken = view.RefreshAsync();
        await listing.WaitAsync(TimeSpan.FromSeconds(5));

        // A newer refresh, over a store and a report the older one will never see.
        browser.Discard(Held);
        await loader.LoadAsync();
        await view.RefreshAsync();

        var newest = view.Report;
        newest.Should().BeSameAs(loader.Report);

        // Counted across the release, so only the overtaken refresh's own announcements are in question.
        var announced = changes;

        release.SetResult();
        await overtaken;

        using var assertions = new AssertionScope();

        view.StoredKeys.Should().BeEmpty("an overtaken read must not put back what the newest one saw gone");
        view.Report.Should().BeSameAs(newest, "the pair this view shows comes from one refresh, not two");
        view.LastFailure.Should().Contain(
            "the newest refresh was refused",
            "an overtaken refresh must not answer a failure the newest one already stated");
        changes.Should().Be(announced, "an overtaken refresh has nothing to announce");
    }

    /// <summary>A refusing subscriber is recorded, and does not deny the next one.</summary>
    /// <remarks>
    /// Nothing awaits a refresh a notification started, so a refusal raised through the whole delegate
    /// would both deny every later subscriber and fault a task no one observes.
    /// </remarks>
    [Test]
    public async Task ARefusingSubscriberIsRecordedAndDoesNotDenyTheNext()
    {
        var browser = new InMemoryContractStore(Held);
        var view = View(browser, out _);

        var reached = false;
        view.Changed += (_, _) => throw new InvalidOperationException("the first subscriber refused");
        view.Changed += (_, _) => reached = true;

        await view.RefreshAsync();

        using var assertions = new AssertionScope();

        reached.Should().BeTrue("the second subscriber is told whatever the first did");
        view.LastFailure.Should().Contain(
            "the first subscriber refused",
            "a refusal has nowhere to be returned, so it is stated here");
        view.StoredKeys.Should().Equal(new[] { Held }, "the refusal came after the commit, not instead of it");
    }

    /// <summary>A reload the view drove is committed and shown.</summary>
    [Test]
    public async Task AReloadCommitsWhatItProduced()
    {
        var browser = new InMemoryContractStore(Held);
        var view = View(browser, out var loader);

        await view.ReloadAsync();

        using var assertions = new AssertionScope();

        view.Report.Should().BeSameAs(loader.Report);
        view.StoredKeys.Should().BeEmpty("this host publishes nothing, so nothing held is still named");
        view.LastFailure.Should().BeNull();
        view.LastReloadFailure.Should().BeNull();
    }

    /// <summary>A view over a host that offers nothing, so a read costs one manifest and no bytes.</summary>
    private static ContractView View(InMemoryContractStore browser, out MediaContractLoader loader)
    {
        var manifest = new ClientContractManifest(
            MediaContractLoader.ClientContractIdentity,
            new string('B', 64),
            [],
            []);

        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(manifest, ApiJsonOptions.Default),
                Encoding.UTF8,
                "application/json"),
        }))
        {
            BaseAddress = new Uri("https://host.invalid/"),
        };

        var store = browser.Open();
        loader = new MediaContractLoader(http, store);

        return new ContractView(loader, store, new ContractReloader(loader, new ContractStoreJanitor(store)));
    }

    private sealed class StubHandler(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(answer(request.RequestUri!.AbsolutePath));
    }
}
