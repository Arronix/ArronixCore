namespace Arronix.Common.Caching;

/// <summary>
/// When a cache measures an entry's lifetime from.
/// </summary>
internal enum CacheExpiry
{
    /// <summary>The lifetime runs from the moment the entry was stored.</summary>
    Fixed = 0,

    /// <summary>The lifetime restarts on every successful read.</summary>
    Rolling = 1
}
