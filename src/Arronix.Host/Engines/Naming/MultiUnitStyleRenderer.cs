// The declaration contracts are experimental until 1.0.
#pragma warning disable ARX0019

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Definition;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// Renders the ordinals of several units in one name, in a declared style.
/// </summary>
/// <remarks>
/// Executes <see cref="MultiUnitStyle"/> rows — the four-property data form of the surveyed six-value
/// multi-episode enum. The semantics are ported from the imperative implementation this engine deletes,
/// <c>Arronix.Plugin.Tv/TvNaming.cs</c> (<c>TvNameFormatter.RenderInnerOrdinals</c>:129-165), itself the
/// data reduction of Sonarr's <c>MultiEpisodeStyle</c> dispatch
/// (<c>src/NzbDrone.Core/Organizer/FileNameBuilder.cs:480,549,1260-1267</c>):
/// <c>Joiner</c> joins consecutive ordinals; <c>RepeatPrefix</c> is restated before each further
/// ordinal; <c>RangeOnly</c> collapses the run to first-through-last; <c>RestateOuter</c> restates the
/// outer coordinate per unit rather than once.
/// </remarks>
internal static class MultiUnitStyleRenderer
{
    /// <summary>
    /// Renders inner ordinals in a declared style.
    /// </summary>
    /// <param name="style">The style row.</param>
    /// <param name="ordinals">The units' inner ordinals, in canonical order.</param>
    /// <param name="outer">The rendered outer coordinate, restated when the style says so.</param>
    /// <param name="padding">The zero-padding width.</param>
    /// <returns>The joined ordinal text, empty for no units.</returns>
    public static string Render(
        MultiUnitStyle style,
        IReadOnlyList<long> ordinals,
        string outer,
        int padding)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(ordinals);

        if (ordinals.Count == 0)
        {
            return string.Empty;
        }

        var format = new string('0', Math.Max(1, padding));

        string Pad(long value) => value.ToString(format, CultureInfo.InvariantCulture);

        if (ordinals.Count == 1)
        {
            return Pad(ordinals[0]);
        }

        if (style.RangeOnly)
        {
            return $"{Pad(ordinals[0])}{style.Joiner}{style.RepeatPrefix}{Pad(ordinals[^1])}";
        }

        return string.Join(
            string.Empty,
            ordinals.Select((ordinal, index) =>
            {
                if (index == 0)
                {
                    return Pad(ordinal);
                }

                var restated = style.RestateOuter ? outer : string.Empty;

                return $"{style.Joiner}{restated}{style.RepeatPrefix}{Pad(ordinal)}";
            }));
    }
}
