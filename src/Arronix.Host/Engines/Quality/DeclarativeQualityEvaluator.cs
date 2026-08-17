// Consumes the experimental definition (ARX0019) and shape (ARX0013) contracts.
#pragma warning disable ARX0019
#pragma warning disable ARX0013

using System.Globalization;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Parsing;

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// The host's quality evaluator: upgrade and cutoff decisions as a pure function of one kind's declared
/// ladders, behind the stable <see cref="IQualityModel"/> seam, so a declared kind and a hand-written one
/// answer through the same pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The rules are the surveyed ones, stated once: weight first, revision second. An upgrade compares
/// <see cref="QualityTier.EffectiveWeight"/> — two rungs sharing a weight form a quality group and
/// neither upgrades the other — and falls through to the revision axis, because a PROPER of the quality
/// already held is an upgrade while the same quality again is not. A cutoff compares weight alone: a file
/// that is good enough does not become not-good-enough because a corrected issue of it exists; whether
/// that corrected issue is still taken is <see cref="CutoffPolicy.ProperHandling"/>'s question, answered
/// in <see cref="ShouldGrab"/>. Tiers of different declared format families are never compared: nothing
/// is an upgrade across families, and a cross-family cutoff reads mismatch as satisfied.
/// </para>
/// <para>
/// This reproduces the answers the surveyed application gives — including the grouped-rung cutoff the
/// movies review documented as wrongly answered by rank comparison
/// (<c>Arronix.Plugin.Movies/MoviesQualityModel.cs</c>, ported here; Radarr
/// <c>src/NzbDrone.Core/Qualities/QualityModelComparer.cs</c> is the reference behavior): a held rung
/// meets a cutoff set at any other rung of the same weight.
/// </para>
/// </remarks>
internal sealed class DeclarativeQualityEvaluator : IQualityModel
{
    private readonly Dictionary<string, (QualityTier Tier, string FamilyId)> _tiersByName;
    private readonly QualityTier _unknown;

    /// <summary>Initializes a new instance of the <see cref="DeclarativeQualityEvaluator"/> class.</summary>
    /// <param name="shape">The kind's derived structure; the ladders live on it.</param>
    internal DeclarativeQualityEvaluator(MediaShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        MediaKind = shape.Kind;
        _tiersByName = new Dictionary<string, (QualityTier, string)>(StringComparer.OrdinalIgnoreCase);

        if (shape.FormatFamilies.Count == 0)
        {
            throw new ArgumentException(
                "A quality evaluator needs at least one declared format family.", nameof(shape));
        }

        foreach (var family in shape.FormatFamilies)
        {
            foreach (var tier in family.Ladder)
            {
                if (!_tiersByName.TryAdd(tier.Name, (tier, family.FamilyId)))
                {
                    throw new ArgumentException(
                        $"Tier '{tier.Name}' is declared on more than one ladder; a tier name must "
                        + "resolve to exactly one rung.",
                        nameof(shape));
                }
            }

            if (family.Unknown is { } sentinel)
            {
                _tiersByName.TryAdd(sentinel.Name, (sentinel, family.FamilyId));
            }
        }

        // A ladder-derived evaluator over a family that declares no ladder has nothing to evaluate, and
        // there is no honest sentinel to invent: the axis model represents an absent reading as a typed
        // absence, and collapsing that back onto a rung is the mechanism this evaluator is being replaced
        // for. A kind whose families read their files onto axes is served by the axis evaluator instead.
        _unknown = shape.FormatFamilies[0].Unknown
            ?? throw new ArgumentException(
                $"The format family '{shape.FormatFamilies[0].FamilyId}' declares no ladder, so a "
                + "ladder-derived quality evaluator has no rung to fall back to.",
                nameof(shape));
    }

