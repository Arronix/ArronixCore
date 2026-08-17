using System.Collections.Frozen;
using System.Linq;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;

#pragma warning disable ARX0001 // Caching contracts are experimental; this assembly gates them.
#pragma warning disable ARX0002 // Diagnostics contracts are experimental; this assembly gates them.
#pragma warning disable ARX0004 // Event contracts are experimental; this assembly gates them.
#pragma warning disable ARX0005 // File-system contracts are experimental; this assembly gates them.
#pragma warning disable ARX0006 // Health contracts are experimental; this assembly gates them.
#pragma warning disable ARX0007 // Hosting contracts are experimental; this assembly gates them.
#pragma warning disable ARX0008 // Outbound-call contracts are experimental; this assembly gates them.
#pragma warning disable ARX0009 // Naming contracts are experimental; this assembly gates them.
#pragma warning disable ARX0010 // Serialization contracts are experimental; this assembly gates them.
#pragma warning disable ARX0011 // Telemetry contracts are experimental; this assembly gates them.
#pragma warning disable ARX0012 // Throttling contracts are experimental; this assembly gates them.
#pragma warning disable ARX0013 // Media-shape contracts are experimental; this assembly gates them.
#pragma warning disable ARX0014 // The extension model is experimental; this assembly implements it.
#pragma warning disable ARX0015 // Provider contracts are experimental; this assembly gates them.
#pragma warning disable ARX0016 // Intent contracts are experimental; this assembly gates them.
#pragma warning disable ARX0019
#pragma warning disable ARX0020 // The typed media surface is experimental; this assembly admits registrations of it. // Definition contracts are experimental; this assembly gates them.

namespace Arronix.Plugins.Registration;

