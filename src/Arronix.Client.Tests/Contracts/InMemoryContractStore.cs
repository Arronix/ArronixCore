using Arronix.Client.Contracts;
using Microsoft.JSInterop;

namespace Arronix.Client.Tests.Contracts;

/// <summary>The browser's contract store, in memory, answering the same script the real one calls.</summary>
internal sealed class InMemoryContractStore(params string[] held)
{
    private readonly List<string> _keys = [.. held];

    /// <summary>Gets the content hashes still held, in insertion order.</summary>
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>Opens a store over this content.</summary>
    public ContractStore Open() => new(new StoreRuntime(this));

    private object? Invoke(string identifier, object?[]? args) => identifier switch
    {
        "isAvailable" => true,
        "keys" => _keys.ToArray(),
        "remove" => _keys.Remove((string)args![0]!),
        _ => throw new NotSupportedException($"This store fixture does not answer '{identifier}'."),
    };

    private sealed class StoreRuntime(InMemoryContractStore store) : IJSRuntime, IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult((TValue)(identifier == "import" ? this : store.Invoke(identifier, args))!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => ValueTask.FromResult((TValue)(identifier == "import" ? this : store.Invoke(identifier, args))!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
