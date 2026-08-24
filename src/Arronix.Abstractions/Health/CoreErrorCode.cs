namespace Arronix.Abstractions.Health;

/// <summary>
/// Enumeration of core error codes used throughout the system.
/// Provides a consistent way to identify and categorize errors.
/// </summary>
public enum CoreErrorCode
{
    /// <summary>Unknown or unspecified error.</summary>
    Unknown = 0,

    /// <summary>Invalid configuration.</summary>
    InvalidConfiguration = 1000,

    /// <summary>Missing required configuration.</summary>
    MissingConfiguration = 1001,

    /// <summary>Plugin failed to load.</summary>
    PluginLoadFailure = 2000,

    /// <summary>Plugin contract version mismatch.</summary>
    PluginContractMismatch = 2001,

    /// <summary>Plugin capability not satisfied.</summary>
    PluginCapabilityMissing = 2002,

    /// <summary>The plugin manifest is missing, unparseable or semantically invalid.</summary>
    PluginManifestInvalid = 2003,

    /// <summary>Two discovered plugins, or two registrations, claim the same identifier.</summary>
    PluginIdConflict = 2004,

    /// <summary>A declared capability has no matching registration (the forward check).</summary>
    PluginCapabilityUnsatisfied = 2005,

    /// <summary>A declared naming token collides with a host global token.</summary>
    PluginTokenConflict = 2006,

    /// <summary>The manifest's declared policy graph is inconsistent with what the plugin registered.</summary>
    PluginPolicyDeclarationInvalid = 2007,

    /// <summary>The plugin references an assembly it is not permitted to reference.</summary>
    PluginIsolationViolation = 2008,

    /// <summary>A registered media shape or presentation-intent surface failed validation.</summary>
    PluginShapeInvalid = 2009,

    /// <summary>The plugin is present but disabled by configuration or by an operator.</summary>
    PluginDisabled = 2010,

    /// <summary>
    /// A declared package dependency is not installed, is installed at a version the declared range does
    /// not admit, or is declared more than once.
    /// </summary>
    PluginDependencyUnsatisfied = 2011,

    /// <summary>The plugin lies on a package dependency cycle.</summary>
    PluginDependencyCycle = 2012,

    /// <summary>
    /// The plugin is well-formed, but a package it requires cannot itself be activated. The fault is
    /// reported against that package.
    /// </summary>
    PluginDependencyUnavailable = 2013,

    /// <summary>
    /// A cataloger or curator closed its contract over a media item type no active media kind supplies.
    /// Distinct from a dependency failure: the required package may be installed and active and still not
    /// declare a kind over that exact type.
    /// </summary>
    PluginMediaPairingUnsatisfied = 2014,

    /// <summary>Media kind not found.</summary>
    MediaKindNotFound = 3000,

    /// <summary>Media item not found.</summary>
    MediaItemNotFound = 3001,

    /// <summary>Two plugins claim the same media kind.</summary>
    MediaKindConflict = 3002,

    /// <summary>
    /// Two admitted media kinds are closed over the same item type. Paired providers, external-identifier
    /// recognition and every other item-type lookup resolve a kind from that type, so two owners of one
    /// type make those answers depend on iteration order rather than on the installation.
    /// </summary>
    MediaItemTypeConflict = 3003,

    /// <summary>Parsing failed.</summary>
    ParsingFailed = 4000,

    /// <summary>Quality evaluation failed.</summary>
    QualityEvaluationFailed = 4001,

    /// <summary>Import validation failed.</summary>
    ImportValidationFailed = 5000,

    /// <summary>Import execution failed.</summary>
    ImportExecutionFailed = 5001,

    /// <summary>Indexer connection failed.</summary>
    IndexerConnectionFailed = 6000,

    /// <summary>Indexer search failed.</summary>
    IndexerSearchFailed = 6001,

    /// <summary>Download client connection failed.</summary>
    DownloaderConnectionFailed = 7000,

    /// <summary>Download send failed.</summary>
    DownloadSendFailed = 7001,

    /// <summary>Metadata provider connection failed.</summary>
    CatalogerConnectionFailed = 8000,

    /// <summary>Metadata provider search failed.</summary>
    CatalogerSearchFailed = 8001,

    /// <summary>Job execution failed.</summary>
    JobExecutionFailed = 9000,

    /// <summary>Job scheduling failed.</summary>
    JobSchedulingFailed = 9001
}
