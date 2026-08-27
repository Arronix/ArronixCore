using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.JSInterop;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// What the client proves about a declaration once the runtime holds it.
/// </summary>
/// <remarks>
/// <para>
/// Every payload here passes preflight: the bytes are exactly what they say they are, and the manifest is
/// built from those bytes. What each gets wrong is only observable after the load, which is the one step a
/// browser cannot take back — so the outcome is terminal in every case, and nothing becomes projectable.
/// </para>
/// <para>
/// Each fixture is compiled under its own assembly name and loaded on purpose, because a load context
/// cannot be unloaded and a name spent here is spent for the process.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class ContractDeclarationProofTests
{
    /// <summary>A declaration whose own code throws is refused, and the exception does not escape.</summary>
    [TestCase(Misbehaviour.ThrowingConstructor, "could not be read once loaded")]
    [TestCase(Misbehaviour.ThrowingSchema, "could not be read once loaded")]
    [TestCase(Misbehaviour.EmptySchema, "no projection schema")]
    public async Task ADeclarationThatMisbehavesAfterLoadingIsTerminal(Misbehaviour misbehaviour, string expected)
    {
        var name = "Fixture.Client." + misbehaviour;
        var report = await LoadAsync(name, misbehaviour);

        using var assertions = new AssertionScope();

        var entry = report.Packages.Single().Assemblies.Single();
        entry.Outcome.Should().Be(ContractLoadOutcome.RuntimeRefused);
        entry.Failure.Should().Contain(expected);

        report.Compatibility.Should().Be(ContractCompatibility.Terminal);
        report.CanProject.Should().BeFalse();
    }

    /// <summary>
    /// A verified declaration is retained, exposed only while the installation projects, and reused as the
    /// same instance.
    /// </summary>
    /// <remarks>
    /// Instance identity, not equality. Reading a declaration runs the payload's own code, so a second
    /// resolution would be a second answer nothing had checked.
    /// </remarks>
    [Test]
    public async Task AVerifiedDeclarationIsRetainedExposedAndReusedAsTheSameInstance()
    {
        const string name = "Fixture.Client.Verified";
        var loader = Loader(name, Misbehaviour.None, out _);

        var first = await loader.LoadAsync();

        using var assertions = new AssertionScope();

        first.CanProject.Should().BeTrue();
        first.Packages.Single().Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.Loaded);

        var contracts = loader.ContractsOf(name);
        contracts.Should().ContainSingle();
        contracts[0].EntityType.FullName.Should().Be("Fixture.Client.Entity");
        contracts[0].EntityType.Assembly.Should().BeSameAs(
            loader.Find(name),
            "the entity comes from the assembly that declared it");

        var second = await loader.LoadAsync();
        second.Packages.Single().Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.AlreadyLoaded);
        loader.ContractsOf(name).Single().Should().BeSameAs(
            contracts[0],
            "reuse hands back what was verified rather than resolving the payload again");
    }

    /// <summary>Nothing is exposed for an assembly this page did not verify.</summary>
    [Test]
    public async Task NothingIsExposedForAnInstallationThatCannotProject()
    {
        var report = await LoadAsync("Fixture.Client.Withheld", Misbehaviour.EmptySchema);

        using var assertions = new AssertionScope();
        report.CanProject.Should().BeFalse();
        Loaded("Fixture.Client.Withheld").Should().BeTrue("the refusal happened after the load, not before");
    }

    private static bool Loaded(string simpleName)
        => AssemblyLoadContext.Default.Assemblies.Any(assembly =>
            string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

    private static async Task<ContractLoadReport> LoadAsync(string name, Misbehaviour misbehaviour)
    {
        var loader = Loader(name, misbehaviour, out _);
        return await loader.LoadAsync();
    }

    private static MediaContractLoader Loader(string name, Misbehaviour misbehaviour, out byte[] image)
    {
        image = CompiledContract.Image(name, misbehaviour);

        ContractMetadataReader
            .TryRead(image, MediaContractLoader.ContractAssemblyName, out var metadata, out var unreadable)
            .Should().BeTrue(unreadable);

        // Described from the bytes, so every preflight check passes and only the runtime proof can fail.
        var published = new ClientContractAssembly(
            name,
            name + ".dll",
            metadata!.Identity,
            Convert.ToHexString(SHA256.HashData(image)),
            metadata.ModuleVersionId,
            image.Length,
            metadata.Declarations);

        var manifest = new ClientContractManifest(
            MediaContractLoader.ClientContractIdentity,
            new string('B', 64),
            [
                new ClientContractPackage(
                    PluginId.FromString("proof.fixture"),
                    "1.0.0",
                    "Proof fixture",
                    [published],
                    [PluginId.FromString("proof.fixture")],
                    new string('C', 64)),
            ],
            []);

        var bytes = image;

        var handler = new StubHandler(path =>
            path.EndsWith("client-contracts", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(manifest, ApiJsonOptions.Default),
                        Encoding.UTF8,
                        "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://host.invalid/") };
        return new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));
    }

    private sealed class StubHandler(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(answer(request.RequestUri!.AbsolutePath));
    }

    private sealed class RefusingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException();

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new NotSupportedException();
    }
}
