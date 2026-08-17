using System.ComponentModel.DataAnnotations;

namespace Arronix.Api.Configuration;

/// <summary>
/// Settings for the HTTP surface: where the client's static files live, how long descriptors may be
/// cached, and which origins may talk to the API from a different origin than they were served from.
/// </summary>
/// <remarks>
/// The seam exists because the client is deliberately not a project reference of this host. The API
/// serves a directory, so the client can equally be published beside the host, served from a CDN, or
/// wrapped in a native shell without a single line here changing. Everything that would otherwise have
/// been a compile-time coupling to a UI technology is therefore a path and an origin list instead.
/// </remarks>
public sealed class ApiOptions
{
    /// <summary>
    /// The configuration section these settings bind to.
    /// </summary>
    public const string SectionName = "Arronix:Api";

    /// <summary>
    /// Gets or sets the directory the client's published static files are read from, relative to the
    /// content root unless it is rooted.
    /// </summary>
    /// <remarks>
    /// Projects that are not part of the legacy tree build into <c>_temp/bin/&lt;config&gt;/&lt;project&gt;/</c>,
    /// so a client published beside this host lands in this host's own output directory. The default is
    /// therefore relative to the content root rather than to the repository.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string ClientRoot { get; set; } = "wwwroot";

    /// <summary>
    /// Gets or sets the file served for any navigation request that does not match an API route or a file
    /// on disk.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientEntryFile { get; set; } = "index.html";

    /// <summary>
    /// Gets or sets the name of the client's service worker, which is the one file that must never be
    /// served from a cache.
    /// </summary>
    /// <remarks>
    /// A cached service worker is the single failure that makes an installed application impossible to
    /// update: the browser keeps serving the old worker, which keeps serving the old assets, and no
    /// deployment can dislodge it. It is named here rather than pattern-matched so an unusual build layout
    /// can point at the right file.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string ServiceWorkerFileName { get; set; } = "service-worker.js";

    /// <summary>
    /// Gets or sets how long a client may reuse a media-kind descriptor before revalidating it.
    /// </summary>
    /// <remarks>
    /// Descriptors change only when an extension is loaded, upgraded or quarantined, so they are the one
    /// payload worth caching hard. Revalidation is by entity tag, so a stale cache costs one conditional
    /// request rather than a wrong screen.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00", "24:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan DescriptorCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the largest page a collection endpoint will return, whatever the caller asked for.
    /// </summary>
    [Range(1, 1000)]
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Gets or sets the default page size for collection endpoints.
    /// </summary>
    [Range(1, 1000)]
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>
    /// Gets the origins allowed to call this API cross-origin. Empty means same-origin only, which is
    /// what a client served by this host needs.
    /// </summary>
    /// <remarks>
    /// Populated only when the client is deliberately hosted elsewhere. It defaults to empty so the
    /// permissive case has to be asked for in configuration and shows up in a diff.
    /// </remarks>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the machine-readable API description is published.
    /// </summary>
    public bool PublishApiDescription { get; set; } = true;

    /// <summary>
    /// Gets or sets how often the platform's health is re-read so that a change in it can be pushed to
    /// connected clients.
    /// </summary>
    /// <remarks>
    /// Health is the one thing a client cannot learn about from anything it did itself: nobody asked for a
    /// disk to fill up. The aggregate is cached with its own lifetime behind this, so reading it on a timer
    /// is cheap, and only a change in the overall status is pushed.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan HealthPollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
