using Microsoft.JSInterop;

namespace Arronix.Client.Contracts;

/// <summary>
/// This browser's persistent store of contract assembly bytes, keyed by content hash.
/// </summary>
/// <remarks>
/// <para>
/// A key that names its own bytes is what makes this a store rather than a cache with an invalidation
/// problem. An entry is either the one the host asked for or it is absent; it can never be the wrong bytes
/// under the right name, so nothing here has to be told when an installation changes.
/// </para>
/// <para>
/// The store is an optimization and is treated as one. Every failure — no secure context, a browser with
/// storage switched off, a quota refusal — degrades to refetching over the network, which is slower and
/// exactly as correct. What must never happen is loading bytes because they were in a store: the loader
/// hashes whatever it gets, from wherever it got it, before the runtime sees it.
/// </para>
/// <para>
/// Bytes cross the interop boundary as base64 text. It costs a third more transfer for an assembly measured
/// in kilobytes, and it buys a boundary with one representation on both sides that cannot be misread.
/// </para>
/// </remarks>
public sealed class ContractStore : IAsyncDisposable
{
    private const string ModulePath = "./js/contract-store.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private bool _unavailable;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractStore"/> class.
    /// </summary>
    /// <param name="js">The browser's script host.</param>
    /// <exception cref="ArgumentNullException"><paramref name="js"/> is <see langword="null"/>.</exception>
    public ContractStore(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        _js = js;
    }

    /// <summary>
    /// Gets whether this browser gave the client somewhere to keep contract bytes.
    /// </summary>
    /// <remarks>False until the first operation has had a chance to find out.</remarks>
    public bool IsAvailable { get; private set; }

    /// <summary>
    /// Reads the bytes held under a content hash.
    /// </summary>
    /// <param name="contentHash">The content hash naming the bytes.</param>
    /// <returns>The bytes, or <see langword="null"/> when this browser is not holding them.</returns>
    public async Task<byte[]?> ReadAsync(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var module = await OpenAsync();
        if (module is null)
        {
            return null;
        }

        try
        {
            var encoded = await module.InvokeAsync<string?>("read", contentHash);
            return encoded is null ? null : Convert.FromBase64String(encoded);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Holds bytes under a content hash.
    /// </summary>
    /// <param name="contentHash">The content hash naming the bytes.</param>
    /// <param name="content">The bytes.</param>
    /// <returns>Whether they were held.</returns>
    public async Task<bool> WriteAsync(string contentHash, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentNullException.ThrowIfNull(content);

        var module = await OpenAsync();
        if (module is null)
        {
            return false;
        }

        try
        {
            return await module.InvokeAsync<bool>("write", contentHash, Convert.ToBase64String(content));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Lists the content hashes this browser is currently holding.
    /// </summary>
    /// <returns>The held hashes, in no particular order.</returns>
    public async Task<IReadOnlyList<string>> KeysAsync()
    {
        var module = await OpenAsync();
        if (module is null)
        {
            return [];
        }

        try
        {
            return await module.InvokeAsync<string[]>("keys");
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Discards the bytes held under a content hash.
    /// </summary>
    /// <param name="contentHash">The content hash naming the bytes.</param>
    /// <returns>Whether anything was discarded.</returns>
    public async Task<bool> RemoveAsync(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var module = await OpenAsync();
        if (module is null)
        {
            return false;
        }

        try
        {
            return await module.InvokeAsync<bool>("remove", contentHash);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Discards everything this browser is holding, which is what a clean start means.
    /// </summary>
    /// <returns>Whether the store was discarded.</returns>
    public async Task<bool> ClearAsync()
    {
        var module = await OpenAsync();
        if (module is null)
        {
            return false;
        }

        try
        {
            return await module.InvokeAsync<bool>("clear");
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not { } module)
        {
            return;
        }

        _module = null;

        try
        {
            await module.DisposeAsync();
        }
        catch (Exception)
        {
            // A page being torn down has already disconnected the script host in some browsers, and there
            // is nothing left for a failure here to protect.
        }
    }

    private async Task<IJSObjectReference?> OpenAsync()
    {
        if (_module is { } held)
        {
            return held;
        }

        if (_unavailable)
        {
            return null;
        }

        try
        {
            var module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            IsAvailable = await module.InvokeAsync<bool>("isAvailable");
            _module = module;
            return module;
        }
        catch (Exception)
        {
            _unavailable = true;
            IsAvailable = false;
            return null;
        }
    }
}
