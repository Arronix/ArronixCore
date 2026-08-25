
namespace Arronix.Abstractions.Plugins;

/// <summary>
/// The names capabilities are written under in manifests and published under on the wire.
/// </summary>
/// <remarks>
/// The wire name and the typed value are two spellings of one vocabulary, and this class is the only
/// place that knows both. Everything inside the platform uses the typed value; only the manifest reader
/// and the published projection use the names.
/// </remarks>
public static class CapabilityNames
{
    /// <summary>The wire name of <see cref="Capability.Indexing"/>.</summary>
    public const string Indexing = "indexing";

    /// <summary>The wire name of <see cref="Capability.Metadata"/>.</summary>
    public const string Metadata = "metadata";

    /// <summary>The wire name of <see cref="Capability.Parsing"/>.</summary>
    public const string Parsing = "parsing";

    /// <summary>The wire name of <see cref="Capability.Matching"/>.</summary>
    public const string Matching = "matching";

    /// <summary>The wire name of <see cref="Capability.Quality"/>.</summary>
    public const string Quality = "quality";

    /// <summary>The wire name of <see cref="Capability.Renaming"/>.</summary>
    public const string Renaming = "renaming";

    /// <summary>The wire name of <see cref="Capability.Import"/>.</summary>
    public const string Import = "import";

    /// <summary>The wire name of <see cref="Capability.Download"/>.</summary>
    public const string Download = "download";

    /// <summary>The wire name of <see cref="Capability.Notification"/>.</summary>
    public const string Notification = "notification";

    /// <summary>The wire name of <see cref="Capability.MediaKind"/>.</summary>
    public const string MediaKind = "media-kind";

    /// <summary>The wire name of <see cref="Capability.Curation"/>.</summary>
    public const string Curation = "curation";

    /// <summary>The wire name of <see cref="Capability.Network"/>.</summary>
    public const string Network = "network";

    /// <summary>The wire name of <see cref="Capability.Storage"/>.</summary>
    public const string Storage = "storage";

    /// <summary>The wire name of <see cref="Capability.TelemetrySink"/>.</summary>
    public const string TelemetrySink = "telemetry-sink";

    /// <summary>The wire name of <see cref="Capability.Language"/>.</summary>
    public const string Language = "language";

    /// <summary>The wire name of <see cref="Capability.TelemetryProcessing"/>.</summary>
    public const string TelemetryProcessing = "telemetry-processing";

    /// <summary>
    /// Reads a wire name.
    /// </summary>
    /// <param name="name">The name to read.</param>
    /// <param name="capability">The capability when the name is known; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the name is known; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? name, out Capability capability)
    {
        switch (name)
        {
            case Indexing: capability = Capability.Indexing; return true;
            case Metadata: capability = Capability.Metadata; return true;
            case Parsing: capability = Capability.Parsing; return true;
            case Matching: capability = Capability.Matching; return true;
            case Quality: capability = Capability.Quality; return true;
            case Renaming: capability = Capability.Renaming; return true;
            case Import: capability = Capability.Import; return true;
            case Download: capability = Capability.Download; return true;
            case Notification: capability = Capability.Notification; return true;
            case MediaKind: capability = Capability.MediaKind; return true;
            case Curation: capability = Capability.Curation; return true;
            case Network: capability = Capability.Network; return true;
            case Storage: capability = Capability.Storage; return true;
            case TelemetrySink: capability = Capability.TelemetrySink; return true;
            case Language: capability = Capability.Language; return true;
            case TelemetryProcessing: capability = Capability.TelemetryProcessing; return true;
            default: capability = default; return false;
        }
    }

    /// <summary>
    /// Writes a wire name.
    /// </summary>
    /// <param name="capability">The capability to write.</param>
    /// <returns>The wire name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capability"/> is not a declared capability.</exception>
    public static string ToWireName(Capability capability) => capability switch
    {
        Capability.Indexing => Indexing,
        Capability.Metadata => Metadata,
        Capability.Parsing => Parsing,
        Capability.Matching => Matching,
        Capability.Quality => Quality,
        Capability.Renaming => Renaming,
        Capability.Import => Import,
        Capability.Download => Download,
        Capability.Notification => Notification,
        Capability.MediaKind => MediaKind,
        Capability.Curation => Curation,
        Capability.Network => Network,
        Capability.Storage => Storage,
        Capability.TelemetrySink => TelemetrySink,
        Capability.Language => Language,
        Capability.TelemetryProcessing => TelemetryProcessing,
        _ => throw new ArgumentOutOfRangeException(nameof(capability))
    };
}
