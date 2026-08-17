namespace Arronix.Common.Time;

/// <summary>
/// Date and time operations bound to an injected clock.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here reads the ambient system clock. Every member that needs to know the current moment takes a
/// <see cref="TimeProvider"/>, which is the one registration a host substitutes to make everything that
/// depends on time — scheduling windows, cache expiry, retry back-off — deterministic in a test. Reaching
/// for the static clock inside a helper puts a hidden, unsubstitutable dependency into every caller.
/// </para>
/// <para>
/// The set is short because most of what the platform inherited was a restatement of a comparison operator.
/// What survives is the pair that preserves round-trip fidelity against storage whose time resolution is
/// coarser than the runtime's, and the two window predicates whose only real content was the clock they
/// could not previously be given.
/// </para>
/// </remarks>
public static class DateTimeExtensions
{
    /// <summary>
    /// Truncates the value to whole seconds by discarding the sub-second part.
    /// </summary>
    /// <param name="value">The value to truncate.</param>
    /// <returns>The same instant with its sub-second ticks removed, keeping its <see cref="DateTimeKind"/>.</returns>
    /// <remarks>
    /// Storage that records time to the second will not return what it was given. Comparing a value that has
    /// been through such a store against one that has not therefore fails on a difference no one can see;
    /// truncating both first is what makes the comparison mean what it reads as.
    /// </remarks>
    public static DateTime WithoutTicks(this DateTime value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    /// <summary>
    /// Truncates the value to whole seconds and then re-applies the sub-second part of another value.
    /// </summary>
    /// <param name="value">The value to adjust.</param>
    /// <param name="other">The value whose sub-second part is adopted.</param>
    /// <returns>
    /// <paramref name="value"/> to the second, carrying the sub-second ticks of <paramref name="other"/>.
    /// </returns>
    /// <remarks>
    /// The companion to <see cref="WithoutTicks"/>: it makes a freshly computed value comparable to a stored
    /// one without either being rounded away.
    /// </remarks>
    public static DateTime WithTicksFrom(this DateTime value, DateTime other) =>
        value.WithoutTicks().AddTicks(other.Ticks % TimeSpan.TicksPerSecond);

    /// <summary>
    /// Determines whether the value falls between now and a window into the future.
    /// </summary>
    /// <param name="value">The instant to test, interpreted as UTC.</param>
    /// <param name="window">How far ahead the window extends. Must not be negative.</param>
    /// <param name="timeProvider">The clock supplying the current moment.</param>
    /// <returns>
    /// <see langword="true"/> when the value is at or after now and at or before the end of the window.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is negative.</exception>
    /// <remarks>
    /// The clock is read once and both bounds are derived from that single reading. Reading it twice, as the
    /// implementation this replaces did, leaves a window that can move between the two comparisons, so a
    /// value sitting exactly on the boundary could satisfy neither.
    /// </remarks>
    public static bool InNext(this DateTime value, TimeSpan window, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        return value >= now && value <= now.Add(window);
    }

    /// <summary>
    /// Determines whether the value falls between a window into the past and now.
    /// </summary>
    /// <param name="value">The instant to test, interpreted as UTC.</param>
    /// <param name="window">How far back the window extends. Must not be negative.</param>
    /// <param name="timeProvider">The clock supplying the current moment.</param>
    /// <returns>
    /// <see langword="true"/> when the value is at or after the start of the window and at or before now.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is negative.</exception>
    /// <remarks>
    /// The clock is read once, for the reason given on <see cref="InNext"/>.
    /// </remarks>
    public static bool InLast(this DateTime value, TimeSpan window, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        return value >= now.Subtract(window) && value <= now;
    }
}
