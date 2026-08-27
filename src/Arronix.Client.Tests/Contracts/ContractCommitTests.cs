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
/// The one path that ends with the runtime holding an assembly.
/// </summary>
/// <remarks>
/// <para>
/// Separate because it loads an assembly into the test process on purpose and a load context cannot be
/// undone. It uses a fixture no other fixture asserts about, so the side effect cannot make another test's
/// residency question answer wrongly.
/// </para>
/// <para>
/// It is the only place the post-load proof runs, in both directions: the ordinary narrative proves it
/// accepts a real load, and the first part proves it refuses a runtime that returns an assembly other than
/// the bytes it was handed. Nothing outside the runtime can provoke that, so the load is supplied through
/// the loader's internal seam.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ContractCommitTests
{
    private const string FixtureAssemblyName = "Arronix.Format.Video";
    private const string FixtureFileName = "Arronix.Format.Video.dll";

    /// <summary>
    /// One narrative, in order, because a load context cannot be rewound between tests.
    /// </summary>
    /// <remarks>
    /// A runtime that returns the wrong assembly, then load, reuse, and the host publishing a different
    /// build. Splitting these into separate cases would make them order-dependent on each other in a way
    /// NUnit does not promise, and the sequence is the behaviour anyway.
    /// </remarks>
    [Test]
    public async Task AVerifiedInstallationIsLoadedReusedAndThenTerminalWhenTheHostMovesOn()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", FixtureFileName);
        File.Exists(path).Should().BeTrue($"the build must stage '{path}'");

        var content = await File.ReadAllBytesAsync(path);
        ContractMetadataReader
            .TryRead(content, MediaContractLoader.ContractAssemblyName, out var metadata, out var unreadable)
            .Should().BeTrue(unreadable);

        var truthful = new ClientContractAssembly(
            FixtureAssemblyName,
            FixtureFileName,
            metadata!.Identity,
            Convert.ToHexString(SHA256.HashData(content)),
            metadata.ModuleVersionId,
            content.Length,
            metadata.Declarations);

        // Part one: the runtime returns something other than the bytes it was handed. Two truthful
        // assemblies are preflighted; the seam answers the first load with an assembly that is real,
        // already loaded, and not this one.
        var movies = Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", "Arronix.Media.Movies.dll");
        var moviesContent = await File.ReadAllBytesAsync(movies);
        ContractMetadataReader
            .TryRead(moviesContent, MediaContractLoader.ContractAssemblyName, out var moviesMetadata, out _)
            .Should().BeTrue();

        var moviesPublished = new ClientContractAssembly(
            "Arronix.Media.Movies",
            "Arronix.Media.Movies.dll",
            moviesMetadata!.Identity,
            Convert.ToHexString(SHA256.HashData(moviesContent)),
            moviesMetadata.ModuleVersionId,
            moviesContent.Length,
            moviesMetadata.Declarations);

        var twoPackages = new ClientContractManifest(
            MediaContractLoader.ClientContractIdentity,
            new string('B', 64),
            [
                new ClientContractPackage(
                    PluginId.FromString("commit.first"), "1.0.0", "First",
                    [moviesPublished], [PluginId.FromString("commit.first")], new string('C', 64)),
                new ClientContractPackage(
                    PluginId.FromString("commit.second"), "1.0.0", "Second",
                    [truthful], [PluginId.FromString("commit.second")], new string('C', 64)),
            ],
            []);

        using (var wrongHandler = new StubHandler(requestPath =>
            requestPath.EndsWith("client-contracts", StringComparison.Ordinal)
                ? Json(twoPackages)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        requestPath.Contains("Arronix.Media.Movies", StringComparison.Ordinal)
                            ? moviesContent
                            : content),
                }))
        {
            using var wrongHttp = new HttpClient(wrongHandler) { BaseAddress = new Uri("https://host.invalid/") };

            // The first load answers with a real assembly that is already loaded and is not the one whose
            // bytes were verified; any later load is the ordinary one. A correct commit never makes the
            // second call, which is what the residency assertions below rely on.
            var impostor = typeof(ClientContractManifest).Assembly;
            var loads = 0;
            var wrongLoader = new MediaContractLoader(
                wrongHttp,
                new ContractStore(new RefusingJsRuntime()),
                bytes => ++loads == 1
                    ? impostor
                    : AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes, writable: false)));

            var wrong = await wrongLoader.LoadInstallationAsync();
            var byWrongPackage = wrong.Packages.ToDictionary(package => package.Id.Value, StringComparer.Ordinal);

            using (new AssertionScope())
            {
                wrong.Compatibility.Should().Be(ContractCompatibility.Terminal);
                wrong.CanProject.Should().BeFalse();
                wrong.Failure.Should().Contain("Reload");

                var refused = byWrongPackage["commit.first"].Assemblies.Single();
                refused.Outcome.Should().Be(ContractLoadOutcome.RuntimeRefused);
                refused.Failure.Should().Contain("loaded as");

                byWrongPackage["commit.second"].Assemblies.Single().Outcome.Should().Be(
                    ContractLoadOutcome.Verified,
                    "the commit stopped at the first entry, so the second is verified and not loaded");

                wrongLoader.Find("Arronix.Media.Movies").Should().BeNull();
                wrongLoader.Find(FixtureAssemblyName).Should().BeNull();

                // Residency, not just an absent projection: Find answering null says the loader will not
                // hand the assembly out, and says nothing about whether the runtime holds it.
                Resident("Arronix.Media.Movies").Should().BeFalse();
                Resident(FixtureAssemblyName).Should().BeFalse();

                // Nor are its declarations reachable. An assembly the runtime substituted must not become
                // the source of a contract, and the accessor is the other door into one.
                wrongLoader.ContractsOf("Arronix.Media.Movies").Should().BeEmpty();
                wrongLoader.ContractsOf(FixtureAssemblyName).Should().BeEmpty();
                loads.Should().Be(1, "the commit stopped at the entry the runtime got wrong");
            }
        }

        // Part two: the ordinary narrative, over the real default-context load.
        var published = truthful;

        using var handler = new StubHandler(requestPath =>
            requestPath.EndsWith("client-contracts", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(Manifest(published), ApiJsonOptions.Default),
                        Encoding.UTF8,
                        "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host.invalid/") };
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        var first = await loader.LoadInstallationAsync();

        using (new AssertionScope())
        {
            first.Compatibility.Should().Be(ContractCompatibility.Compatible);
            first.CanProject.Should().BeTrue();

            var entry = first.Packages.Single().Assemblies.Single();
            entry.Outcome.Should().Be(
                ContractLoadOutcome.Loaded,
                "the runtime took these bytes and the post-load proof agreed about what it produced");
            entry.Source.Should().Be(ContractByteSource.Network);

            loader.Find(FixtureAssemblyName).Should().NotBeNull();
            Resident(FixtureAssemblyName).Should().BeTrue();
        }

        // A second pass finds it resident. It is reused rather than reloaded, and the report says where the
        // bytes did not come from as precisely as it says where they did.
        var second = await loader.LoadInstallationAsync();

        using (new AssertionScope())
        {
            var reused = second.Packages.Single().Assemblies.Single();
            second.Compatibility.Should().Be(ContractCompatibility.Compatible);
            second.CanProject.Should().BeTrue();
            reused.Outcome.Should().Be(ContractLoadOutcome.AlreadyLoaded);
            reused.Source.Should().Be(
                ContractByteSource.Resident,
                "nothing was fetched and something is held, which is not the same as nothing being fetched");
        }

        // The host now publishes a different build under the same name. The page cannot unload what it
        // holds, so it can never satisfy this installation: terminal, not merely refused.
        published = truthful with
        {
            ContentHash = new string('D', 64),
            ModuleVersionId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
        };

        var third = await loader.LoadInstallationAsync();

        using var assertions = new AssertionScope();

        third.Compatibility.Should().Be(ContractCompatibility.Terminal);
        third.CanProject.Should().BeFalse();
        third.Failure.Should().Contain("Reload");
        third.Packages.Single().Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.NameAlreadyResident);
        loader.Find(FixtureAssemblyName).Should().BeNull();

        // And it stays terminal even if the host goes back to what this page already holds.
        published = truthful;
        (await loader.LoadInstallationAsync()).Compatibility.Should().Be(ContractCompatibility.Terminal);
    }

    /// <summary>
    /// Bytes this page already holds cannot be given a new meaning by a later manifest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reused entry is the one path that fetches nothing and loads nothing. There are no bytes to
    /// preflight and no load to prove afterwards, so whatever the reuse gate accepts is accepted by nothing
    /// else — which is why the declarations have to be part of what "the same one" means. Without that, a
    /// host could publish different entity types or different hashes for a payload this page verified on an
    /// earlier pass, and the page would project them.
    /// </para>
    /// <para>
    /// The reference language pack is used because it is built against this contract line, declares no
    /// client contract, and no other case loads it — a browser cannot unload an assembly, so a page that
    /// must hold one needs a name no other test has spent. Declaring none is the sharper fixture anyway:
    /// the restatement here invents a contract for a payload that carries none, which is the disagreement
    /// a reused entry would otherwise never look at.
    /// </para>
    /// </remarks>
    [Test]
    public async Task RestatingTheDeclarationOfBytesThisPageHoldsIsTerminal()
    {
        const string name = "Arronix.Language.Reference";
        var path = Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", name + ".dll");
        var content = await File.ReadAllBytesAsync(path);

        ContractMetadataReader
            .TryRead(content, MediaContractLoader.ContractAssemblyName, out var metadata, out var unreadable)
            .Should().BeTrue(unreadable);

        metadata!.Declarations.Should().BeEmpty("this payload owns no item, so it declares no client contract");

        var truthful = new ClientContractAssembly(
            name,
            name + ".dll",
            metadata.Identity,
            Convert.ToHexString(SHA256.HashData(content)),
            metadata.ModuleVersionId,
            content.Length,
            metadata.Declarations);

        var published = truthful;

        using var handler = new StubHandler(requestPath =>
            requestPath.EndsWith("client-contracts", StringComparison.Ordinal)
                ? Json(Manifest(published))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host.invalid/") };
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        (await loader.LoadInstallationAsync()).Packages.Single().Assemblies.Single().Outcome.Should().Be(
            ContractLoadOutcome.Loaded,
            "nothing else in this process loads this payload, so this pass is the one that does");

        // Byte for byte the payload this page verified. Only the meaning the host publishes for it moved.
        published = truthful with
        {
            Declarations =
            [
                new ClientContractDeclaration(
                    name + ".Invented",
                    name + ".Entity",
                    new string('E', 64),
                    new string('F', 64)),
            ],
        };

        var restated = await loader.LoadInstallationAsync();
        var entry = restated.Packages.Single().Assemblies.Single();

        using var assertions = new AssertionScope();

        entry.Outcome.Should().NotBe(
            ContractLoadOutcome.AlreadyLoaded,
            "reuse would skip both the preflight and the post-load declaration checks");
        entry.Outcome.Should().Be(ContractLoadOutcome.NameAlreadyResident);
        entry.Failure.Should().Contain("declaration");

        restated.Compatibility.Should().Be(ContractCompatibility.Terminal);
        restated.CanProject.Should().BeFalse();
        loader.Find(name).Should().BeNull();
    }

    private static HttpResponseMessage Json(ClientContractManifest manifest)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(manifest, ApiJsonOptions.Default),
                Encoding.UTF8,
                "application/json"),
        };

    private static ClientContractManifest Manifest(ClientContractAssembly assembly)
        => new(
            MediaContractLoader.ClientContractIdentity,
            new string('B', 64),
            [
                new ClientContractPackage(
                    PluginId.FromString("commit.fixture"), "1.0.0", "Commit fixture",
                    [assembly],
                    [PluginId.FromString("commit.fixture")],
                    new string('C', 64)),
            ],
            []);

    private static bool Resident(string simpleName)
        => AssemblyLoadContext.Default.Assemblies.Any(assembly =>
            string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

    private sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond(request.RequestUri!.AbsolutePath));
    }

    private sealed class RefusingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException("This test process has no browser.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new NotSupportedException("This test process has no browser.");
    }
}
