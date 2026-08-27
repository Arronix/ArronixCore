using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Telemetry;
using Arronix.Api.Hubs;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Api.Tests.Vertical;

/// <summary>
/// The server started through its own entry point, with Movies and the video format installed beside it.
/// </summary>
/// <remarks>
/// Ordinary composition: the fixture sets install, state and library paths and registers one extra
/// telemetry sink, and substitutes no service. An extension with an entry assembly activates only if the
/// platform supplies the cache, telemetry, event, host-runtime and operating-system contracts.
/// </remarks>
[TestFixture]
internal sealed class MoviesActivationTests
{
    private const string Movies = "movies";
    private const string Video = "arronix.format.video";

    private ArronixServer _server = null!;

    [OneTimeSetUp]
    public void StartServer() => _server = new ArronixServer();

    [OneTimeTearDown]
    public void StopServer() => _server.Dispose();

    [Test]
    public void BothInstalledPackagesAreActive()
    {
        var installed = _server.Services.GetRequiredService<PluginRuntimeRegistry>().Snapshot();

        using var assertions = new AssertionScope();
        installed.Select(view => view.Id).Should().BeEquivalentTo([Video, Movies]);
        installed.Should().OnlyContain(view => view.State == nameof(PluginState.Active));
    }

    [Test]
    public void ThePlatformSuppliesEveryServiceTheExtensionNeeded()
    {
        var platform = _server.Services.GetRequiredService<PluginPlatformServices>();

        using var assertions = new AssertionScope();
        platform.MissingRequiredServices().Should().BeEmpty();
        _server.Services.GetService<ICacheProvider>().Should().NotBeNull();
        _server.Services.GetService<ITelemetryEmitter>().Should().NotBeNull();
        _server.Services.GetService<IEventPublisher>().Should().NotBeNull();
        _server.Services.GetService<IHostRuntimeInfo>().Should().NotBeNull();
        _server.Services.GetService<IOperatingSystemInfo>().Should().NotBeNull();
    }

    [Test]
    public async Task TheKindsRouteServesTheKindTheExtensionDeclaredAsync()
    {
        using var kinds = await ReadAsync("/api/v1/kinds").ConfigureAwait(false);

        var declared = kinds.RootElement.EnumerateArray()
            .Select(kind => kind.GetProperty("kind").GetString())
            .ToArray();

        declared.Should().Equal(["movies"], "nothing in this server names a movie kind; the extension does");
    }

    [Test]
    public async Task ThePluginsRouteReportsBothPackagesActiveAsync()
    {
        using var plugins = await ReadAsync("/api/v1/plugins").ConfigureAwait(false);

        var reported = plugins.RootElement.EnumerateArray()
            .Select(plugin => (Id: plugin.GetProperty("id").GetString(), State: plugin.GetProperty("state").GetString()))
            .ToArray();

        using var assertions = new AssertionScope();
        reported.Should().BeEquivalentTo(
        [
            (Id: (string?)Video, State: (string?)nameof(PluginState.Active)),
            (Id: (string?)Movies, State: (string?)nameof(PluginState.Active)),
        ]);
    }

    [Test]
    public async Task TheClientContractManifestPublishesTheExactClosureAsync()
    {
        using var manifest = await ReadAsync("/api/v1/client-contracts").ConfigureAwait(false);

        var packages = manifest.RootElement.GetProperty("packages").EnumerateArray().ToArray();

        using var assertions = new AssertionScope();

        packages.Select(package => package.GetProperty("id").GetString()).Should().Equal(
            [Video, Movies],
            "the manifest lists packages by identifier");

        // The load order is each package's own closure: a client loads what a package binds before it.
        Closure(packages[0]).Should().Equal([Video]);
        Closure(packages[1]).Should().Equal([Video, Movies]);

        Files(packages[0]).Should().Equal(["Arronix.Format.Video.dll"]);
        Files(packages[1]).Should().Equal(["Arronix.Media.Movies.dll"]);

        packages.SelectMany(Files).Should().NotContain(
            ["Arronix.Plugin.Movies.dll", "Arronix.Abstractions.dll", "Arronix.Api.dll"],
            "an entry assembly and the server's own assemblies are not a client's to load");

        manifest.RootElement.GetProperty("refused").EnumerateArray().Should().BeEmpty();
    }

    /// <summary>
    /// A real server publishes what each admitted assembly declares about the contracts it holds.
    /// </summary>
    /// <remarks>
    /// Read from the staged bytes at admission, without loading the assembly or calling into the package, so
    /// what reaches a browser is what the file says rather than what running it would report. The video
    /// format declares nothing and that is a fact rather than an omission: it owns no item.
    /// </remarks>
    [Test]
    public async Task TheClientContractManifestPublishesWhatEachAssemblyDeclaresAsync()
    {
        using var manifest = await ReadAsync("/api/v1/client-contracts").ConfigureAwait(false);

        var packages = manifest.RootElement.GetProperty("packages").EnumerateArray().ToArray();

        using var assertions = new AssertionScope();

        Declarations(packages[0]).Should().BeEmpty(
            "the video format owns no item, so it declares no client contract");

        var declarations = Declarations(packages[1]);
        declarations.Should().HaveCount(1);

        var declaration = declarations[0];
        declaration.GetProperty("entityTypeName").GetString().Should().Be("Arronix.Media.Movies.Movie");
        declaration.GetProperty("entryPointType").GetString().Should()
            .StartWith("Arronix.Media.Movies.", "the entry point is the contract assembly's own type");

        foreach (var name in new[] { "generatedMetadataHash", "projectionSchemaHash" })
        {
            declaration.GetProperty(name).GetString().Should().MatchRegex(
                "^[0-9A-F]{64}$",
                $"'{name}' is a SHA-256 a browser recomputes and compares");
        }
    }

