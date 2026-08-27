using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.JSInterop;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// One serialized entity, read through an admitted contract and proved before anything renders it.
/// </summary>
/// <remarks>
/// The shipped movies contract is admitted through the ordinary loader and handed the fixture the browser
/// proof reads, so the typed half is exercised against the real generated reader and the real generated
/// projection rather than against a stand-in. The misbehaviour cases are compiled fixtures, because a
/// declaration that hands back the wrong thing is the case no real contract can be made to produce.
/// </remarks>
[TestFixture]
internal sealed class ContractPayloadTests
{
    private const string MoviesAssembly = "Arronix.Media.Movies";
    private const string PayloadPath = "fixtures/g07/movie.json";

    private static byte[] Movies() => File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "ClientContractFixtures", MoviesAssembly + ".dll"));

    /// <summary>The published fixture, read from the repository the same way the browser reads it.</summary>
    private static byte[] Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is above the test binary");

        return File.ReadAllBytes(Path.Combine(directory!.FullName, "eng", "proofs", "fixtures", "g07", "movie.json"));
    }

    [Test]
    public async Task TheShippedMoviesContractReadsAndProjectsThePublishedFixture()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());

        var report = await page.Payloads.ProjectAsync(PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.Projected, report.Failure);
        report.EntityTypeName.Should().Be("Arronix.Media.Movies.Movie");
        report.PayloadLength.Should().Be(Fixture().Length);
        report.Projection.Should().NotBeNull();

        // The projection is proved against the contract's own schema, object for object and in its own
        // order — the four ways it can stop being that are refused in ProjectionAuditTests. What is handed
        // back is the copy the proof made while it was proving, so the entity type is the contract's own
        // and every collection under it is this client's.
        var contract = page.Contracts.Admitted().Single().Contract;
        report.Projection!.EntityType.Should().BeSameAs(contract.EntityType);
        report.Projection.Fields.Select(field => field.Descriptor.FieldId)
            .Should().Equal(contract.Schema.Admitted.Select(descriptor => descriptor.FieldId));
        report.Projection.Fields
            .Any(field => contract.Schema.Admitted.Any(declared => ReferenceEquals(declared, field.Descriptor)))
            .Should().BeFalse("what is rendered is the copy, not the contract's own object");
    }

    /// <remarks>
    /// The five the browser proof reads back, each non-empty, each still typed. Artwork is the one that has
    /// to survive as a whole image rather than as an address.
    /// </remarks>
    [Test]
    public async Task TheProjectedMovieCarriesArtworkRatingsLifecycleStatusAndCollections()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());

        var report = await page.Payloads.ProjectAsync(PayloadPath);
        report.Outcome.Should().Be(ContractPayloadOutcome.Projected, report.Failure);

        FieldValue Value(string fieldId) => report.Projection!.Fields
            .Single(field => field.Descriptor.FieldId == fieldId).Value;

        using var _ = new AssertionScope();

        var artwork = Value("artwork");
        artwork.Kind.Should().Be(FieldValueKind.Artwork);
        artwork.IsAbsent.Should().BeFalse();
        artwork.Items.Should().HaveCount(2);

        var poster = artwork.Items![0].Image;
        poster.Should().NotBeNull("artwork is a whole image, never a URL string");
        poster!.Role.Should().Be("poster");
        poster.Width.Should().Be(8);
        poster.Height.Should().Be(12);
        poster.Address.Scheme.Should().Be("data");
        artwork.Items[1].Image!.Role.Should().Be("fanart");

        Value("ratings").Items.Should().HaveCount(2);
        Value("lifecycle").Kind.Should().Be(FieldValueKind.Composite);
        Value("lifecycle").IsAbsent.Should().BeFalse();
        Value("status").Text.Should().Be("released");
        Value("collections").Items.Should().HaveCount(1);
    }

    /// <remarks>
    /// The rendered report carries live CLR types and tagged values, which no serializer writes. The proof
    /// a harness reads is a separate shape of strings and numbers, and this is the case that keeps the page
    /// from throwing where it renders it.
    /// </remarks>
    [Test]
    public async Task TheProofSerializesAndCarriesTheRenderedFieldEvidence()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());

        var report = await page.Payloads.ProjectAsync(PayloadPath);
        report.Outcome.Should().Be(ContractPayloadOutcome.Projected, report.Failure);

        var proof = ContractPayloadProof.Of(report);
        var serialize = () => JsonSerializer.Serialize(proof, ApiJsonOptions.Default);

        using var _ = new AssertionScope();

        serialize.Should().NotThrow("a proof carries no CLR type");

        var document = serialize();
        document.Should().Contain("\"artwork\"").And.Contain("\"ratings\"").And.Contain("\"lifecycle\"");

        var artwork = proof.Fields.Single(field => field.FieldId == "artwork");
        artwork.Absent.Should().BeFalse();
        artwork.ItemCount.Should().Be(2);
        artwork.Images.Should().HaveCount(2);
        artwork.Images[0].Role.Should().Be("poster");
        artwork.Images[0].Width.Should().Be(8);
        artwork.Images[0].Height.Should().Be(12);
        artwork.Images[0].Address.Should().StartWith("data:image/png;base64,");

        // A nested entity's artwork survives with its role too.
        proof.Fields.Single(field => field.FieldId == "collections").Images
            .Should().ContainSingle().Which.Role.Should().Be("poster");

        proof.Fields.Single(field => field.FieldId == "status").Text.Should().Be("Released");
        proof.Fields.Single(field => field.FieldId == "lifecycle").Absent.Should().BeFalse();
    }

    /// <remarks>
    /// Two assemblies may declare an entry point of the same name, so a selection keyed on one half alone
    /// would hand a caller the other assembly's contract.
    /// </remarks>
    [Test]
    public async Task AnOfferIsNamedByItsAssemblyAndItsEntryPointTogether()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());

        var offer = page.Payloads.Offers().Single();

        using var _ = new AssertionScope();

        offer.Key.Should().Be(offer.AssemblyName + "|" + offer.EntryPointType);
        offer.Key.Should().NotBe(offer.EntryPointType);
    }

    [Test]
    public async Task APayloadThisContractCannotReadIsADeserializationFailure()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), "{ \"title\": "u8.ToArray());

        var report = await page.Payloads.ProjectAsync(PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.DeserializationFailed);
        report.Failure.Should().Contain("MovieClientContractEntryPointAttribute");
        report.Projection.Should().BeNull();
    }

    [Test]
    public async Task APayloadThisHostDoesNotServeIsUnavailable()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());
        page.PayloadStatus = HttpStatusCode.NotFound;

        var report = await page.Payloads.ProjectAsync(PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.Unavailable);
        report.Projection.Should().BeNull();
    }

    [Test]
    public async Task AnAddressOutsideThisHostIsRefusedWithoutBeingFetched()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());

        var report = await page.Payloads.ProjectAsync("https://evil.test/movie.json");

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.AddressUnsafe);
        page.PayloadRequests.Should().Be(0, "nothing was fetched");
    }

    /// <remarks>
    /// Both halves: a declared length past the limit is refused before a byte is read, and a response that
    /// declares nothing is refused at the first byte past it.
    /// </remarks>
    [Test]
    public async Task APayloadLargerThanOneEntityIsRefusedRatherThanHeld()
    {
        var oversize = new byte[ClientContractLimits.MaxPayloadBytes + 16];
        Array.Fill(oversize, (byte)' ');

        await using var declared = await Page.OpenAsync(MoviesAssembly, Movies(), oversize);
        var first = await declared.Payloads.ProjectAsync(PayloadPath);

        await using var undeclared = await Page.OpenAsync(MoviesAssembly, Movies(), oversize);
        undeclared.HidePayloadLength = true;
        var second = await undeclared.Payloads.ProjectAsync(PayloadPath);

        using var _ = new AssertionScope();

        first.Outcome.Should().Be(ContractPayloadOutcome.Unavailable);
        first.Failure.Should().Contain("declares a " + oversize.Length);
        second.Outcome.Should().Be(ContractPayloadOutcome.Unavailable);
        second.Failure.Should().Contain("longer than the " + ClientContractLimits.MaxPayloadBytes);
    }

    [Test]
    public async Task ASignaledCallerTokenPropagates()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());
        using var abandoned = new CancellationTokenSource();
        await abandoned.CancelAsync();

        var attempt = async () => await page.Payloads.ProjectAsync(PayloadPath, abandoned.Token);

        await attempt.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <remarks>
    /// Cancellation belongs to the caller. Something else canceling something of its own is an ordinary
    /// failure, and reporting it as "the caller abandoned this" would lose the difference.
    /// </remarks>
    [Test]
    public async Task ACancellationNobodyAskedForIsAnOrdinaryContainedFailure()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());
        page.PayloadFailure = () => throw new OperationCanceledException("a transport canceled itself");

        var report = await page.Payloads.ProjectAsync(PayloadPath, CancellationToken.None);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.Unavailable);
        report.Failure.Should().Contain("a transport canceled itself");
    }

    /// <summary>
    /// A declaration that hands back something other than what it declared, at each of the four points it
    /// can.
    /// </summary>
    [TestCase(Misbehaviour.PayloadReadable, ContractPayloadOutcome.Projected)]
    [TestCase(Misbehaviour.PayloadForeignType, ContractPayloadOutcome.DeserializedTypeMismatch)]
    [TestCase(Misbehaviour.PayloadNullEntity, ContractPayloadOutcome.DeserializationFailed)]
    [TestCase(Misbehaviour.PayloadThrowingProject, ContractPayloadOutcome.ProjectionFailed)]
    [TestCase(Misbehaviour.PayloadCancelingProject, ContractPayloadOutcome.ProjectionFailed)]
    [TestCase(Misbehaviour.PayloadForeignProjectedType, ContractPayloadOutcome.ProjectedTypeMismatch)]
    public async Task ADeclarationIsHeldToWhatItDeclared(
        Misbehaviour misbehaviour,
        ContractPayloadOutcome expected)
    {
        var name = "Fixture.Payload." + misbehaviour;
        await using var page = await Page.OpenAsync(
            name,
            CompiledContract.Build(name, misbehaviour).Payload,
            "{}"u8.ToArray());

        var report = await page.Payloads.ProjectAsync(PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(expected, report.Failure);
        (report.Projection is null).Should().Be(expected != ContractPayloadOutcome.Projected);
    }

    /// <remarks>
    /// The installation was admitted correctly and one document was not readable. Downgrading the admission
    /// report because of that would lose the one fact that is still true.
    /// </remarks>
    [Test]
    public async Task APayloadFailureDoesNotRewriteTheInstallationReport()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), "not json"u8.ToArray());
        var before = page.Contracts.Report;

        var report = await page.Payloads.ProjectAsync(PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.DeserializationFailed);
        page.Contracts.Report.Should().BeSameAs(before);
        page.Contracts.Report!.Compatibility.Should().Be(ContractCompatibility.Compatible);
        page.Contracts.Report.CanProject.Should().BeTrue();
        page.Payloads.Offers().Should().ContainSingle();
    }

    [Test]
    public async Task NothingIsProjectedThroughAContractThisPageDoesNotAdmit()
    {
        await using var one = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());
        await using var two = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());

        // Same assembly, same declaration, a different page's installation.
        var foreign = two.Payloads.Offers().Single();
        var report = await one.Payloads.ProjectAsync(foreign, PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.NoAdmittedContract);
        one.PayloadRequests.Should().Be(0, "nothing was fetched through a contract this page does not hold");
    }

    /// <remarks>
    /// A withdrawal can land while a payload is in flight. The contract is asked for again after the fetch,
    /// so bytes that arrived for a contract this page has stopped admitting are not read through it.
    /// </remarks>
    [Test]
    public async Task AContractWithdrawnWhileThePayloadIsInFlightReadsNothing()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());
        var offer = page.Payloads.Offers().Single();

        page.BeforePayload = async () =>
        {
            page.Publish = false;
            await page.Contracts.LoadAsync();
        };

        var report = await page.Payloads.ProjectAsync(offer, PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.NoAdmittedContract);
        report.Failure.Should().Contain("were not read through it");
        page.PayloadRequests.Should().Be(1, "the fetch had already happened");
    }

    /// <remarks>
    /// Values already on screen are the other half of the same rule: a page rendering a projection from a
    /// contract the host has stopped publishing is describing an installation that no longer exists.
    /// </remarks>
    [Test]
    public async Task AProjectionIsInvalidatedOnceItsContractIsNoLongerAdmitted()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());
        var offer = page.Payloads.Offers().Single();
        var report = await page.Payloads.ProjectAsync(offer, PayloadPath);
        report.Outcome.Should().Be(ContractPayloadOutcome.Projected, report.Failure);

        using var _ = new AssertionScope();

        page.Payloads.Revalidate(report, offer).Should().BeSameAs(report, "the contract is still admitted");

        page.Publish = false;
        await page.Contracts.LoadAsync();

        var stale = page.Payloads.Revalidate(report, offer);
        stale!.Outcome.Should().Be(ContractPayloadOutcome.NoAdmittedContract);
        stale.Projection.Should().BeNull();

        // A diagnostic says what was true when it was written and is left alone.
        var diagnostic = report with { Outcome = ContractPayloadOutcome.Unavailable, Projection = null };
        page.Payloads.Revalidate(diagnostic, offer).Should().BeSameAs(diagnostic);
    }

    [Test]
    public async Task APageHoldingNoContractProjectsNothing()
    {
        await using var page = await Page.OpenAsync(MoviesAssembly, Movies(), Fixture());
        page.Publish = false;
        await page.Contracts.LoadAsync();

        var report = await page.Payloads.ProjectAsync(PayloadPath);

        using var _ = new AssertionScope();

        report.Outcome.Should().Be(ContractPayloadOutcome.NoAdmittedContract);
        page.Payloads.Offers().Should().BeEmpty();
        page.PayloadRequests.Should().Be(0);
    }

    /// <summary>
    /// One browser page: a host publishing one contract assembly and one payload, a loader that admitted it
    /// into a context this fixture can unload, and the payload reader over both.
    /// </summary>
    private sealed class Page : IAsyncDisposable
    {
        private readonly AssemblyLoadContext _context;

        private Page(AssemblyLoadContext context, MediaContractLoader contracts, ContractPayloadLoader payloads)
        {
            _context = context;
            Contracts = contracts;
            Payloads = payloads;
        }

        internal MediaContractLoader Contracts { get; }

        internal ContractPayloadLoader Payloads { get; }

        /// <summary>Whether the host still publishes the facet.</summary>
        internal bool Publish { get; set; } = true;

        /// <summary>What the payload route answers with.</summary>
        internal HttpStatusCode PayloadStatus { get; set; } = HttpStatusCode.OK;

        /// <summary>Whether the payload response declares its length.</summary>
        internal bool HidePayloadLength { get; set; }

        /// <summary>How the payload route fails instead of answering.</summary>
        internal Func<HttpResponseMessage>? PayloadFailure { get; set; }

        /// <summary>What happens while the payload request is in flight.</summary>
        internal Func<Task>? BeforePayload { get; set; }

        /// <summary>How many times the payload was fetched.</summary>
        internal int PayloadRequests { get; private set; }

        internal static async Task<Page> OpenAsync(string assemblyName, byte[] image, byte[] payload)
        {
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

            var context = new AssemblyLoadContext(assemblyName + ".payload", isCollectible: true);
            Page? page = null;

            var handler = new StubHandler(async request =>
            {
                var path = request.RequestUri!.AbsolutePath;

                if (path.EndsWith("client-contracts", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(Manifest(published, page?.Publish ?? true), ApiJsonOptions.Default),
                            Encoding.UTF8,
                            "application/json"),
                    };
                }

                if (path.Contains("client-contracts/", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) };
                }

                page!.PayloadRequests++;

                if (page.BeforePayload is { } during)
                {
                    await during();
                }

                if (page.PayloadFailure is { } failure)
                {
                    return failure();
                }

                var response = new HttpResponseMessage(page.PayloadStatus)
                {
                    Content = page.HidePayloadLength
                        ? new StreamContent(new UndeclaredStream(payload))
                        : new ByteArrayContent(payload),
                };

                return response;
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://host.invalid/") };
            var contracts = new MediaContractLoader(
                http,
                new ContractStore(new RefusingJsRuntime()),
                bytes => context.LoadFromStream(new MemoryStream(bytes, writable: false)));

            page = new Page(context, contracts, new ContractPayloadLoader(http, contracts));

            var report = await contracts.LoadAsync();
            report.CanProject.Should().BeTrue(report.Failure);

            return page;
        }

        public ValueTask DisposeAsync()
        {
            _context.Unload();
            return ValueTask.CompletedTask;
        }

        private static ClientContractManifest Manifest(ClientContractAssembly published, bool publish)
            => new(
                MediaContractLoader.ClientContractIdentity,
                new string('B', 64),
                publish
                    ?
                    [
                        new ClientContractPackage(
                            PluginId.FromString("proof.fixture"),
                            "1.0.0",
                            "Proof fixture",
                            [published],
                            [PluginId.FromString("proof.fixture")],
                            new string('C', 64)),
                    ]
                    : [],
                []);
    }

    /// <summary>A body whose length nothing declares, so only the read can bound it.</summary>
    private sealed class UndeclaredStream(byte[] content) : MemoryStream(content, writable: false)
    {
        public override long Length => throw new NotSupportedException();

        public override bool CanSeek => false;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => answer(request);
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
