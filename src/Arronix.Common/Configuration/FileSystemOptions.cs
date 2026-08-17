using System.ComponentModel.DataAnnotations;

namespace Arronix.Common.Configuration;

/// <summary>
/// Operator control over how the platform probes, transfers and browses storage.
/// </summary>
/// <remarks>
/// <para>
/// Every list here is empty or minimal by default and additive when configured: the configuration binder
/// appends to the values below rather than replacing them. That is deliberate. A shared foundation cannot
/// know that a particular file name is one download client's debug log or that a particular folder name is
/// one storage vendor's thumbnail cache, so those names are supplied by whoever does know, and an
/// installation that stops using that component stops carrying its exclusions.
/// </para>
/// <para>
/// The folder exclusions that <em>are</em> defaulted name operating-system directories only, because a
/// media library is never inside one and walking into them wastes time on every scan.
/// </para>
/// </remarks>
public sealed class FileSystemOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:FileSystem";

    /// <summary>
    /// Gets or sets the name of the file written and deleted again to prove a folder is writable. It is a
    /// bare file name: any path separator in it would let a writability probe escape the folder it is
    /// probing.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression(
        @"^[^\\/:*?""<>|]+$",
        ErrorMessage = "The write probe file name must be a bare file name containing no path separators.")]
    public string WriteProbeFileName { get; set; } = ".write-probe.tmp";

    /// <summary>
    /// Gets or sets the suffix appended to a folder that is moved out of the way during a transfer, so the
    /// original name is free for a case-only rename. The trailing character is chosen to sort after
    /// ordinary names and to be visibly temporary to an operator who finds one left behind after a crash.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(16, MinimumLength = 1)]
    [RegularExpression(
        @"^[^\\/:*?""<>|]+$",
        ErrorMessage = "The transfer backup suffix must contain no path separators.")]
    public string TransferBackupSuffix { get; set; } = ".backup~";

    /// <summary>
    /// Gets or sets the size at which a file left behind in a source folder blocks that folder from being
    /// deleted after a move, in bytes. Below the threshold the leftover is assumed to be an artifact worth
    /// discarding; above it, it is assumed to be content worth keeping and the move fails loudly.
    /// </summary>
    [Range(0L, long.MaxValue)]
    public long LargeFileThresholdBytes { get; set; } = 100L * 1024L * 1024L;

    /// <summary>
    /// Gets or sets how many times a failed transfer is retried before the failure is reported.
    /// </summary>
    [Range(0, 10)]
    public int TransferRetryCount { get; set; } = 2;

    /// <summary>
    /// Gets or sets how long to wait before retrying a failed transfer. The wait is asynchronous, so it
    /// costs no thread.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:05:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan TransferRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets the file and folder name prefixes a transfer skips. Defaults to the prefix a network filesystem
    /// gives a file that was deleted while still open, which is an artifact of the filesystem itself rather
    /// than of anything that put content there.
    /// </summary>
    public IList<string> IgnoredNamePrefixes { get; } = [".nfs"];

    /// <summary>
    /// Gets the exact file names a transfer skips. Empty by default: a name in this list is always some
    /// specific component's scratch file, and that component supplies it.
    /// </summary>
    public IList<string> IgnoredFileNames { get; } = [];

    /// <summary>
    /// Gets the file name suffixes a transfer skips. Empty by default, for the same reason as
    /// <see cref="IgnoredFileNames"/>; socket and lock files are the usual entries.
    /// </summary>
    public IList<string> IgnoredFileNameSuffixes { get; } = [];

    /// <summary>
    /// Gets the folder names, compared case-insensitively, that a scan or a folder listing does not descend
    /// into. Storage-appliance vendors each have their own; add them here rather than expecting the
    /// platform to know them.
    /// </summary>
    public IList<string> ExcludedFolderNames { get; } =
    [
        "$recycle.bin",
        "boot",
        "bootmgr",
        "cache",
        "cachedmessages",
        "caches",
        "msocache",
        "recovery",
        "recycler",
        "system volume information",
        "temporary internet files",
        "trash",
        "windows",
        ".fseventd",
        ".spotlight",
        ".trashes",
        ".vol",
    ];
}