    [Test]
    public async Task EveryPublishedAddressServesTheBytesItNamesAsync()
    {
        using var client = _server.CreateClient();
        using var manifest = await ReadAsync("/api/v1/client-contracts").ConfigureAwait(false);

        using var assertions = new AssertionScope();

        foreach (var package in manifest.RootElement.GetProperty("packages").EnumerateArray())
        {
            var id = package.GetProperty("id").GetString();

            foreach (var assembly in package.GetProperty("assemblies").EnumerateArray())
            {
                var declared = assembly.GetProperty("contentHash").GetString();
                var file = assembly.GetProperty("fileName").GetString();

                using var download = await client
                    .GetAsync($"/api/v1/client-contracts/{id}/{declared}/{file}")
                    .ConfigureAwait(false);

                download.StatusCode.Should().Be(HttpStatusCode.OK);

                var bytes = await download.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                Convert.ToHexString(SHA256.HashData(bytes)).Should().Be(
                    declared,
                    "an address is content-addressed, so the bytes it serves are the hash it is named by");
            }
        }
    }

    [Test]
    public async Task ASinkRegisteredAfterThePlatformIsStillDeliveredToAsync()
    {
        // The pipeline is built when it is first resolved, not when AddArronixHost runs, which is what lets
        // this server register its own sink afterwards. The broadcaster below is that registration.
        _server.Services.GetServices<ITelemetrySink>().Should().Contain(
            sink => sink is EventBroadcaster,
            "the server's own sink is registered after the platform's composition");

        _server.Services.GetRequiredService<ITelemetryEmitter>().Emit(
            new TelemetryEvent(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, TelemetrySeverity.Info, "vertical"));

        (await _server.Sink.Waited("vertical").ConfigureAwait(false)).Should().BeTrue(
            "a sink registered after the platform receives what the platform emits");
    }

    private static IReadOnlyList<string?> Closure(JsonElement package)
        => [.. package.GetProperty("closure").EnumerateArray().Select(id => id.GetString())];

    private static IReadOnlyList<string?> Files(JsonElement package)
        => [.. package.GetProperty("assemblies").EnumerateArray()
            .Select(assembly => assembly.GetProperty("fileName").GetString())];

    private static IReadOnlyList<JsonElement> Declarations(JsonElement package)
        => [.. package.GetProperty("assemblies").EnumerateArray()
            .SelectMany(assembly => assembly.GetProperty("declarations").EnumerateArray())];

    private async Task<JsonDocument> ReadAsync(string route)
    {
        using var client = _server.CreateClient();
        using var response = await client.GetAsync(route).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"'{route}' is part of the surface this proves");

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    /// <summary>The server's own entry point, with the two packages installed beside it.</summary>
    private sealed class ArronixServer : WebApplicationFactory<Program>
    {
        private readonly string _state = Path.Combine(
            Path.GetTempPath(),
            "arronix-api-vertical",
            Guid.NewGuid().ToString("N"));

        internal RecordingSink Sink { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var installed = Path.Combine(AppContext.BaseDirectory, "InstalledPlugins");

            builder.UseSetting("Arronix:Plugins:RootFolder", installed);
            builder.UseSetting("Arronix:Host:ExtensionFolder", installed);
            builder.UseSetting("Arronix:Plugins:StateFolder", Path.Combine(_state, "state"));
            builder.UseSetting("Arronix:Library:RootFolders:0", Path.Combine(_state, "library"));
            builder.UseSetting("Arronix:Api:PublishApiDescription", "false");

            // Added, not substituted, and after the platform's composition — the same position the server's
            // own sink is registered in.
            builder.ConfigureServices(services => services.AddSingleton<ITelemetrySink>(Sink));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing && Directory.Exists(_state))
            {
                Directory.Delete(_state, recursive: true);
            }
        }
    }

    /// <summary>A sink that says whether a message reached it.</summary>
    internal sealed class RecordingSink : ITelemetrySink
    {
        private readonly List<string> _received = [];

        public string SinkId => "arronix.api.tests.recording";

        public Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
        {
            lock (_received)
            {
                _received.Add(telemetryEvent.Message);
            }

            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal async Task<bool> Waited(string message)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                lock (_received)
                {
                    if (_received.Contains(message, StringComparer.Ordinal))
                    {
                        return true;
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            return false;
        }
    }
}
