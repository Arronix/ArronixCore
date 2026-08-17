// The revision axis is an experimental shape contract until 1.0.
#pragma warning disable ARX0013

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Runs the host's kind-agnostic token scans over one release title.
/// </summary>
/// <remarks>
/// A port of the scan halves of Radarr's <c>QualityParser.ParseQualityName</c> and
/// <c>ParseQualityModifiers</c> (<c>src/NzbDrone.Core/Parser/QualityParser.cs:112-122, 739-775</c>) via
/// <c>MovieQualityTokenParser</c>. The decision half — which rung the evidence resolves to — is not here:
/// that is the kind's declared rung table, executed by <see cref="RungResolver"/>.
/// </remarks>
internal static class ReleaseTagScanner
{
    /// <summary>Scans one title.</summary>
    /// <param name="rawTitle">The raw text as it arrived, trimmed.</param>
    /// <param name="normalizedTitle">The working text the pattern list runs against.</param>
    /// <returns>The tag evidence.</returns>
    internal static ScannedReleaseTags Scan(string rawTitle, string normalizedTitle)
    {
        return new ScannedReleaseTags
        {
            SourceGroup = ScanSource(normalizedTitle),
            StatedResolution = ScanResolution(normalizedTitle),
            IsRemux = ReleaseTokenVocabulary.RemuxTag().IsMatch(normalizedTitle),
            VideoCodec = ReleaseCodecScanner.ScanVideoCodec(normalizedTitle),
            AudioCodec = ReleaseCodecScanner.ScanAudioCodec(normalizedTitle),
            Revision = ScanRevision(rawTitle, normalizedTitle),
        };
    }

    /// <summary>
    /// Scans the source group. The RIGHTMOST occurrence in the text wins — a per-release fact
    /// (<c>QualityParser.cs:117-118</c>: <c>SourceRegex.Matches(...).LastOrDefault()</c>) that no fixed
    /// rule-selection mode over an authored table could express, which is why last-ness is a property of
    /// the scan and the rung table consumes the single value the scan produced.
    /// </summary>
    /// <param name="normalizedTitle">The working text.</param>
    /// <returns>The source group token, or null.</returns>
    internal static string? ScanSource(string normalizedTitle)
    {
        var last = ReleaseTokenVocabulary.SourceTags().Matches(normalizedTitle).LastOrDefault();

        if (last is not { Success: true })
        {
            return null;
        }

        foreach (var groupName in ReleaseTokenVocabulary.SourceGroupNames)
        {
            if (last.Groups[groupName].Success)
            {
                return groupName;
            }
        }

        return null;
    }

    /// <summary>Scans the stated resolution, as a pixel height; zero when none was stated.</summary>
    /// <param name="normalizedTitle">The working text.</param>
    /// <returns>The stated pixel height, or zero.</returns>
    internal static int ScanResolution(string normalizedTitle)
    {
        var match = ReleaseTokenVocabulary.ResolutionTags().Match(normalizedTitle);

        if (match.Success)
        {
            if (match.Groups["r360p"].Success)
            {
                return 360;
            }

            if (match.Groups["r480p"].Success)
            {
                return 480;
            }

            if (match.Groups["r540p"].Success)
            {
                return 540;
            }

            if (match.Groups["r576p"].Success)
            {
                return 576;
            }

            if (match.Groups["r720p"].Success)
            {
                return 720;
            }

            if (match.Groups["r1080p"].Success)
            {
                return 1080;
            }

            if (match.Groups["r2160p"].Success)
            {
                return 2160;
            }
        }

        // "UHD" and "[4K]" name a resolution without stating it numerically.
        return ReleaseTokenVocabulary.AlternativeResolutionTags().IsMatch(normalizedTitle) ? 2160 : 0;
    }

    /// <summary>Scans the revision markers.</summary>
    /// <param name="rawTitle">The raw text: REAL is matched case-sensitively against it, because
    /// <c>real</c> in lower case is an ordinary English word.</param>
    /// <param name="normalizedTitle">The working text the other markers scan.</param>
    /// <returns>The revision the title claims.</returns>
    internal static QualityRevision ScanRevision(string rawTitle, string normalizedTitle)
    {
        var version = 1;
        var isRepack = false;

        var explicitVersion = ReleaseTokenVocabulary.VersionTag().Match(normalizedTitle);

        if (explicitVersion.Success)
        {
            version = int.Parse(
                explicitVersion.Groups["version"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        // PROPER and REPACK each add one to whatever version was already stated, so "REPACK2" is version
        // three: it is the second repack of a release that was already at version two.
        if (ReleaseTokenVocabulary.ProperTag().IsMatch(normalizedTitle))
        {
            version = explicitVersion.Success
                ? int.Parse(
                    explicitVersion.Groups["version"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture) + 1
                : 2;
        }

        if (ReleaseTokenVocabulary.RepackTag().IsMatch(normalizedTitle))
        {
            version = explicitVersion.Success
                ? int.Parse(
                    explicitVersion.Groups["version"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture) + 1
                : 2;

            isRepack = true;
        }

        var real = ReleaseTokenVocabulary.RealTag().Matches(rawTitle).Count;

        return new QualityRevision(version, real, isRepack);
    }
}
