using Arronix.Abstractions.Quality;

// Quality contracts are experimental; producing the evidence record is what this scanner is for.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Turns a release title, a file name and a probe into typed evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the parse boundary.</b> Strings go in and typed evidence comes out; nothing here names a
/// rung, ranks anything, or decides what any of it is worth. Every scanner it composes reports what a
/// title <i>stated</i>, and the readings that turn statements into axis values live behind the contract
/// seam, where a format family owns them.
/// </para>
/// <para>
/// It is host-owned and knows no media kind. Every member it fills is the same question for every kind of
/// media: what raster, what codec, what signal, which language, which issue. The per-kind residue —
/// bracket conventions, a scene's local dialect, a kind's own guard set — is passed through untouched, so
/// that a kind can contribute to its own family's axes without any of its identifier strings reaching
/// this assembly or the one above it.
/// </para>
/// </remarks>
public static class ReleaseEvidenceScanner
{
    /// <summary>
    /// Scans one release.
    /// </summary>
    /// <param name="request">What is known about it.</param>
    /// <returns>The evidence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static ReleaseEvidence Scan(EvidenceScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stream = EvidenceLexer.Tokenize(request.Title);

        var raster = EvidenceResolutionScanner.Read(stream);
        var container = EvidencePackagingScanner.ReadContainer(stream, request.FileName);
        var revision = EvidenceRevisionScanner.Read(stream);
        var dynamicRange = EvidenceDynamicRangeScanner.Read(stream);

        return new ReleaseEvidence
        {
            Title = request.Title,
            SourceToken = EvidenceSourceScanner.Read(stream),
            StatedResolution = raster.IsStated ? raster.Lines : null,
            StatedResolutionForm = raster.Form,
            ScanType = raster.Scan,
            VideoCodecToken = EvidenceCodecScanner.ReadVideoCodec(stream),
            AudioToken = EvidenceCodecScanner.ReadAudioFormat(stream),
            DynamicRangeTokens = dynamicRange,
            IsRemux = EvidenceSourceScanner.ReadBitstreamClaim(stream),
            Version = revision.Issue,
            RealCount = revision.Mislabels,
            IsRepack = revision.IsRepack,
            ReleaseGroup = request.ReleaseGroup,
            DistributorToken = EvidenceDistributorScanner.Read(stream),
            Languages = EvidenceLanguageScanner.Read(stream),
            PackagingToken = EvidencePackagingScanner.Read(stream, container),
            FlawTokens = EvidenceFlawScanner.Read(stream),
            StatedFrameRate = EvidenceCodecScanner.ReadFrameRate(stream),
            Container = container,
            Guards = request.Guards ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Tags = request.Tags ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Probe = request.Probe,
        };
    }

}

/// <summary>
/// What is known about one release before anything is read from it.
/// </summary>
/// <remarks>
/// The per-kind members are passed straight through. A kind's guards and captured tags are its own
/// strings, and the scan neither reads them nor knows what any of them mean — carrying them is what lets
/// a kind refine its family's axes later without the strings ever crossing into a contract.
/// </remarks>
public sealed record EvidenceScanRequest
{
    /// <summary>Gets the release title as it arrived.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the file's name, when the file is on disk.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets the release group, as the group scan reported it.</summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>Gets the per-kind guards that matched.</summary>
    public IReadOnlySet<string>? Guards { get; init; }

    /// <summary>Gets the per-kind tags the kind's own patterns captured.</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>Gets what a container or stream probe measured.</summary>
    public MediaProbe? Probe { get; init; }
}
