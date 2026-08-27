using System.Linq;
using System.Net;
using System.Net.Http;
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
/// What one page holds as the installation under it changes.
/// </summary>
/// <remarks>
/// <para>
/// Every case here loads a real assembly and then moves the host, which is the only way to reach the states
/// that exist because a browser cannot unload: an assembly the installation stopped naming, one that came
/// back, and one that came back as something else. Each fixture is compiled under its own assembly name,
/// because a name spent in this process is spent for the process.
/// </para>
/// <para>
/// The host answers each manifest read from a mutable field, so a pass reads whatever the installation is
/// at that moment rather than a document written once.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class ContractLifecycleTests
{
    private static readonly PluginId Domain = PluginId.FromString("lifecycle.domain");
    private static readonly PluginId Shared = PluginId.FromString("lifecycle.shared");

    /// <summary>
    /// A withdrawn package is orphaned rather than forgotten, refused rather than served, and reunited with
    /// its exact resident assembly when it returns unchanged.
    /// </summary>
    /// <remarks>
    /// The three failures this guards, in order: a package whose panel simply disappears is
    /// indistinguishable from one this host never had; an orphan that <c>Find</c> still answers for is a
    /// contract the host does not admit being projected with nothing able to detect it; and a removal
    /// elsewhere in an installation must not make the rest of it a fault.
    /// </remarks>
    [Test]
    public async Task AWithdrawnPackageIsOrphanedRefusedAndReunitedWithoutRefetching()
    {
        const string domain = "Fixture.Client.Lifecycle.Domain";
        const string shared = "Fixture.Client.Lifecycle.Shared";

        var domainPayload = Payload(domain);
        var sharedPayload = Payload(shared, declaring: false);

        sharedPayload.Published.Declarations.Should().BeEmpty(
            "an assembly that owns no item declares no client contract, and that is a payload rather than a gap");

        var both = Manifest(
            "1111111111111111111111111111111111111111111111111111111111111111",
            Package(Domain, "Domain", "1.0.0", domainPayload.Published),
            Package(Shared, "Shared", "2.4.0", sharedPayload.Published));

        var host = new Installation(both, domainPayload, sharedPayload);
        using var http = host.Connect();
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        (await loader.LoadAsync()).CanProject.Should().BeTrue("both packages verify and load");

        var declarations = loader.ContractsOf(domain);
        declarations.Should().ContainSingle();

        // The host drops the domain package entirely. Nothing about the rest of the installation changed.
        host.Publishes(Manifest(
            "2222222222222222222222222222222222222222222222222222222222222222",
            Package(Shared, "Shared", "2.4.0", sharedPayload.Published)));

        var withdrawn = await loader.LoadAsync();

        using (new AssertionScope())
        {
            withdrawn.Compatibility.Should().Be(
                ContractCompatibility.Compatible,
                "everything this installation still requires is resident; a removal elsewhere is not a fault");
            withdrawn.CanProject.Should().BeTrue();

            withdrawn.Packages.Should().ContainSingle().Which.Id.Should().Be(Shared);

            var orphan = withdrawn.Orphaned.Should().ContainSingle().Subject;
            orphan.Verified.AssemblyName.Should().Be(domain);
            orphan.PackageId.Should().Be(Domain);
            orphan.PackageName.Should().Be("Domain", "the owner is retained from when it was last admitted");
            orphan.PackageVersion.Should().Be("1.0.0");
            orphan.Owner.Should().Be(
                OrphanedContractOwner.Unpublished,
                "this manifest names that identifier neither among its offers nor among its refusals");
            orphan.Refusal.Should().BeNull("the host stated no refusal to carry");

            loader.Find(domain).Should().BeNull("the installation this page last read does not name it");
            loader.ContractsOf(domain).Should().BeEmpty("declarations are the other door into a contract");

            loader.Find(shared).Should().NotBeNull("this one is still named, and still current");
            host.ByteRequests.Should().Be(2, "a withdrawal fetches nothing");
        }

        // It comes back, byte for byte what this page already verified.
        host.Publishes(both);
        var before = host.ByteRequests;
        var reunited = await loader.LoadAsync();

        using var assertions = new AssertionScope();

        reunited.Compatibility.Should().Be(ContractCompatibility.Compatible);
        reunited.Orphaned.Should().BeEmpty();
        host.ByteRequests.Should().Be(before, "the exact resident assembly is reused, not fetched again");

        var entry = reunited.Packages.Single(package => package.Id == Domain).Assemblies.Single();
        entry.Outcome.Should().Be(ContractLoadOutcome.AlreadyLoaded);
        entry.Source.Should().Be(ContractByteSource.Resident);

        loader.Find(domain).Should().NotBeNull();
        loader.ContractsOf(domain).Single().Should().BeSameAs(
            declarations[0],
            "reunion hands back the instances the post-load proof accepted, never a second resolution");

        reunited.Packages.Single(package => package.Id == Shared).Assemblies.Single()
            .Outcome.Should().Be(ContractLoadOutcome.AlreadyLoaded, "declaring nothing stays valid throughout");
    }

    /// <summary>
    /// A withdrawn package that returns under a different content hash is terminal, exactly as one that
    /// never left would be.
    /// </summary>
    /// <remarks>
    /// A stale resident assembly is a stale resident assembly whichever path produced it. Treating
    /// "withdraw, then republish something else" more leniently than "republish something else" would let
    /// one page hold an assembly the host has replaced and go on projecting it.
    /// </remarks>
    [Test]
    public async Task AWithdrawnPackageReturningUnderADifferentHashIsTerminal()
    {
        const string name = "Fixture.Client.Lifecycle.Replaced";
        var payload = Payload(name);
        var offered = Manifest(
            "3333333333333333333333333333333333333333333333333333333333333333",
            Package(Domain, "Domain", "1.0.0", payload.Published));

        var host = new Installation(offered, payload);
        using var http = host.Connect();
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        (await loader.LoadAsync()).CanProject.Should().BeTrue();

        host.Publishes(Manifest("4444444444444444444444444444444444444444444444444444444444444444"));
        (await loader.LoadAsync()).Orphaned.Should().ContainSingle("the package left this installation");

        // It returns as a different build under the same simple name.
        host.Publishes(Manifest(
            "5555555555555555555555555555555555555555555555555555555555555555",
            Package(
                Domain,
                "Domain",
                "1.1.0",
                payload.Published with
                {
                    ContentHash = new string('D', 64),
                    ModuleVersionId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
                })));

        var replaced = await loader.LoadAsync();

        using (new AssertionScope())
        {
            replaced.Packages.Single().Assemblies.Single().Outcome.Should().Be(
                ContractLoadOutcome.NameAlreadyResident);
            replaced.Compatibility.Should().Be(ContractCompatibility.Terminal);
            replaced.CanProject.Should().BeFalse();
            replaced.Failure.Should().Contain("Reload");
            loader.Find(name).Should().BeNull();
        }

        // And it stays terminal when the host goes back to what this page holds.
        host.Publishes(offered);
        (await loader.LoadAsync()).Compatibility.Should().Be(
            ContractCompatibility.Terminal,
            "a page that can never satisfy an installation does not recover because the host relented");
    }

    /// <summary>An orphan is labeled from what the current manifest says about its package, and nothing else.</summary>
    /// <remarks>
    /// The host keeps no history, so a client cannot say why a package left. It can say what that
    /// identifier means to this host now: withheld with the host's own reason, still offered without this
    /// file, or not mentioned at all.
    /// </remarks>
    [Test]
    public async Task AnOrphanIsLabeledFromWhatTheHostSaysAboutItsPackageNow()
    {
        const string name = "Fixture.Client.Lifecycle.Withheld";
        var payload = Payload(name);
        var successor = Payload("Fixture.Client.Lifecycle.Successor");

        var host = new Installation(
            Manifest(
                "6666666666666666666666666666666666666666666666666666666666666666",
                Package(Domain, "Domain", "1.0.0", payload.Published)),
            payload,
            successor);

        using var http = host.Connect();
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        (await loader.LoadAsync()).CanProject.Should().BeTrue();

        var refusal = new ClientContractRefusal(
            Domain,
            "Its client facet binds an assembly this installation no longer admits.",
            ["Arronix.Format.Video"],
            [],
            []);

        host.Publishes(Manifest("7777777777777777777777777777777777777777777777777777777777777777") with
        {
            Refused = [refusal],
        });

        var withheld = (await loader.LoadAsync()).Orphaned.Should().ContainSingle().Subject;

        using (new AssertionScope())
        {
            withheld.Owner.Should().Be(OrphanedContractOwner.Withheld);
            withheld.Refusal.Should().NotBeNull();
            withheld.Refusal!.Reason.Should().Be(
                refusal.Reason,
                "the host's live reason travels rather than a client guess");
            withheld.Refusal.MissingAssemblies.Should().Equal(refusal.MissingAssemblies);
        }

        // Offered again, with a facet that carries a successor and no longer carries this file. A published
        // package always offers something, so "offered without this assembly" is what a dropped file is.
        host.Publishes(Manifest(
            "8888888888888888888888888888888888888888888888888888888888888888",
            Package(Domain, "Domain", "1.2.0", successor.Published)));

        var dropped = (await loader.LoadAsync()).Orphaned.Should().ContainSingle().Subject;

        using var assertions = new AssertionScope();

        dropped.Owner.Should().Be(OrphanedContractOwner.Offered);
        dropped.Refusal.Should().BeNull();
        dropped.PackageVersion.Should().Be(
            "1.0.0",
            "the retained facts are the ones under which this page was admitted, not the ones it never loaded");
    }

    /// <summary>
    /// An unchanged installation hash costs one manifest read, and is a reason to look rather than a
    /// statement that nothing moved.
    /// </summary>
    /// <remarks>
    /// The zero-fetch half is a cost property the ordinary reuse path already has; the second half is what
    /// the early-out itself must not break. The published closure hash covers each assembly's identity and
    /// content hash and not what it declares, so an installation hash can stand still while the host
    /// restates a payload's contracts — and this page still has to refuse it.
    /// </remarks>
    [Test]
    public async Task AnUnchangedInstallationHashSkipsTheFetchAndStillChecksWhatWasPublished()
    {
        const string name = "Fixture.Client.Lifecycle.Unchanged";
        var payload = Payload(name);
        const string hash = "9999999999999999999999999999999999999999999999999999999999999999";
        var offered = Manifest(hash, Package(Domain, "Domain", "1.0.0", payload.Published));

        var host = new Installation(offered, payload);
        using var http = host.Connect();
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        (await loader.LoadAsync()).CanProject.Should().BeTrue();
        host.ByteRequests.Should().Be(1, "the first pass fetched the payload");

        var again = await loader.LoadAsync();

        using (new AssertionScope())
        {
            again.Compatibility.Should().Be(ContractCompatibility.Compatible);
            again.InstallationHash.Should().Be(hash);
            host.ByteRequests.Should().Be(1, "nothing a client would load changed");

            var entry = again.Packages.Single().Assemblies.Single();
            entry.Outcome.Should().Be(ContractLoadOutcome.AlreadyLoaded);
            entry.Source.Should().Be(ContractByteSource.Resident);
        }

        // The same hash over the same bytes, and a different statement about what they declare.
        host.Publishes(Manifest(
            hash,
            Package(
                Domain,
                "Domain",
                "1.0.0",
                payload.Published with
                {
                    Declarations =
                    [
                        new ClientContractDeclaration(
                            "Fixture.Client.Invented",
                            "Fixture.Client.Entity",
                            new string('E', 64),
                            new string('F', 64)),
                    ],
                })));

        var restated = await loader.LoadAsync();

        using var assertions = new AssertionScope();

        restated.Packages.Single().Assemblies.Single().Outcome.Should().Be(
            ContractLoadOutcome.NameAlreadyResident,
            "an equal installation hash permits the question and does not answer it");
        restated.Compatibility.Should().Be(ContractCompatibility.Terminal);
        loader.Find(name).Should().BeNull();
        host.ByteRequests.Should().Be(1, "no pass here needed a byte");
    }

    /// <summary>
    /// A caller that abandons a load leaves this page describing the installation it last actually read.
    /// </summary>
    /// <remarks>
    /// Applying the bookkeeping before the cancellable fetches would pair the previous report with the next
    /// installation's residency: a page still claiming to be compatible while <c>Find</c> had already
    /// changed its mind about which names it serves.
    /// </remarks>
    [Test]
    public async Task ACanceledLoadLeavesResidencyAndTheReportDescribingTheSameInstallation()
    {
        const string held = "Fixture.Client.Lifecycle.Held";
        var payload = Payload(held);
        var arriving = Payload("Fixture.Client.Lifecycle.Arriving");

        var host = new Installation(
            Manifest(
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                Package(Domain, "Domain", "1.0.0", payload.Published)),
            payload,
            arriving);

        using var http = host.Connect();
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        var compatible = await loader.LoadAsync();
        compatible.CanProject.Should().BeTrue();

        // The host drops the package this page holds and offers a different one, so this pass both orphans
        // a name and has bytes to fetch. The caller withdraws while it is fetching them.
        host.Publishes(Manifest(
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            Package(Shared, "Shared", "1.0.0", arriving.Published)));

        using var abandoned = new CancellationTokenSource();
        host.BeforeBytes = _ =>
        {
            abandoned.Cancel();
            throw new OperationCanceledException(abandoned.Token);
        };

        await FluentActions
            .Awaiting(() => loader.LoadAsync(abandoned.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        host.BeforeBytes = null;

        using (new AssertionScope())
        {
            loader.Report.Should().BeSameAs(compatible, "nothing was read to replace it with");
            loader.Report!.Orphaned.Should().BeEmpty();
            loader.Find(held).Should().NotBeNull("the installation this page last read still names it");
            loader.ContractsOf(held).Should().NotBeEmpty();
        }

        // The same manifest, read through. Now the bookkeeping moves, together with the report.
        var withdrawn = await loader.LoadAsync();

        using var assertions = new AssertionScope();

        withdrawn.Orphaned.Should().ContainSingle().Which.Verified.AssemblyName.Should().Be(held);
        loader.Find(held).Should().BeNull();
        loader.ContractsOf(held).Should().BeEmpty();
    }

    private static ClientContractManifest Manifest(string installationHash, params ClientContractPackage[] packages)
        => new(MediaContractLoader.ClientContractIdentity, installationHash, packages, []);

    private static ClientContractPackage Package(
        PluginId id,
        string name,
        string version,
        params ClientContractAssembly[] assemblies)
        => new(id, version, name, assemblies, [id], new string('C', 64));

    /// <summary>Compiles one fixture and describes it exactly, so only lifecycle questions can fail.</summary>
    private static Compiled Payload(string assemblyName, bool declaring = true)
    {
        var image = CompiledContract.Image(assemblyName, Misbehaviour.None, declaring);

        ContractMetadataReader
            .TryRead(image, MediaContractLoader.ContractAssemblyName, out var metadata, out var unreadable)
            .Should().BeTrue(unreadable);

        return new Compiled(
            image,
            new ClientContractAssembly(
                assemblyName,
                assemblyName + ".dll",
                metadata!.Identity,
                Convert.ToHexString(SHA256.HashData(image)),
                metadata.ModuleVersionId,
                image.Length,
                metadata.Declarations));
    }

    private sealed record Compiled(byte[] Image, ClientContractAssembly Published);

    /// <summary>A host whose installation moves between one client's reads.</summary>
    private sealed class Installation(ClientContractManifest manifest, params Compiled[] payloads)
    {
        private ClientContractManifest _manifest = manifest;

        /// <summary>Gets how many times a client has asked this host for bytes.</summary>
        public int ByteRequests { get; private set; }

        /// <summary>Gets or sets what happens before this host answers for bytes.</summary>
        public Action<string>? BeforeBytes { get; set; }

        public void Publishes(ClientContractManifest next) => _manifest = next;

        public HttpClient Connect()
            => new(new StubHandler(Answer)) { BaseAddress = new Uri("https://host.invalid/") };

        private HttpResponseMessage Answer(string path)
        {
            if (path.EndsWith("client-contracts", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(_manifest, ApiJsonOptions.Default),
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            ByteRequests++;
            BeforeBytes?.Invoke(path);

            var payload = payloads.FirstOrDefault(candidate =>
                path.Contains(candidate.Published.AssemblyName, StringComparison.Ordinal));

            return payload is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload.Image) };
        }
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
            => throw new NotSupportedException("This test process has no browser.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new NotSupportedException("This test process has no browser.");
    }
}
