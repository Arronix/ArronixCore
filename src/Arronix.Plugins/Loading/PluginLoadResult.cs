using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;


namespace Arronix.Plugins.Loading;

/// <summary>
/// What became of one extension.
/// </summary>
/// <remarks>
/// One type for success and for failure, deliberately. An operator's first question about an extension that
/// is not working is "how far did it get and what stopped it", and a result that only exists on the happy
/// path cannot answer it. The quarantined case carries the whole defect list rather than the first defect,
/// because fixing a declaration one restart at a time is not a diagnosis, it is a punishment.
/// </remarks>
public sealed class PluginLoadResult
{
    private PluginLoadResult(
        string source,
        PluginState state,
        PluginId? id,
        ValidatedManifest? manifest,
        PluginRegistrationLedger? ledger,
        PluginLoadContext? loadContext,
        CoreErrorCode? errorCode,
        string? message,
        IReadOnlyList<string> defects,
        DateTimeOffset changedAt)
    {
        Source = source;
        State = state;
        Id = id;
        Manifest = manifest;
        Ledger = ledger;
        LoadContext = loadContext;
        ErrorCode = errorCode;
        Message = message;
        Defects = defects;
        ChangedAt = changedAt;
    }

    /// <summary>
    /// Gets where the extension was found — its declaration file, or its folder when there was none.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets how far the extension got.
    /// </summary>
    public PluginState State { get; }

    /// <summary>
    /// Gets the extension identifier, or <see langword="null"/> when the declaration was too broken to
    /// yield one.
    /// </summary>
    public PluginId? Id { get; }

    /// <summary>
    /// Gets the proved declaration, or <see langword="null"/> when validation did not get that far.
    /// </summary>
    public ValidatedManifest? Manifest { get; }

    /// <summary>
    /// Gets everything the extension registered, or <see langword="null"/> when it never ran.
    /// </summary>
    public PluginRegistrationLedger? Ledger { get; }

    /// <summary>
    /// Gets the extension's load context, or <see langword="null"/> when none was created.
    /// </summary>
    public PluginLoadContext? LoadContext { get; }

    /// <summary>
    /// Gets the failure class, or <see langword="null"/> when the extension did not fail.
    /// </summary>
    public CoreErrorCode? ErrorCode { get; }

    /// <summary>
    /// Gets the failure message, or <see langword="null"/> when the extension did not fail.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets everything found wrong, or an empty list when nothing was.
    /// </summary>
    public IReadOnlyList<string> Defects { get; }

    /// <summary>
    /// Gets when the extension last changed state.
    /// </summary>
    public DateTimeOffset ChangedAt { get; }

    /// <summary>
    /// Gets a value indicating whether the extension is serving.
    /// </summary>
    public bool IsActive => State == PluginState.Active;

    /// <summary>
    /// Records an extension that reached a state without failing.
    /// </summary>
    /// <param name="source">Where it was found.</param>
    /// <param name="state">The state it reached.</param>
    /// <param name="manifest">Its proved declaration.</param>
    /// <param name="ledger">Everything it registered, when it got that far.</param>
    /// <param name="loadContext">Its load context, when one was created.</param>
    /// <param name="changedAt">When it reached the state.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <see langword="null"/>.</exception>
    public static PluginLoadResult Progressed(
        string source,
        PluginState state,
        ValidatedManifest manifest,
        PluginRegistrationLedger? ledger,
        PluginLoadContext? loadContext,
        DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return new PluginLoadResult(
            source,
            state,
            manifest.Id,
            manifest,
            ledger,
            loadContext,
            errorCode: null,
            message: null,
            defects: [],
            changedAt);
    }

    /// <summary>
    /// Records an extension that failed and contributes nothing.
    /// </summary>
    /// <param name="source">Where it was found.</param>
    /// <param name="id">Its identifier, when one could be read.</param>
    /// <param name="manifest">Its proved declaration, when validation got that far.</param>
    /// <param name="errorCode">The failure class.</param>
    /// <param name="message">What stopped it.</param>
    /// <param name="defects">Everything found wrong.</param>
    /// <param name="changedAt">When it failed.</param>
    /// <returns>The result.</returns>
    public static PluginLoadResult Quarantined(
        string source,
        PluginId? id,
        ValidatedManifest? manifest,
        CoreErrorCode errorCode,
        string message,
        IReadOnlyList<string>? defects,
        DateTimeOffset changedAt)
        => new(
            source,
            PluginState.Quarantined,
            id,
            manifest,
            ledger: null,
            loadContext: null,
            errorCode,
            message,
            defects ?? [],
            changedAt);

    /// <summary>
    /// Returns the same extension in a later state.
    /// </summary>
    /// <param name="state">The state it reached.</param>
    /// <param name="ledger">Everything it registered, when that is what changed.</param>
    /// <param name="loadContext">Its load context, when that is what changed.</param>
    /// <param name="changedAt">When it reached the state.</param>
    /// <returns>The updated result.</returns>
    public PluginLoadResult Advance(
        PluginState state,
        PluginRegistrationLedger? ledger,
        PluginLoadContext? loadContext,
        DateTimeOffset changedAt)
        => new(
            Source,
            state,
            Id,
            Manifest,
            ledger ?? Ledger,
            loadContext ?? LoadContext,
            ErrorCode,
            Message,
            Defects,
            changedAt);

    /// <summary>
    /// Projects the result onto the shape the interface reads.
    /// </summary>
    /// <returns>The published view.</returns>
    /// <remarks>
    /// The projection publishes the granted capability set rather than the declared one, because what an
    /// operator needs to see is what the extension can actually do.
    /// </remarks>
    public PluginStatusView ToStatusView()
        => new(
            Id?.ToString() ?? Source,
            Manifest?.Declaration.Name,
            Manifest?.Version.ToString(),
            State.ToString(),
            Manifest is null
                ? []
                : Manifest.GrantedCapabilities.Enumerate().Select(CapabilityNames.ToWireName).ToArray(),
            ErrorCode is { } code ? (int)code : null,
            Message,
            Defects,
            ChangedAt);
}
