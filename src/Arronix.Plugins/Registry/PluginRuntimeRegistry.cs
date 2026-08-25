using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;


namespace Arronix.Plugins.Registry;

/// <summary>
/// The record of what became of every extension.
/// </summary>
/// <remarks>
/// <para>
/// Concurrent because the interface reads it while the host is still loading. Loading is single-threaded
/// today, but a registry that is only safe to read once loading has finished is a registry that cannot back
/// a status page during startup — which is exactly when an operator most wants one.
/// </para>
/// <para>
/// Every record is keyed by where the package was found, because that is the one thing unique to an
/// installed copy. Two folders claiming one identifier are two records: an operator has two folders to act
/// on, and keying by identifier would show them one. An extension too broken to yield an identifier at all
/// is recorded for the same reason.
/// </para>
/// </remarks>
public sealed class PluginRuntimeRegistry : IPluginRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, PluginLoadResult> _results = new(StringComparer.Ordinal);
    private readonly PluginPublicationGate _publication;

    /// <summary>Creates a standalone runtime registry with its own publication boundary.</summary>
    public PluginRuntimeRegistry()
        : this(new PluginPublicationGate())
    {
    }

    /// <summary>Creates a runtime registry participating in one publication boundary.</summary>
    public PluginRuntimeRegistry(PluginPublicationGate publication)
    {
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    }

    /// <summary>Gets the publication boundary this registry participates in.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

    /// <summary>Gets every raw result for the lifecycle coordinator.</summary>
    internal IReadOnlyList<PluginLoadResult> All
    {
        get
        {
            using var publication = _publication.EnterRead();
            return [.. _results.Values.OrderBy(Order, StringComparer.Ordinal)];
        }
    }

    /// <summary>Gets every active raw result for the lifecycle coordinator.</summary>
    internal IReadOnlyList<PluginLoadResult> Active
    {
        get
        {
            using var publication = _publication.EnterRead();
            return [.. _results.Values.Where(result => result.IsActive).OrderBy(Order, StringComparer.Ordinal)];
        }
    }

    /// <summary>Finds one raw result for the lifecycle coordinator.</summary>
    /// <remarks>
    /// A live result wins over a refused one: an identifier claimed by two folders has at most one outcome
    /// that matters to a caller asking about the extension rather than about a folder.
    /// </remarks>
    internal bool TryGet(PluginId plugin, out PluginLoadResult? result)
    {
        using var publication = _publication.EnterRead();

        result = _results.Values
            .Where(candidate => candidate.Id == plugin)
            .OrderByDescending(candidate => candidate.IsActive)
            .ThenBy(candidate => candidate.Source, StringComparer.Ordinal)
            .FirstOrDefault();

        return result is not null;
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginStatusView> Snapshot()
    {
        using var publication = _publication.EnterRead();
        return
        [
            .. _results.Values
                .OrderBy(Order, StringComparer.Ordinal)
                .Select(result => result.ToStatusView()),
        ];
    }

    /// <summary>
    /// Records what became of an extension, replacing any earlier record of the same one.
    /// </summary>
    /// <param name="result">The outcome.</param>
    /// <returns>
    /// <see langword="false"/> when a different active attempt already owns the same identifier; otherwise
    /// <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    internal bool Record(PluginLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        using var publication = _publication.EnterWrite();

        // A folder has one outcome, and an identifier has at most one live one. Both are checked: replacing
        // this folder's record is ordinary, but taking an identifier another folder is actively serving is
        // the conflict the publication gate exists to catch.
        if (_results.Values.Any(existing =>
                existing.IsActive
                && existing.Id == result.Id
                && !ReferenceEquals(existing, result)))
        {
            return false;
        }

        _results[Key(result)] = result;
        return true;
    }

    /// <summary>
    /// Forgets every non-active result. Active results require the exact teardown transition instead.
    /// </summary>
    internal void Clear()
    {
        using var publication = _publication.EnterWrite();

        if (_results.Values.Any(result => result.IsActive))
        {
            throw new InvalidOperationException(
                "An active extension result owns a runtime lifetime and cannot be cleared without teardown.");
        }

        _results.Clear();
    }

    /// <summary>Checks whether a new active result may own this extension identifier.</summary>
    internal bool CanActivate(PluginId plugin)
    {
        using var publication = _publication.EnterRead();
        return !_results.Values.Any(existing => existing.IsActive && existing.Id == plugin);
    }

    /// <summary>
    /// Replaces the exact active result with a reference-free stopped result and returns its lifetime.
    /// </summary>
    internal bool TryStop(
        PluginLoadResult expected,
        DateTimeOffset changedAt,
        out PackageAdmissionLease? lifetime)
    {
        ArgumentNullException.ThrowIfNull(expected);

        using var publication = _publication.EnterWrite();

        var key = Key(expected);
        if (!_results.TryGetValue(key, out var active)
            || !ReferenceEquals(active, expected)
            || !active.IsActive)
        {
            lifetime = null;
            return false;
        }

        lifetime = active.PackageLease;

        // Under the same write lease that removes the published result, so a dispatch path either took its
        // lease before this and is waited for, or cannot take one at all.
        lifetime?.CloseToInvocation();
        lifetime?.UnpublishTokenClaims();
        _results[key] = active.Stop(changedAt);
        return true;
    }

    /// <summary>The identity of one installed copy: where it was found.</summary>
    private static string Key(PluginLoadResult result) => result.Source;

    /// <summary>The order results are reported in: by identifier, then by folder.</summary>
    private static string Order(PluginLoadResult result)
        => $"{result.Id?.ToString() ?? string.Empty}\u0000{result.Source}";
}
