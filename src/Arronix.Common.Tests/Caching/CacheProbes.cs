namespace Arronix.Common.Tests.Caching;

/// <summary>
/// A type a cache partition is keyed on. Loaded a second time in a collectible context, it stands in for an
/// extension's own owner type.
/// </summary>
public sealed class CacheOwnerProbe;

/// <summary>
/// A value a cache holds. Loaded a second time in a collectible context, it stands in for a value an
/// extension cached.
/// </summary>
public sealed class CacheValueProbe;