    /// <inheritdoc />
    public MediaKindId MediaKind { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The parse engine resolved the rung; this resolves the rung name to its declared tier and attaches
    /// the revision the parser recorded. A name on no ladder lands on the primary family's unknown tier —
    /// under the declared <see cref="RungFallback.RoundUp"/> mode unrecognized evidence is never read
    /// downward as the worst rung, and the unknown tier is the only honest alternative.
    /// </remarks>
    public QualityTier EvaluateQuality(ParsedRelease parsedRelease)
    {
        ArgumentNullException.ThrowIfNull(parsedRelease);

        var tier = parsedRelease.Quality is { } name && _tiersByName.TryGetValue(name, out var entry)
            ? entry.Tier
            : _unknown;

        return tier with { Revision = ReadRevision(parsedRelease) };
    }

    /// <inheritdoc />
    public bool IsUpgrade(QualityTier currentQuality, QualityTier newQuality)
    {
        ArgumentNullException.ThrowIfNull(currentQuality);
        ArgumentNullException.ThrowIfNull(newQuality);

        if (!SameFamily(currentQuality, newQuality))
        {
            return false;
        }

        var current = currentQuality.EffectiveWeight;
        var candidate = newQuality.EffectiveWeight;

        if (candidate != current)
        {
            return candidate > current;
        }

        return RevisionOf(newQuality).CompareTo(RevisionOf(currentQuality)) > 0;
    }

    /// <inheritdoc />
    public bool MeetsCutoff(QualityTier quality, CutoffPolicy cutoff)
    {
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(cutoff);

        // Declared rule: tiers of different families are never compared, and a cross-family cutoff
        // reads mismatch as satisfied — never as an open-ended search across ladders.
        if (!SameFamily(quality, cutoff.CutoffTier))
        {
            return true;
        }

        return quality.EffectiveWeight >= cutoff.CutoffTier.EffectiveWeight;
    }

    /// <summary>
    /// Whether a candidate is worth grabbing over what is already held, given a cutoff.
    /// </summary>
    /// <param name="currentQuality">The quality already held, or null when nothing is.</param>
    /// <param name="candidateQuality">The candidate's quality.</param>
    /// <param name="cutoff">The cutoff policy in force.</param>
    /// <returns><see langword="true"/> when the candidate should be grabbed.</returns>
    /// <remarks>
    /// The one place the revision axis meets the cutoff: with the cutoff already met, only
    /// <see cref="ProperHandling.PreferProper"/> still takes a corrected issue of the same rung; with it
    /// unmet, <see cref="ProperHandling.IgnoreProper"/> keeps a revision from causing a grab on its own.
    /// </remarks>
    internal bool ShouldGrab(QualityTier? currentQuality, QualityTier candidateQuality, CutoffPolicy cutoff)
    {
        ArgumentNullException.ThrowIfNull(candidateQuality);
        ArgumentNullException.ThrowIfNull(cutoff);

        if (currentQuality is null)
        {
            return true;
        }

        if (MeetsCutoff(currentQuality, cutoff))
        {
            return cutoff.ProperHandling == ProperHandling.PreferProper
                && SameFamily(currentQuality, candidateQuality)
                && candidateQuality.EffectiveWeight == currentQuality.EffectiveWeight
                && RevisionOf(candidateQuality).CompareTo(RevisionOf(currentQuality)) > 0;
        }

        if (cutoff.ProperHandling == ProperHandling.IgnoreProper
            && candidateQuality.EffectiveWeight == currentQuality.EffectiveWeight)
        {
            return false;
        }

        return IsUpgrade(currentQuality, candidateQuality);
    }

    /// <summary>
    /// Whether a file's size is plausible for its claimed rung, from the tier's declared band.
    /// </summary>
    /// <param name="tier">The claimed tier.</param>
    /// <param name="sizeInBytes">The file size.</param>
    /// <param name="runtime">The item's runtime. Unknown disables the check rather than failing it.</param>
    /// <returns><see langword="true"/> when the size is inside the declared band.</returns>
    /// <remarks>
    /// Limits are stated per minute of runtime because a plausible size scales with duration and nothing
    /// else the platform reliably knows. Ported from
    /// <c>Arronix.Plugin.Movies/MoviesQualityModel.cs</c> (<c>MoviesQualityLadder.IsPlausibleSize</c>),
    /// itself Radarr's <c>QualityDefinition</c> size gate; the declared band moved onto the tier's typed
    /// slots (review A4/A5), so one engine rule now serves every kind.
    /// </remarks>
    internal static bool IsPlausibleSize(QualityTier tier, long sizeInBytes, TimeSpan runtime)
    {
        ArgumentNullException.ThrowIfNull(tier);

        if (sizeInBytes <= 0 || runtime <= TimeSpan.Zero)
        {
            return true;
        }

        var megabytesPerMinute = sizeInBytes / 1048576d / runtime.TotalMinutes;

        if (tier.MinSizeMbPerMinute is { } floor && megabytesPerMinute < floor)
        {
            return false;
        }

        return tier.MaxSizeMbPerMinute is not { } ceiling || megabytesPerMinute <= ceiling;
    }

    /// <summary>Reads the revision the parse engine recorded in a release's metadata.</summary>
    /// <param name="parsedRelease">The parsed release.</param>
    /// <returns>The revision, or the initial one when none was recorded.</returns>
    internal static QualityRevision ReadRevision(ParsedRelease parsedRelease)
    {
        ArgumentNullException.ThrowIfNull(parsedRelease);

        if (parsedRelease.AdditionalMetadata is not { } metadata)
        {
            return QualityRevision.Initial;
        }

        var version = ReadInt(metadata, DeclarativeParseFields.RevisionVersion, 1);
        var real = ReadInt(metadata, DeclarativeParseFields.RevisionReal, 0);
        var isRepack = metadata.TryGetValue(DeclarativeParseFields.RevisionIsRepack, out var repack)
            && string.Equals(repack, "true", StringComparison.OrdinalIgnoreCase);

        return new QualityRevision(version, real, isRepack);
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> metadata, string key, int fallback) =>
        metadata.TryGetValue(key, out var text)
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static QualityRevision RevisionOf(QualityTier tier) => tier.Revision ?? QualityRevision.Initial;

    private bool SameFamily(QualityTier left, QualityTier right)
    {
        // Family membership is by declared rung name. A tier the declaration does not know cannot be
        // placed in any family, and an unplaceable tier is compared by weight alone rather than being
        // silently excluded.
        if (_tiersByName.TryGetValue(left.Name, out var leftEntry)
            && _tiersByName.TryGetValue(right.Name, out var rightEntry))
        {
            return string.Equals(leftEntry.FamilyId, rightEntry.FamilyId, StringComparison.Ordinal);
        }

        return true;
    }
}
