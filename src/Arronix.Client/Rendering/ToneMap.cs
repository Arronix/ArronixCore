#pragma warning disable ARX0016 // Intent contracts are experimental; tone is what this table maps.

using Arronix.Abstractions.Health;
using Arronix.Abstractions.Intent;

namespace Arronix.Client.Rendering;

/// <summary>
/// Turns declared valence into this client's own styling.
/// </summary>
/// <remarks>
/// <para>
/// One of the five tables that make up the whole coupling between what an extension declares and what
/// this application draws. An extension says a state is a problem; only this file decides what a problem
/// looks like, and a different front end would answer the same declaration with a prefix character, a
/// spoken emphasis or an exit code.
/// </para>
/// <para>
/// Every method lists each member of its enumeration and has no fall-through arm, so a vocabulary that
/// grows a member fails this project's build until someone decides what the new member means here.
/// </para>
/// </remarks>
public static class ToneMap
{
    /// <summary>
    /// Gets the style class for a declared state's valence.
    /// </summary>
    /// <param name="tone">The valence.</param>
    /// <returns>The class name.</returns>
    public static string ClassFor(StateTone tone) => tone switch
    {
        StateTone.Neutral => "tone-neutral",
        StateTone.Positive => "tone-positive",
        StateTone.Attention => "tone-attention",
        StateTone.Problem => "tone-problem",
        StateTone.InProgress => "tone-progress",
    };

    /// <summary>
    /// Gets the text marker for a declared state's valence, used where no styling reaches: a document
    /// title, a plain-text copy, an assistive-technology label.
    /// </summary>
    /// <param name="tone">The valence.</param>
    /// <returns>The marker.</returns>
    public static string MarkerFor(StateTone tone) => tone switch
    {
        StateTone.Neutral => "·",
        StateTone.Positive => "✓",
        StateTone.Attention => "!",
        StateTone.Problem => "×",
        StateTone.InProgress => "…",
    };

    /// <summary>
    /// Gets the style class for a health status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The class name.</returns>
    public static string ClassFor(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "tone-positive",
        HealthStatus.Degraded => "tone-attention",
        HealthStatus.Unhealthy => "tone-problem",
    };

    /// <summary>
    /// Gets the style class for a health issue's severity.
    /// </summary>
    /// <param name="severity">The severity.</param>
    /// <returns>The class name.</returns>
    public static string ClassFor(HealthSeverity severity) => severity switch
    {
        HealthSeverity.Info => "tone-neutral",
        HealthSeverity.Warning => "tone-attention",
        HealthSeverity.Error => "tone-problem",
        HealthSeverity.Critical => "tone-problem",
    };
}