/// <summary>
/// The one table that makes least privilege mechanical.
/// </summary>
/// <remarks>
/// <para>
/// Two directions, two tables. A <i>registration</i> requirement gates something an extension contributes
/// through the registry; a <i>dependency</i> requirement gates something an extension reaches for through
/// its context. Keeping them apart is what lets both halves of the bidirectional check be exact: every
/// gated registration contract must have a registry method, and no dependency contract may be mistaken for
/// one that should have had a registration.
/// </para>
/// <para>
/// A requirement is satisfied by <i>any</i> of the capabilities listed for a contract, not all of them. Only
/// one contract needs that today — folding diacritics is a legitimate interest of both a parser and a
/// renamer — but expressing it as a list rather than as a special case keeps the table readable and the
/// check uniform.
/// </para>
/// <para>
/// Two capabilities appear in no registration row: making outbound calls and reading files are privileges
/// to <i>use</i> something, not to contribute anything. The forward check derives the set of capabilities
/// it can check from this table rather than hard-coding exceptions, so a capability that later grows a
/// registration form starts being forward-checked with no other change.
/// </para>
/// </remarks>
public static class CapabilityMatrix
{
    private static readonly FrozenDictionary<Type, Capability[]> RegistrationRows =
        new Dictionary<Type, Capability[]>
        {
            // The typed media kind. The media-kind capability gates the capture; the kind's sections then
            // demand and satisfy the per-seam capabilities through DefinitionCapabilityRules, so a
            // typed-kind extension is held to the same bidirectional check as an imperative one.
            [typeof(IMediaTypeRegistration)] = [Capability.MediaKind],

            // Media seams.
            [typeof(IMediaShapeProvider)] = [Capability.MediaKind],
            [typeof(IMediaItemSource)] = [Capability.MediaKind],
            [typeof(IReleaseMatcher)] = [Capability.Matching],
            [typeof(IReleaseQueryPlanner)] = [Capability.Indexing],
            [typeof(IReleaseParser)] = [Capability.Parsing],
            [typeof(IQualityModel)] = [Capability.Quality],
            [typeof(IImportPipeline)] = [Capability.Import],
            [typeof(IRenamePolicy)] = [Capability.Renaming],
            [typeof(ILibraryLayout)] = [Capability.Renaming],
            [typeof(IMediaIdResolver)] = [Capability.Metadata],

            // Provider families, one row per family.
            [typeof(IndexerRegistration)] = [Capability.Indexing],
            [typeof(DownloaderRegistration)] = [Capability.Download],
            [typeof(NotifierRegistration)] = [Capability.Notification],
            [typeof(CatalogerRegistration)] = [Capability.Metadata],
            [typeof(CuratorRegistration)] = [Capability.Curation],

            // Platform participation that is a privilege rather than a contribution.
            [typeof(ITelemetrySink)] = [Capability.TelemetrySink],
            [typeof(IOutboundHttpInterceptor)] = [Capability.Indexing],
            [typeof(IDiacriticFoldingProvider)] = [Capability.Parsing, Capability.Renaming]
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, Capability[]> DependencyRows =
        new Dictionary<Type, Capability[]>
        {
            [typeof(IHttpGateway)] = [Capability.Network],
            [typeof(IRateLimiter)] = [Capability.Network],
            [typeof(ICertificateValidationPolicy)] = [Capability.Network],
            [typeof(IFileSystem)] = [Capability.Storage],
            [typeof(IFileTransferService)] = [Capability.Import]
        }.ToFrozenDictionary();

    private static readonly FrozenSet<Type> UngatedRows = new[]
    {
        typeof(ITelemetryEmitter),
        typeof(ITelemetryEnricher),
        typeof(ITelemetryEventFilter),
        typeof(IHealthContributor),
        typeof(IRedactionRuleProvider),
        typeof(IProgressReporter),
        typeof(IPluginPaths),
        typeof(ICacheProvider),
        typeof(IEventPublisher),
        typeof(IEventHandler<>),
        typeof(IJsonSerializer),
        typeof(IHostRuntimeInfo),
        typeof(IOperatingSystemInfo),
        typeof(IScheduledJob),
        typeof(PluginIntentSurface)
    }.ToFrozenSet();

    private static readonly FrozenSet<Capability> ForwardCheckable =
        RegistrationRows.Values.SelectMany(capabilities => capabilities).ToFrozenSet();

    /// <summary>
    /// Gets the contracts an extension registers through the registry, and what each requires.
    /// </summary>
    public static IReadOnlyDictionary<Type, Capability[]> RegistrationRequirements => RegistrationRows;

    /// <summary>
    /// Gets the contracts an extension reaches for through its context, and what each requires.
    /// </summary>
    public static IReadOnlyDictionary<Type, Capability[]> DependencyRequirements => DependencyRows;

    /// <summary>
    /// Gets the contracts that need no capability at all.
    /// </summary>
    /// <remarks>
    /// Listed rather than implied. "Everything not in the gated tables is ungated" would make a contract
    /// added and forgotten silently ungated, which is the wrong default for a privilege model.
    /// </remarks>
    public static IReadOnlySet<Type> UngatedContracts => UngatedRows;

    /// <summary>
    /// Gets the capabilities that a registration can satisfy, and which the forward check therefore
    /// applies to.
    /// </summary>
    public static IReadOnlySet<Capability> ForwardCheckableCapabilities => ForwardCheckable;

    /// <summary>
    /// Gets what a contract requires.
    /// </summary>
    /// <param name="contract">The contract being registered or reached for.</param>
    /// <param name="required">
    /// The capabilities, any one of which suffices, when the contract is gated; otherwise an empty list.
    /// </param>
    /// <returns><see langword="true"/> when the contract is gated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is <see langword="null"/>.</exception>
    public static bool TryGetRequirement(Type contract, out IReadOnlyList<Capability> required)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (RegistrationRows.TryGetValue(contract, out var registration))
        {
            required = registration;
            return true;
        }

        if (DependencyRows.TryGetValue(contract, out var dependency))
        {
            required = dependency;
            return true;
        }

        required = [];
        return false;
    }

    /// <summary>
    /// Determines whether a grant covers a contract.
    /// </summary>
    /// <param name="granted">The capabilities the extension holds, after implication.</param>
    /// <param name="contract">The contract being registered or reached for.</param>
    /// <returns><see langword="true"/> when the contract is ungated or the grant covers it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is <see langword="null"/>.</exception>
    public static bool IsPermitted(CapabilitySet granted, Type contract)
    {
        if (!TryGetRequirement(contract, out var required))
        {
            return true;
        }

        foreach (var capability in required)
        {
            if (granted.Has(capability))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the capability to name in a refusal, which is the first acceptable one.
    /// </summary>
    /// <param name="contract">The contract that was refused.</param>
    /// <returns>The capability to name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="contract"/> is not gated.</exception>
    public static Capability RequirementToReport(Type contract)
    {
        if (!TryGetRequirement(contract, out var required) || required.Count == 0)
        {
            throw new ArgumentException($"'{contract?.Name}' is not a gated contract.", nameof(contract));
        }

        return required[0];
    }
}
