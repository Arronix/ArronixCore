using Arronix.Abstractions.Shape;

namespace Arronix.Client.Catalog;

/// <summary>One validated catalog search request from the generic workspace.</summary>
/// <param name="Scheme">The selected configured catalog scheme.</param>
/// <param name="Text">Optional text to search for.</param>
/// <param name="Identity">Optional <c>scheme:value</c> identity to resolve.</param>
public sealed record CatalogSearch(string Scheme, string? Text, ExternalId? Identity);
