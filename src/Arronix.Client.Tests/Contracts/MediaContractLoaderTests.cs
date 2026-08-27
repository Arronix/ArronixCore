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
/// What the client decides before a browser runtime is allowed near a payload.
/// </summary>
/// <remarks>
/// <para>
/// Every case asserts both that the refusal is reported and that the payload was never loaded. A load
/// context cannot unload, so "refused but resident" is the same failure as "accepted".
/// </para>
/// <para>
/// The fixture assemblies are staged as files rather than referenced, so this test process does not hold
/// them; otherwise "was it loaded?" would have no answer.
/// </para>
/// </remarks>
[TestFixture]
public sealed class MediaContractLoaderTests
{
    private const string Package = "fixture.contracts";
    private const string FixtureAssemblyName = "Arronix.Media.Movies";
    private const string FixtureFileName = "Arronix.Media.Movies.dll";

    private static readonly string ContractIdentity = MediaContractLoader.ClientContractIdentity;
    private static readonly string InstallationHash = new('B', 64);
    private static readonly string ClosureHash = new('C', 64);

    private byte[] _fixture = [];
    private ContractMetadata _declared = null!;

    [OneTimeSetUp]
    public void ReadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", FixtureFileName);
        File.Exists(path).Should().BeTrue($"the build must stage '{path}' before a test can publish it");

        _fixture = File.ReadAllBytes(path);

        ContractMetadataReader
            .TryRead(_fixture, MediaContractLoader.ContractAssemblyName, out var metadata, out var failure)
            .Should().BeTrue(failure);

