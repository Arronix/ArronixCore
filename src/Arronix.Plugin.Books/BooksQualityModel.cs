// The media-shape contracts are experimental; the format families and their ladders live on the shape.
#pragma warning disable ARX0013
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Books;

/// <summary>
/// Ranks copies of a manifestation against one another, within a family.
/// </summary>
/// <remarks>
/// <para>
/// <b>Within a family</b> is the whole of the design. A written copy and a spoken copy are not comparable,
/// and the model refuses to compare them: <see cref="IsUpgrade"/> returns false across families rather
/// than answering a question that has no answer. That is the direct fix for the failure the surveyed
/// original lives with, where one ordered ladder carrying both flavors makes any spoken copy an upgrade
/// over every written one.
/// </para>
/// <para>
/// The honest limit is recorded here rather than hidden: the shape declares two ladders and therefore
/// admits two ceilings, but the platform's cutoff contract carries one ceiling per call with no family on
/// it. Until a per-family profile exists, the ceiling a caller passes is applied to whichever family the
/// copy belongs to, and a caller that wants two ceilings has to hold two profiles.
/// </para>
/// </remarks>
public sealed class BooksQualityModel : IQualityModel
{
    /// <inheritdoc />
    public MediaKindId MediaKind => BooksShape.Kind;

    /// <summary>Gets the written family's declaration.</summary>
    public static FormatFamily Written { get; } =
        BooksShape.Declaration.FormatFamilies.First(
            family => string.Equals(family.FamilyId, BooksShape.WrittenFamilyId, StringComparison.Ordinal));

    /// <summary>Gets the spoken family's declaration.</summary>
    public static FormatFamily Spoken { get; } =
        BooksShape.Declaration.FormatFamilies.First(
            family => string.Equals(family.FamilyId, BooksShape.SpokenFamilyId, StringComparison.Ordinal));

    /// <inheritdoc />
    public QualityTier EvaluateQuality(ParsedRelease parsedRelease)
    {
        ArgumentNullException.ThrowIfNull(parsedRelease);

        var declared = parsedRelease.Quality;

        if (declared is not null)
        {
            var match = Written.Ladder
                .Concat(Spoken.Ladder)
                .FirstOrDefault(tier => string.Equals(tier.Name, declared, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        // A release name that says nothing recognizable is unknown in the family the codec implies, and
        // unknown-text when even that is silent. Two sentinels, one per family, is the point.
        return string.Equals(parsedRelease.Codec, "AUDIO", StringComparison.Ordinal)
            ? Spoken.Unknown
            : Written.Unknown;
    }

    /// <inheritdoc />
    public bool IsUpgrade(QualityTier currentQuality, QualityTier newQuality)
    {
        ArgumentNullException.ThrowIfNull(currentQuality);
        ArgumentNullException.ThrowIfNull(newQuality);

        var currentFamily = FamilyOf(currentQuality);
        var newFamily = FamilyOf(newQuality);

        // Across families the question is not "is this better", it is "is this a different thing". The
        // model declines to answer rather than answering wrongly.
        if (currentFamily is null || newFamily is null
            || !string.Equals(currentFamily, newFamily, StringComparison.Ordinal))
        {
            return false;
        }

        // Within a family, effective weight rather than rank: neither ladder declares a quality group
        // today, so the answers are unchanged, but the typed slot is the comparison the contract defines.
        return newQuality.CompareTo(currentQuality) > 0;
    }

    /// <inheritdoc />
    public bool MeetsCutoff(QualityTier quality, CutoffPolicy cutoff)
    {
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(cutoff);

        var qualityFamily = FamilyOf(quality);
        var cutoffFamily = FamilyOf(cutoff.CutoffTier);

        // A ceiling expressed in one family says nothing about the other. Treating a mismatched ceiling as
        // satisfied is the only safe reading: it stops a text ceiling silently condemning every spoken
        // copy to be re-acquired forever.
        if (qualityFamily is null || cutoffFamily is null
            || !string.Equals(qualityFamily, cutoffFamily, StringComparison.Ordinal))
        {
            return true;
        }

        return cutoff.MeetsCutoff(quality);
    }

    /// <summary>
    /// Returns which family a tier belongs to.
    /// </summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The family identifier, or <see langword="null"/> when no family claims it.</returns>
    public static string? FamilyOf(QualityTier tier)
    {
        ArgumentNullException.ThrowIfNull(tier);

        if (Written.Ladder.Any(candidate => Same(candidate, tier))
            || Same(Written.Unknown, tier))
        {
            return BooksShape.WrittenFamilyId;
        }

        if (Spoken.Ladder.Any(candidate => Same(candidate, tier))
            || Same(Spoken.Unknown, tier))
        {
            return BooksShape.SpokenFamilyId;
        }

        return null;
    }

    /// <summary>
    /// Returns which family claims a file, by its extension.
    /// </summary>
    /// <param name="path">The file's path or name.</param>
    /// <returns>The family identifier, or <see langword="null"/> when no family claims it.</returns>
    /// <remarks>
    /// The extension sets are the shape's declared discriminator and are validated pairwise disjoint, so
    /// at most one family can answer. That is what makes this a lookup rather than a precedence rule.
    /// </remarks>
    public static string? FamilyForExtension(string path)
    {
        var extension = ExtensionOf(path);

        if (extension is null)
        {
            return null;
        }

        foreach (var family in BooksShape.Declaration.FormatFamilies)
        {
            if (family.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return family.FamilyId;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the tier a file's extension alone implies.
    /// </summary>
    /// <param name="path">The file's path or name.</param>
    /// <returns>The implied tier, or the unknown tier of whichever family claims the extension.</returns>
    public static QualityTier TierForExtension(string path)
    {
        var extension = ExtensionOf(path);

        if (extension is null)
        {
            return Written.Unknown;
        }

        var byName = extension.TrimStart('.').ToUpperInvariant() switch
        {
            "EPUB" => "EPUB",
            "AZW3" or "AZW" => "AZW3",
            "MOBI" => "MOBI",
            "PDF" => "PDF",
            "M4B" or "AAX" or "AA" => "M4B",
            "M4A" or "AAC" => "AAC",
            "MP3" => "MP3",
            "FLAC" => "FLAC",
            _ => null,
        };

        if (byName is null)
        {
            return FamilyForExtension(path) == BooksShape.SpokenFamilyId
                ? Spoken.Unknown
                : Written.Unknown;
        }

        return Written.Ladder.Concat(Spoken.Ladder)
            .First(tier => string.Equals(tier.Name, byName, StringComparison.Ordinal));
    }

    private static bool Same(QualityTier left, QualityTier right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) && left.Rank == right.Rank;

    private static string? ExtensionOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var dot = path.LastIndexOf('.');

        return dot < 0 || dot == path.Length - 1 ? null : path[dot..];
    }
}
