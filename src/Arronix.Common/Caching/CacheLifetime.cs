namespace Arronix.Common.Caching;

/// <summary>
/// Validates the lifetimes callers hand to the caching surface.
/// </summary>
/// <remarks>
/// A zero or negative lifetime is not a shorter cache, it is an entry that is expired before it is
/// readable — which reads at the call site as caching and behaves as a leak of the factory's cost onto
/// every caller. It is refused rather than clamped, because clamping would make the mistake invisible.
/// </remarks>
internal static class CacheLifetime
{
    /// <summary>Requires a supplied lifetime to be strictly positive.</summary>
    /// <param name="lifetime">The lifetime, or <see langword="null"/> when none was supplied.</param>
    /// <param name="parameterName">The caller's parameter name, for the refusal.</param>
    /// <returns>The lifetime, unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A lifetime was supplied and is not positive.</exception>
    internal static TimeSpan? RequirePositive(TimeSpan? lifetime, string parameterName)
    {
        if (lifetime is { } supplied && supplied <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                supplied,
                "A cache lifetime must be greater than zero.");
        }

        return lifetime;
    }

    /// <summary>Requires a mandatory lifetime to be strictly positive.</summary>
    /// <param name="lifetime">The lifetime.</param>
    /// <param name="parameterName">The caller's parameter name, for the refusal.</param>
    /// <returns>The lifetime, unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is not positive.</exception>
    internal static TimeSpan RequirePositive(TimeSpan lifetime, string parameterName)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                lifetime,
                "A cache lifetime must be greater than zero.");
        }

        return lifetime;
    }
}
