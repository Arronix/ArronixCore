namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads the defects a release states it carries.
/// </summary>
/// <remarks>
/// <para>
/// A defect is deliberately kept apart from the signal a file descends from. A disc screener really is a
/// high-fidelity signal with a distribution mark burned into it, and saying so is more useful than
/// pretending it is worse than a broadcast capture; a picture enlarged from a smaller raster really is
/// the raster it claims, with an enlargement on top. Both statements survive here as two facts instead of
/// being folded into one worse name.
/// </para>
/// <para>
/// <b>Two members of the vocabulary are declared and never scanned, and that is reported rather than
/// hidden.</b> <c>network-logo</c> has no reliable spelling — a broadcast capture with a station mark
/// burned in does not say so, and inferring it from the source would be an inference, not a reading. The
/// same is true of interlacing, which is not scanned here at all: it arrives as the scan type on the
/// raster claim, where it was actually stated.
/// </para>
/// </remarks>
internal static class EvidenceFlawScanner
{
    /// <summary>
    /// Reads the defect markers.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The normalized markers, without repeats.</returns>
    internal static IReadOnlySet<string> Read(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in stream.OfClass(EvidenceTokenClass.Flaw))
        {
            markers.Add(token.Value);
        }

        return markers;
    }
}
