using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.JSInterop;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// What the client discards from its byte store, and what it must not.
/// </summary>
/// <remarks>
/// A content-hash key names its own bytes, so nothing here can make a load wrong: the worst an eviction can
/// do is make a later load fetch over the network. What it must never do is remove an address the running
/// installation still names, or empty the store because a manifest could not be read.
/// </remarks>
[TestFixture]
internal sealed class ContractStoreJanitorTests
{
    private const string Live = "AAAA000000000000000000000000000000000000000000000000000000000001";
    private const string Dead = "BBBB000000000000000000000000000000000000000000000000000000000002";

    /// <summary>Only the addresses the verified installation does not name are discarded.</summary>
    [Test]
    public async Task OnlyTheAddressesTheInstallationDoesNotNameAreDiscarded()
    {
        var store = new InMemoryStore(Live, Dead);
        var sweep = await new ContractStoreJanitor(store.Open()).SweepAsync(Report(Live));

        using var assertions = new AssertionScope();

        sweep.Ran.Should().BeTrue();
        sweep.Evicted.Should().Equal(Dead);
        store.Keys.Should().Equal(
            new[] { Live },
            "an address this installation publishes is not the janitor's to take");
    }

    /// <summary>
    /// A report whose manifest was never proved whole is not an installation that publishes nothing.
    /// </summary>
    /// <remarks>
    /// Its empty package list is an absence of knowledge. Sweeping against one would empty the store every
    /// time a host could not be reached, turning every recovery into a cold start.
    /// </remarks>
    [Test]
    public async Task AnUnreadableManifestSweepsNothing()
    {
        var store = new InMemoryStore(Live, Dead);
        var unreadable = Report(Live) with { InstallationHash = null, Packages = [] };

        var sweep = await new ContractStoreJanitor(store.Open()).SweepAsync(unreadable);

        using var assertions = new AssertionScope();

        sweep.Ran.Should().BeFalse();
        sweep.Evicted.Should().BeEmpty();
        store.Keys.Should().Equal(Live, Dead);
    }

    /// <summary>A host that genuinely offers nothing does empty the store.</summary>
    [Test]
    public async Task AnInstallationThatPublishesNothingEmptiesTheStore()
    {
        var store = new InMemoryStore(Live, Dead);
        var empty = Report(Live) with { Packages = [] };

        var sweep = await new ContractStoreJanitor(store.Open()).SweepAsync(empty);

        using var assertions = new AssertionScope();

        sweep.Ran.Should().BeTrue();
        sweep.Evicted.Should().BeEquivalentTo([Live, Dead]);
        store.Keys.Should().BeEmpty();
    }

    private static ContractLoadReport Report(string contentHash)
    {
        var published = new ClientContractAssembly(
            "Fixture.Store",
            "Fixture.Store.dll",
            "Fixture.Store, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            contentHash,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            16,
            []);

        return new ContractLoadReport(
            ContractCompatibility.Compatible,
            MediaContractLoader.ClientContractIdentity,
            MediaContractLoader.ClientContractIdentity,
            new string('E', 64),
            [
                new LoadedContractPackage(
                    PluginId.FromString("store.fixture"),
                    "1.0.0",
                    "Store fixture",
                    new string('C', 64),
                    [PluginId.FromString("store.fixture")],
                    [
                        new LoadedContractAssembly(
                            published,
                            ContractLoadOutcome.Loaded,
                            ContractByteSource.Network,
                            published.Length,
                            published.ContentHash,
                            published.Identity,
                            published.ModuleVersionId,
                            MediaContractLoader.ClientContractIdentity,
                            [],
                            null),
                    ]),
            ],
            [],
            [],
            true,
            null);
    }

    /// <summary>The browser's store, in memory, answering the same script the real one calls.</summary>
    private sealed class InMemoryStore(params string[] held)
    {
        private readonly List<string> _keys = [.. held];

        public IReadOnlyList<string> Keys => _keys;

        public ContractStore Open() => new(new StoreRuntime(this));

        private object? Invoke(string identifier, object?[]? args) => identifier switch
        {
            "isAvailable" => true,
            "keys" => _keys.ToArray(),
            "remove" => _keys.Remove((string)args![0]!),
            _ => throw new NotSupportedException($"This store fixture does not answer '{identifier}'."),
        };

        private sealed class StoreRuntime(InMemoryStore store) : IJSRuntime, IJSObjectReference
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
                => ValueTask.FromResult((TValue)(identifier == "import" ? this : store.Invoke(identifier, args))!);

            public ValueTask<TValue> InvokeAsync<TValue>(
                string identifier,
                CancellationToken cancellationToken,
                object?[]? args)
                => ValueTask.FromResult((TValue)(identifier == "import" ? this : store.Invoke(identifier, args))!);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