        _declared = metadata!;
    }

    /// <summary>
    /// The fixture is a real contract assembly, and this is what the client reads out of its bytes.
    /// </summary>
    /// <remarks>
    /// Compared against <see cref="System.Reflection.AssemblyName.GetAssemblyName(string)"/>, which reads
    /// the same metadata by a different route and does not load the assembly either. Two independent readers
    /// agreeing is the only claim worth making about a reader.
    /// </remarks>
    [Test]
    public void TheReaderDescribesARealContractAssemblyWithoutLoadingIt()
    {
        var expected = System.Reflection.AssemblyName.GetAssemblyName(
            Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", FixtureFileName));

        using var assertions = new AssertionScope();

        _declared.Identity.Should().Be(expected.FullName);
        _declared.ModuleVersionId.Should().NotBe(Guid.Empty);
        _declared.ContractReference.Should().Be(
            ContractIdentity,
            "a client contract is built against the contract line the client carries, and the reference "
            + "table is where that is stated");

        IsResident(FixtureAssemblyName).Should().BeFalse();
    }

    [Test]
    public async Task AManifestThisClientCannotReadLoadsNothing()
    {
        var report = await LoadAsync(_ => Text("<!doctype html><html>not a manifest</html>"));

        using var assertions = new AssertionScope();

        report.Compatibility.Should().Be(ContractCompatibility.Unreachable);
        report.CanProject.Should().BeFalse();
        report.Packages.Should().BeEmpty();
        IsResident(FixtureAssemblyName).Should().BeFalse();
    }

    /// <summary>
    /// A manifest that is well-formed JSON and does not describe an installation stops the loader before it
    /// asks the host for a single byte.
    /// </summary>
    [Test]
    public async Task AStructurallyInvalidManifestFetchesNothing()
    {
        var byteRequests = 0;

        var report = await LoadAsync(path =>
        {
            if (!path.EndsWith("client-contracts", StringComparison.Ordinal))
            {
                byteRequests++;
                return Bytes(_fixture);
            }

            // Its own closure does not contain it, so a client following that closure would never load it.
            return Json(Manifest(Truthful()) with
            {
                Packages =
                [
                    new ClientContractPackage(
                        Id(Package), "1.0.0", "Fixture", [Truthful()], [], ClosureHash),
                ],
            });
        });

        using var assertions = new AssertionScope();

        report.Compatibility.Should().Be(ContractCompatibility.ManifestInvalid);
        report.CanProject.Should().BeFalse();
        report.Packages.Should().BeEmpty();
        report.Failure.Should().Contain("does not describe an installation");
        byteRequests.Should().Be(0, "nothing may be fetched on the strength of a document this client refused");
        IsResident(FixtureAssemblyName).Should().BeFalse();
    }

    [Test]
    public async Task AContractLineThisClientDoesNotCarryLoadsNothing()
    {
        var manifest = Manifest(Truthful()) with
        {
            ContractIdentity = "Arronix.Abstractions, Version=99.0.0.0, Culture=neutral, PublicKeyToken=null",
        };

        var report = await LoadAsync(Serve(manifest));

        using var assertions = new AssertionScope();

        report.Compatibility.Should().Be(ContractCompatibility.ContractIdentityMismatch);
        report.CanProject.Should().BeFalse();
        report.Packages.Single().Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.NotAttempted);
        IsResident(FixtureAssemblyName).Should().BeFalse();
    }

    /// <summary>
    /// Each published fact is falsified on its own, with every other fact left true.
    /// </summary>
    /// <remarks>
    /// One at a time, with every other fact left true. A wrong content hash is a corrupted download; a right
    /// content hash with a wrong declared build is a different assembly served under a name this
    /// installation already decided the meaning of. Both are refused before the runtime is involved.
    /// </remarks>
    [TestCase("length", ContractLoadOutcome.LengthMismatch)]
    [TestCase("hash", ContractLoadOutcome.ContentHashMismatch)]
    [TestCase("identity", ContractLoadOutcome.IdentityMismatch)]
    [TestCase("module", ContractLoadOutcome.ModuleVersionMismatch)]
    public async Task AFalsifiedFactIsRefusedBeforeTheRuntimeSeesTheBytes(string falsify, ContractLoadOutcome expected)
    {
        var published = falsify switch
        {
            "length" => Truthful() with { Length = _fixture.Length + 1 },
            "hash" => Truthful() with { ContentHash = new string('0', 64) },
            "identity" => Truthful() with { Identity = "Arronix.Media.Movies, Version=9.9.9.9, Culture=neutral, PublicKeyToken=null" },
            "module" => Truthful() with { ModuleVersionId = Guid.Parse("11111111-2222-3333-4444-555555555555") },
            _ => throw new ArgumentOutOfRangeException(nameof(falsify), falsify, "Unknown fact."),
        };

        var report = await LoadAsync(Serve(Manifest(published)));
        var assembly = report.Packages.Single().Assemblies.Single();

        using var assertions = new AssertionScope();

        assembly.Outcome.Should().Be(expected);
        assembly.Failure.Should().NotBeNullOrWhiteSpace();
        report.Compatibility.Should().Be(ContractCompatibility.Refused);
        report.CanProject.Should().BeFalse();
        IsResident(FixtureAssemblyName).Should().BeFalse();
    }

    /// <summary>
    /// A payload that is exactly what it says it is, and is not built against this contract line, is refused.
    /// </summary>
    /// <remarks>
    /// The generator is the honest example: every published fact about it is true, and it references Roslyn
    /// rather than any Arronix contract. Binding it under a media contract's name would give the page a
    /// media contract that shares no types with the one this client compiled against.
    /// </remarks>
    [Test]
    public async Task APayloadThatDoesNotReferenceThisContractLineIsRefused()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", "Arronix.Generators.dll");
        File.Exists(path).Should().BeTrue($"the build must stage '{path}'");

        var content = await File.ReadAllBytesAsync(path);
        ContractMetadataReader
            .TryRead(content, MediaContractLoader.ContractAssemblyName, out var metadata, out _)
            .Should().BeTrue();

        var published = new ClientContractAssembly(
            "Arronix.Generators",
            "Arronix.Generators.dll",
            metadata!.Identity,
            Convert.ToHexString(SHA256.HashData(content)),
            metadata.ModuleVersionId,
            content.Length,
            []);

        var report = await LoadAsync(Serve(Manifest(published), content));
        var assembly = report.Packages.Single().Assemblies.Single();

        using var assertions = new AssertionScope();

        assembly.Outcome.Should().Be(ContractLoadOutcome.ContractReferenceMismatch);
        assembly.ObservedContractReference.Should().BeNull();
        assembly.ObservedIdentity.Should().Be(published.Identity, "every other published fact was true");
        report.CanProject.Should().BeFalse();
        IsResident("Arronix.Generators").Should().BeFalse();
    }

    /// <summary>
    /// A prerequisite that fails verification stops its dependant from loading, even though the dependant's
    /// own bytes were perfect.
    /// </summary>
    /// <remarks>
    /// This is the case a per-assembly loader gets wrong. Verifying and loading one payload at a time admits
    /// the good half of a closure and only then discovers the bad half, and a browser cannot take it back.
    /// </remarks>
    [Test]
    public async Task AFailedPrerequisiteStopsItsDependantFromLoading()
    {
        var generators = Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", "Arronix.Generators.dll");
        var content = await File.ReadAllBytesAsync(generators);
        ContractMetadataReader
            .TryRead(content, MediaContractLoader.ContractAssemblyName, out var metadata, out _)
            .Should().BeTrue();

        // Truthful in every respect except its content hash, so it fails verification.
        var dependency = new ClientContractAssembly(
            "Arronix.Generators",
            "Arronix.Generators.dll",
            metadata!.Identity,
            new string('0', 64),
            metadata.ModuleVersionId,
            content.Length,
            []);

        // The dependant is the real fixture, described truthfully, so it verifies. Whether it became
        // resident is therefore an unambiguous witness of whether the loader loaded anything at all.
        var dependant = Truthful();

        var manifest = new ClientContractManifest(
            ContractIdentity,
            InstallationHash,
            [
                new ClientContractPackage(
                    Id("fixture.dependency"), "1.0.0", "Dependency", [dependency],
                    [Id("fixture.dependency")], ClosureHash),
                new ClientContractPackage(
                    Id("fixture.dependant"), "1.0.0", "Dependant", [dependant],
                    [Id("fixture.dependency"), Id("fixture.dependant")], ClosureHash),
            ],
            []);

        var report = await LoadAsync(path => path.EndsWith("client-contracts", StringComparison.Ordinal)
            ? Json(manifest)
            : Bytes(path.Contains("Arronix.Generators", StringComparison.Ordinal) ? content : _fixture));

        using var assertions = new AssertionScope();

        report.Compatibility.Should().Be(ContractCompatibility.Refused);
        report.CanProject.Should().BeFalse();

        report.Packages.Single(package => package.Id.Value == "fixture.dependency")
            .Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.ContentHashMismatch);

        // The dependant's own bytes verified, and the report says exactly that: Verified, never Loaded.
        // Reporting a payload the runtime never saw as loaded would be the same lie as loading it.
        report.Packages.Single(package => package.Id.Value == "fixture.dependant")
            .Assemblies.Single().Outcome.Should().Be(ContractLoadOutcome.Verified);

        IsResident(FixtureAssemblyName).Should().BeFalse(
            "a verified dependant must not be admitted while its prerequisite is refused, because a browser "
            + "cannot take it back");
    }

    /// <summary>
    /// Nothing verified may be reached while the installation as a whole is refused.
    /// </summary>
    [Test]
    public async Task NothingMayBeProjectedFromARefusedInstallation()
    {
        var report = await LoadAsync(Serve(Manifest(Truthful() with { Length = 1 })));

        using var assertions = new AssertionScope();

        report.CanProject.Should().BeFalse();
        _loader!.Find(FixtureAssemblyName).Should().BeNull();
    }

    private MediaContractLoader? _loader;

    private async Task<ContractLoadReport> LoadAsync(Func<string, HttpResponseMessage> respond)
    {
        using var handler = new StubHandler(respond);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://host.invalid/") };

        _loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));
        return await _loader.LoadAsync();
    }

    /// <summary>The description that is true of the staged fixture in every respect.</summary>
    private ClientContractAssembly Truthful()
        => new(
            FixtureAssemblyName,
            FixtureFileName,
            _declared.Identity,
            Convert.ToHexString(SHA256.HashData(_fixture)),
            _declared.ModuleVersionId,
            _fixture.Length,
            []);

    private static PluginId Id(string value) => PluginId.FromString(value);

    private static ClientContractManifest Manifest(ClientContractAssembly assembly)
        => new(
            ContractIdentity,
            InstallationHash,
            [new ClientContractPackage(Id(Package), "1.0.0", "Fixture", [assembly], [Id(Package)], ClosureHash)],
            []);

    private Func<string, HttpResponseMessage> Serve(ClientContractManifest manifest, byte[]? content = null)
        => path => path.EndsWith("client-contracts", StringComparison.Ordinal)
            ? Json(manifest)
            : Bytes(content ?? _fixture);

    private static HttpResponseMessage Json(ClientContractManifest manifest)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(manifest, ApiJsonOptions.Default),
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage Text(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] content)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    /// <summary>
    /// Whether this process holds an assembly of that simple name.
    /// </summary>
    /// <remarks>
    /// The default context is where the client loads contracts, and it is the only place a refused payload
    /// could have ended up.
    /// </remarks>
    private static bool IsResident(string simpleName)
        => AssemblyLoadContext.Default.Assemblies.Any(assembly =>
            string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

    private sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond(request.RequestUri!.AbsolutePath));
    }

    /// <summary>A script host that is not there, which is the client's "no persistent store" configuration.</summary>
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
