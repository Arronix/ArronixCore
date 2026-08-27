using System.IO;
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
/// Every payload passes preflight: its bytes are exactly what they say they are, and the manifest is built
/// from those bytes. What each gets wrong is observable only after the load, which is the one step a browser
/// cannot take back — so a refusal here is terminal and nothing becomes projectable.
/// </para>
/// <para>
/// Each payload is loaded into a collectible context of its own. A browser has only the one it cannot
/// unload; these tests need many, and spending a process-wide assembly name per case would make them
/// order-dependent on each other.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class ContractDeclarationProofTests
{
    [TestCase(Misbehaviour.ThrowingConstructor, "could not be read once loaded")]
    [TestCase(Misbehaviour.ThrowingContext, "could not be read once loaded")]
    [TestCase(Misbehaviour.ThrowingEntityTypeInfo, "could not be read once loaded")]
    [TestCase(Misbehaviour.ThrowingSchema, "could not be read once loaded")]
    [TestCase(Misbehaviour.NullSchema, "answers null")]
    [TestCase(Misbehaviour.IncoherentRoot, "its own context does not hold")]
    [TestCase(Misbehaviour.ForeignRoot, "as the entity metadata of")]
    [TestCase(Misbehaviour.ForeignContext, "rather than from the assembly that declared it")]
    [TestCase(Misbehaviour.IndirectBase, "the host published 0")]
    [TestCase(Misbehaviour.DigestMismatch, "does not hash to what it declares")]
    [TestCase(Misbehaviour.UnstableEntityTypeInfo, "its own context does not hold")]
    [TestCase(Misbehaviour.DeepSchema, "nests deeper than")]
    [TestCase(Misbehaviour.CyclicSchema, "contains itself")]
    public async Task ADeclarationThatDoesNotSurviveTheProofIsTerminal(Misbehaviour misbehaviour, string expected)
    {
        var loader = Loader("Fixture.Client." + misbehaviour, misbehaviour, out var name);
        var report = await loader.LoadAsync();

        using var assertions = new AssertionScope();

        var entry = report.Packages.Single().Assemblies.Single();
        entry.Outcome.Should().Be(ContractLoadOutcome.RuntimeRefused);
        entry.Failure.Should().Contain(expected);

        report.Compatibility.Should().Be(ContractCompatibility.Terminal);
        report.CanProject.Should().BeFalse();

        // Nothing is exposed for an assembly whose declaration did not survive, though it is loaded.
        loader.ContractsOf(name).Should().BeEmpty();
        loader.Find(name).Should().BeNull();
    }

    /// <summary>A coherent declaration is admitted, captured, and reused without being read again.</summary>
    [Test]
    public async Task ACoherentDeclarationIsAdmittedCapturedAndReusedWithoutBeingReadAgain()
    {
        var loader = Loader("Fixture.Client.Coherent", Misbehaviour.None, out var name);

        var first = await loader.LoadAsync();

        using var assertions = new AssertionScope();

        first.Packages.Single().Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.Loaded);
        first.CanProject.Should().BeTrue();

        var contract = loader.ContractsOf(name).Should().ContainSingle().Which;
        contract.EntityType.FullName.Should().Be("Fixture.Client.Entity");
        contract.EntityType.Assembly.Should().BeSameAs(loader.Find(name));
        contract.EntryPointType.FullName.Should().Be("Fixture.Client.Entry");
        contract.Schema.Should().BeEmpty("an empty schema is a schema");

        // The captured values, not a second reading: the entity metadata is the context's own answer.
        contract.SerializationContext.GetTypeInfo(contract.EntityType).Should().BeSameAs(contract.EntityTypeInfo);

        var second = await loader.LoadAsync();
        second.Packages.Single().Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.AlreadyLoaded);

        var reused = loader.ContractsOf(name).Single();
        reused.Should().BeSameAs(contract, "reuse hands back what was captured");
        reused.SerializationContext.Should().BeSameAs(contract.SerializationContext);
        reused.EntityTypeInfo.Should().BeSameAs(contract.EntityTypeInfo);
        reused.Schema.Should().BeSameAs(contract.Schema);
    }

    /// <summary>
    /// The shipped movies contract is admitted through the ordinary loader, not only synthetic fixtures.
    /// </summary>
    /// <remarks>
    /// The generator, the framework's serialization generator and this proof have to agree about one real
    /// assembly for any of the synthetic cases to mean anything.
    /// </remarks>
    [Test]
    public async Task TheGeneratedMoviesContractIsAdmitted()
    {
        const string name = "Arronix.Media.Movies";
        var image = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", name + ".dll"));

        var loader = Loader(name, image, out _);
        var report = await loader.LoadAsync();

        using var assertions = new AssertionScope();

        report.Packages.Single().Assemblies.Single().Outcome.Should().Be(
            ContractLoadOutcome.Loaded,
            report.Packages.Single().Assemblies.Single().Failure ?? "the shipped contract must be admissible");
        report.CanProject.Should().BeTrue();

        var contract = loader.ContractsOf(name).Should().ContainSingle().Which;
        contract.EntityType.FullName.Should().Be("Arronix.Media.Movies.Movie");
        contract.Schema.Should().NotBeEmpty();
        contract.SerializationContext.GetTypeInfo(contract.EntityType).Should().BeSameAs(contract.EntityTypeInfo);
    }

    private static MediaContractLoader Loader(string assemblyName, Misbehaviour misbehaviour, out string name)
    {
        var fixture = CompiledContract.Build(assemblyName, misbehaviour);
        return Loader(assemblyName, fixture.Payload, out name, fixture.Auxiliary);
    }

    /// <summary>
    /// A loader over a manifest built from the payload's own bytes, loading into a collectible context.
    /// </summary>
    private static MediaContractLoader Loader(
        string assemblyName,
        byte[] image,
        out string name,
        byte[]? auxiliary = null)
    {
        name = assemblyName;

        ContractMetadataReader
            .TryRead(image, MediaContractLoader.ContractAssemblyName, out var metadata, out var unreadable)
            .Should().BeTrue(unreadable);

        var published = new ClientContractAssembly(
            assemblyName,
            assemblyName + ".dll",
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

        var handler = new StubHandler(path =>
            path.EndsWith("client-contracts", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(manifest, ApiJsonOptions.Default),
                        Encoding.UTF8,
                        "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });

        var context = new AssemblyLoadContext(assemblyName + ".proof", isCollectible: true);

        if (auxiliary is not null)
        {
            // Loaded into the payload's own context so the payload binds to it there, and nowhere else.
            var companion = context.LoadFromStream(new MemoryStream(auxiliary, writable: false));
            context.Resolving += (_, requested) =>
                requested.Name == CompiledContract.AuxiliaryName ? companion : null;
        }

        return new MediaContractLoader(
            new HttpClient(handler) { BaseAddress = new Uri("https://host.invalid/") },
            new ContractStore(new RefusingJsRuntime()),
            bytes => context.LoadFromStream(new MemoryStream(bytes, writable: false)));
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
