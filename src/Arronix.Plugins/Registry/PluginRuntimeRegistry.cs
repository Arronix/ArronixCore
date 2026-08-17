using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Loading;

#pragma warning disable ARX0014 // The extension model is experimental; this assembly implements it.
#pragma warning disable ARX0017 // The wire contracts are experimental; this assembly projects onto them.

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
/// An extension too broken to yield an identifier is still recorded, keyed by where it was found. Dropping
/// it would mean the one failure an operator cannot diagnose from the logs is also the one the status page
/// does not mention.
/// </para>
/// </remarks>
public sealed class PluginRuntimeRegistry : IPluginRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, PluginLoadResult> _results = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<PluginLoadResult> All
        => [.. _results.Values.OrderBy(result => Key(result), StringComparer.Ordinal)];

    /// <inheritdoc />
    public IReadOnlyList<PluginLoadResult> Active
        => [.. _results.Values.Where(result => result.IsActive).OrderBy(Key, StringComparer.Ordinal)];

    /// <inheritdoc />
    public bool TryGet(PluginId plugin, out PluginLoadResult? result)
        => _results.TryGetValue(plugin.ToString(), out result);

    /// <inheritdoc />
    public IReadOnlyList<PluginStatusView> Snapshot()
        => [.. All.Select(result => result.ToStatusView())];

    /// <summary>
    /// Records what became of an extension, replacing any earlier record of the same one.
    /// </summary>
    /// <param name="result">The outcome.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public void Record(PluginLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _results[Key(result)] = result;
    }

    /// <summary>
    /// Forgets everything recorded.
    /// </summary>
    public void Clear() => _results.Clear();

    private static string Key(PluginLoadResult result) => result.Id?.ToString() ?? result.Source;
}
