using System.IO;
using System.Linq;
using Arronix.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

#pragma warning disable CA1848 // Source-generated log delegates buy an allocation on a hot path;
                               // every call site in this file is a startup event or a rare failure.

namespace Arronix.Api.Hosting;

/// <summary>
/// Serves whichever client has been published beside this host, with the cache rules an installable
/// application needs to be able to update itself.
/// </summary>
/// <remarks>
/// <para>
/// The client is served as a directory of files rather than through the conventional hosted-application
/// wiring, and that is a deliberate structural choice rather than an omission. The conventional wiring
/// would require a project reference and a UI-framework hosting package in this assembly, which would make
/// "this server is not a UI framework host" an unverifiable claim; serving a directory keeps the package
/// set empty, keeps the assertion mechanical, and leaves the client independently deployable behind a
/// content delivery network, on another origin, or inside a native shell.
/// </para>
/// <para>
/// The price is the content-type table and the cache policy below, which the hosting package would
/// otherwise have supplied. Both are small, and both are things this host wants an opinion about anyway.
/// </para>
/// </remarks>
internal static class ClientStaticFiles
{
    /// <summary>Path segments that belong to the API and must never be answered by a file or a fallback.</summary>
    private static readonly string[] ReservedPrefixes = ["/api", "/hub", "/health", "/openapi"];

    /// <summary>
    /// Serves the client's files and sends every unmatched navigation to its entry document.
    /// </summary>
    /// <param name="app">The application being configured.</param>
    /// <returns>The same application, for chaining.</returns>
    internal static WebApplication UseArronixClient(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<IOptions<ApiOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ClientStaticFiles));
        var root = ResolveClientRoot(app.Environment, options);

        if (!Directory.Exists(root))
        {
            // The client is a separate deliverable with its own build. Its absence is an ordinary
            // deployment state — a headless install, an API-only container, a server started from a source
            // tree before the client was published — and the API must be fully usable in it. Answering
            // navigations with an explanation is more useful than a bare 404, and much more useful than
            // failing to start.
            logger.LogInformation(
                "No client was found at {ClientRoot}. The API is serving without one; publish a client there or point Arronix:Api:ClientRoot at it.",
                root);

            app.MapFallback(static (HttpContext context) =>
                Results.Text(
                    "No Arronix client is published at this host's configured client root. The REST API is available under /api/v1 and the event stream at /hub/events.",
                    "text/plain",
                    System.Text.Encoding.UTF8,
                    StatusCodes.Status503ServiceUnavailable));

            return app;
        }

        var fileProvider = new PhysicalFileProvider(root);
        var contentTypes = BuildContentTypeProvider();
        var serviceWorker = "/" + options.ServiceWorkerFileName.TrimStart('/');
        var entryFile = options.ClientEntryFile;

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            DefaultFileNames = [entryFile],
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ContentTypeProvider = contentTypes,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context => ApplyCachePolicy(context, serviceWorker, entryFile),
        });

        app.MapFallback(async (HttpContext context) =>
        {
            if (ReservedPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound();
            }

            var entry = fileProvider.GetFileInfo(entryFile);
            if (!entry.Exists)
            {
                return Results.NotFound();
            }

            // The entry document is the application's bootstrapper: it names the current asset manifest, so
            // a cached copy pins the whole application to a previous deployment.
            ApplyNoStore(context.Response);
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(entry, context.RequestAborted).ConfigureAwait(false);
            return Results.Empty;
        });

        logger.LogInformation("Serving the Arronix client from {ClientRoot}.", root);
        return app;
    }

    private static string ResolveClientRoot(IHostEnvironment environment, ApiOptions options)
        => Path.IsPathRooted(options.ClientRoot)
            ? Path.GetFullPath(options.ClientRoot)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.ClientRoot));

    /// <summary>
    /// Maps the file extensions a compiled-to-the-browser client ships that the framework's own table does
    /// not know about. An unmapped extension is not served at all, so this table is load-bearing.
    /// </summary>
    private static FileExtensionContentTypeProvider BuildContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();

        provider.Mappings[".wasm"] = "application/wasm";
        provider.Mappings[".dat"] = "application/octet-stream";
        provider.Mappings[".blat"] = "application/octet-stream";
        provider.Mappings[".dll"] = "application/octet-stream";
        provider.Mappings[".pdb"] = "application/octet-stream";
        provider.Mappings[".symbols"] = "application/octet-stream";
        provider.Mappings[".br"] = "application/x-brotli";
        provider.Mappings[".gz"] = "application/gzip";

        // The installability descriptor. A browser refuses to treat an application as installable when this
        // file arrives under any other media type, which is a silent failure with no console error.
        provider.Mappings[".webmanifest"] = "application/manifest+json";

        return provider;
    }

    private static void ApplyCachePolicy(StaticFileResponseContext context, string serviceWorkerPath, string entryFile)
    {
        var path = context.Context.Request.Path;
        var name = context.File.Name;

        if (path.StartsWithSegments(serviceWorkerPath, StringComparison.OrdinalIgnoreCase))
        {
            // THE ONE RULE THIS WHOLE FILE EXISTS FOR.
            //
            // A cached service worker is unrecoverable from the server side: the browser keeps running the
            // old worker, the old worker keeps answering from the old cache, and no amount of redeploying
            // reaches the user. Browsers now bypass the HTTP cache for the worker script on their own, but
            // intermediaries and reverse proxies do not, and this is the header that stops them.
            ApplyNoStore(context.Context.Response);

            // Lets a worker served from anywhere claim the whole origin, so an installation whose files
            // move under a sub-path keeps controlling the routes the client actually navigates to.
            context.Context.Response.Headers["Service-Worker-Allowed"] = "/";
            return;
        }

        // The worker's own asset manifest and the boot manifest are the same class of file: each one names
        // the exact set of assets the application will then fetch, so caching either freezes a deployment.
        if (name.Equals("service-worker-assets.js", StringComparison.OrdinalIgnoreCase)
            || name.Equals("blazor.boot.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals(entryFile, StringComparison.OrdinalIgnoreCase))
        {
            ApplyNoStore(context.Context.Response);
            return;
        }

        // Content-addressed framework payloads never change under a fixed name, so they are the one thing
        // worth caching hard.
        if (path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
            return;
        }

        // Everything else revalidates. In an installable application the worker is the real cache; letting
        // the HTTP cache hold application assets as well is how a deployment ends up half-applied.
        context.Context.Response.Headers[HeaderNames.CacheControl] = "public, no-cache";
    }

    private static void ApplyNoStore(HttpResponse response)
    {
        response.Headers[HeaderNames.CacheControl] = "no-cache, no-store, must-revalidate";
        response.Headers[HeaderNames.Pragma] = "no-cache";
        response.Headers[HeaderNames.Expires] = "0";
    }
}
