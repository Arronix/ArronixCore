using Arronix.Abstractions.Media;

namespace Arronix.Abstractions.Releases;

/// <summary>How a release covers the requested target.</summary>
public enum TargetDisposition
{
    /// <summary>The release does not identify or cannot satisfy the target.</summary>
    Rejected = 0,

    /// <summary>The release covers only part of the requested target.</summary>
    Partial = 1,

    /// <summary>The release covers the target and additional units.</summary>
    Superset = 2,

    /// <summary>The release covers exactly the requested target.</summary>
    Satisfied = 3
}

/// <summary>The typed result of matching one interpreted release to one acquisition target.</summary>
/// <typeparam name="TTarget">The media kind's target type.</typeparam>
/// <param name="Disposition">The overall coverage judgment.</param>
/// <param name="Covered">The target portions the release covers.</param>
/// <param name="Missing">The target portions it does not cover.</param>
/// <param name="Reason">A human-readable reason when useful.</param>
public sealed record TargetMatch<TTarget>(
    TargetDisposition Disposition,
    IReadOnlyList<TTarget> Covered,
    IReadOnlyList<TTarget> Missing,
    string? Reason = null)
    where TTarget : class, IReleaseTarget;
