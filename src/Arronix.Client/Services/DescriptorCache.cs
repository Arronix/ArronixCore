#pragma warning disable ARX0017 // Wire contracts are experimental; the descriptor is what this cache holds.

using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Wire;

namespace Arronix.Client.Services;

/// <summary>
/// Holds the media-kind descriptions the whole client renders from.
/// </summary>
/// <remarks>
/// <para>
/// A descriptor is this application's entire schema: every view takes one as a parameter and reads its
/// levels, fields, actions and traversals from it. Fetching it once and holding it is therefore not an
/// optimization but the boot sequence — until it has arrived there is nothing any view could render.
/// </para>
/// <para>
/// It is invalidated when an extension changes state, because that is the one event that can add, remove
/// or re-describe a media kind. Nothing else can, which is why there is no polling here.
/// </para>
/// </remarks>
public sealed class DescriptorCache : IDisposable
{
    private readonly ArronixApiClient _api;
    private readonly EventStream _events;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MediaKindDescriptor>? _kinds;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DescriptorCache"/> class.
    /// </summary>
    /// <param name="api">The client used to fetch descriptions.</param>
    /// <param name="events">The live connection whose extension-state events invalidate the cache.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public DescriptorCache(ArronixApiClient api, EventStream events)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(events);

        _api = api;
        _events = events;
        _events.Received += OnReceived;
    }

    /// <summary>
    /// Occurs when the held descriptions have been discarded and must be fetched again.
    /// </summary>
    public event EventHandler? Invalidated;

    /// <summary>
    /// Gets the descriptions already held, which is empty until the first fetch completes.
    /// </summary>
    public IReadOnlyList<MediaKindDescriptor> Loaded => _kinds ?? [];

    /// <summary>
    /// Reads every registered media kind, fetching once and reusing thereafter.
    /// </summary>
    /// <param name="cancellationToken">Abandons the fetch.</param>
    /// <returns>The media kinds, ordered by their display name.</returns>
    public async ValueTask<IReadOnlyList<MediaKindDescriptor>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (_kinds is { } held)
        {
            return held;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_kinds is { } raced)
            {
                return raced;
            }

            var fetched = await _api.GetKindsAsync(cancellationToken).ConfigureAwait(false);
            _kinds = fetched.OrderBy(kind => kind.PluralName, StringComparer.CurrentCultureIgnoreCase).ToList();
            return _kinds;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads one media kind's description.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="cancellationToken">Abandons the fetch.</param>
    /// <returns>The description, or <see langword="null"/> when the kind is not registered.</returns>
    public async ValueTask<MediaKindDescriptor?> GetAsync(
        MediaKindId kind,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(candidate => candidate.Kind == kind);
    }

    /// <summary>
    /// Discards the held descriptions so the next read fetches them again.
    /// </summary>
    public void Invalidate()
    {
        if (_kinds is null)
        {
            return;
        }

        _kinds = null;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _events.Received -= OnReceived;
        _gate.Dispose();
    }

    private void OnReceived(object? sender, EventEnvelope envelope)
    {
        if (envelope.Kind is EventKind.PluginStateChanged)
        {
            Invalidate();
        }
    }
}
