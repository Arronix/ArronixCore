using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>An external identity marker recognized in input text by its owning cataloger.</summary>
/// <param name="Id">The catalog-owned identifier.</param>
/// <param name="Marker">The complete marker spelling that was recognized.</param>
/// <param name="Index">The marker's zero-based position in the input.</param>
public readonly record struct ExternalIdReading(ExternalId Id, string Marker, int Index);
