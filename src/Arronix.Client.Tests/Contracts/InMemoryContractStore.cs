using Arronix.Client.Contracts;
using Microsoft.JSInterop;

namespace Arronix.Client.Tests.Contracts;

/// <summary>The browser's contract store, in memory, answering the same script the real one calls.</summary>
/// <remarks>
/// Reads always miss, so a load fetches over the network and writes what it verified — which is what makes
/// "a stale sweep evicts what the newest installation just fetched" reachable from a test.
/// </remarks>
internal sealed class InMemoryContractStore(params string[] held)
{
    private readonly List<string> _keys = [.. held];
    private TaskCompletionSource? _hold;
    private TaskCompletionSource? _reached;
    private Exception? _failListing;

    /// <summary>Gets the content hashes still held, in insertion order.</summary>
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>Opens a store over this content. One instance is one browser's store.</summary>
    public ContractStore Open() => new(new StoreRuntime(this));

    /// <summary>Drops a content hash, as a sweep this fixture is not modelling would have.</summary>
    /// <param name="contentHash">The hash to drop.</param>
    public void Discard(string contentHash) => _keys.Remove(contentHash);

    /// <summary>Stalls the next listing until the returned release is completed.</summary>
    /// <param name="reached">Completed once that listing has been asked for and is waiting.</param>
    /// <returns>The release to complete when the listing may proceed.</returns>
    /// <remarks>
    /// Listing is a sweep's first act, so holding it holds one whole sweep at a point where a second
    /// transaction can be started and observed either overlapping it or queued behind it.
    /// </remarks>
    public TaskCompletionSource HoldNextListing(out Task reached)
    {
        _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reached = _reached.Task;
        return _hold;
    }

    /// <summary>Makes the next listing raise a failure rather than answer.</summary>
    /// <param name="failure">What it raises.</param>
    /// <remarks>The one call in a transaction that every step makes, so it reaches every containment.</remarks>
    public void FailNextListing(Exception failure) => _failListing = failure;

    private object? Invoke(string identifier, object?[]? args) => identifier switch
    {
        "isAvailable" => true,
        "read" => null,
        "write" => Write((string)args![0]!),
        "keys" => _keys.ToArray(),
        "clear" => Clear(),
        "remove" => _keys.Remove((string)args![0]!),
        _ => throw new NotSupportedException($"This store fixture does not answer '{identifier}'."),
    };

    private bool Clear()
    {
        _keys.Clear();
        return true;
    }

    private bool Write(string contentHash)
    {
        if (!_keys.Contains(contentHash))
        {
            _keys.Add(contentHash);
        }

        return true;
    }

    private ValueTask<TValue> Answer<TValue>(object module, string identifier, object?[]? args)
    {
        if (identifier == "import")
        {
            return ValueTask.FromResult((TValue)module);
        }

        if (identifier == "keys" && _failListing is { } raised)
        {
            _failListing = null;
            throw raised;
        }

        if (identifier == "keys" && _hold is { } hold)
        {
            var reached = _reached;
            _hold = null;
            _reached = null;
            return new ValueTask<TValue>(Stalled<TValue>(hold, reached, identifier, args));
        }

        return ValueTask.FromResult((TValue)Invoke(identifier, args)!);
    }

    /// <remarks>
    /// Answered from the store as it is when asked, and delivered when released: a read that happened early
    /// and completed late is the hazard, not one that reads late.
    /// </remarks>
    private async Task<TValue> Stalled<TValue>(
        TaskCompletionSource hold,
        TaskCompletionSource? reached,
        string identifier,
        object?[]? args)
    {
        var answer = (TValue)Invoke(identifier, args)!;
        reached?.TrySetResult();
        await hold.Task.ConfigureAwait(false);
        return answer;
    }

    private sealed class StoreRuntime(InMemoryContractStore store) : IJSRuntime, IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => store.Answer<TValue>(this, identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => store.Answer<TValue>(this, identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
