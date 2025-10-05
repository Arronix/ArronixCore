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

    /// <summary>Media kind not found.</summary>
    MediaKindNotFound = 3000,

    /// <summary>Media item not found.</summary>
    MediaItemNotFound = 3001,

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
    DownloadClientConnectionFailed = 7000,

    /// <summary>Download send failed.</summary>
    DownloadSendFailed = 7001,

    /// <summary>Metadata provider connection failed.</summary>
    MetadataProviderConnectionFailed = 8000,

    /// <summary>Metadata provider search failed.</summary>
    MetadataProviderSearchFailed = 8001,

    /// <summary>Job execution failed.</summary>
    JobExecutionFailed = 9000,

    /// <summary>Job scheduling failed.</summary>
    JobSchedulingFailed = 9001
}
