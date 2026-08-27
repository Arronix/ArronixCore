using Arronix.Abstractions.Wire;

namespace Arronix.Client.Services;

/// <summary>The typed result of explicitly materializing a catalog item.</summary>
/// <param name="Item">The catalog view returned by the host.</param>
/// <param name="Created">
/// Whether this call created the durable item. This is derived only from HTTP 201; HTTP 200 means the
/// item was already present when the request reached the host.
/// </param>
public sealed record CatalogAddResult(CatalogItemView Item, bool Created);
