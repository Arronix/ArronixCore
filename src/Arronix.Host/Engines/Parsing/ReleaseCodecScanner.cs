namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Reads the video and audio codec tokens a release title claims.
/// </summary>
/// <remarks>
/// A port of <c>Arronix.Plugin.Movies/MoviesReleaseParser.cs</c> (<c>MovieCodecReader</c>), itself a
/// collapse of Radarr's codec regexes (<c>QualityParser.cs:66-67</c> and the media-info token tables):
/// two token-to-value tables and a boundary-scan rule. First row that appears in the text wins, so the
/// table order encodes token precedence — <c>x265</c> spellings before <c>x264</c>, lossless audio
/// before lossy.
/// </remarks>
internal static class ReleaseCodecScanner
{
    private static readonly (string Token, string Value)[] VideoCodecs =
    [
        ("x265", "x265"), ("h265", "x265"), ("h.265", "x265"), ("hevc", "x265"),
        ("x264", "x264"), ("h264", "x264"), ("h.264", "x264"), ("avc", "x264"),
        ("xvid", "xvid"), ("divx", "divx"), ("av1", "av1"), ("vc1", "vc1"), ("mpeg2", "mpeg2")
    ];

    private static readonly (string Token, string Value)[] AudioCodecs =
    [
        ("truehd", "TrueHD"), ("dts-hd", "DTS-HD"), ("dtshd", "DTS-HD"), ("dts-x", "DTS-X"),
        ("dts-ma", "DTS-HD"), ("atmos", "Atmos"), ("eac3", "EAC3"), ("ddp", "EAC3"), ("dd+", "EAC3"),
        ("dts", "DTS"), ("ac3", "AC3"), ("dd5", "AC3"), ("flac", "FLAC"), ("opus", "Opus"),
        ("aac", "AAC"), ("mp3", "MP3"), ("lpcm", "PCM"), ("pcm", "PCM")
    ];

    /// <summary>Reads the video codec a title claims.</summary>
    /// <param name="title">The release title.</param>
    /// <returns>The codec token, or null.</returns>
    internal static string? ScanVideoCodec(string? title) => FirstToken(title, VideoCodecs);

    /// <summary>Reads the audio codec a title claims.</summary>
    /// <param name="title">The release title.</param>
    /// <returns>The codec token, or null.</returns>
    internal static string? ScanAudioCodec(string? title) => FirstToken(title, AudioCodecs);

    private static string? FirstToken(string? title, (string Token, string Value)[] table)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var haystack = title.Replace('_', ' ').Replace('.', ' ');

        foreach (var (token, value) in table)
        {
            if (ContainsToken(haystack, token))
            {
                return value;
            }
        }

        return null;
    }

    private static bool ContainsToken(string haystack, string token)
    {
        var index = haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var beforeIsBoundary = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var after = index + token.Length;
            var afterIsBoundary = after >= haystack.Length || !char.IsLetterOrDigit(haystack[after]);

            if (beforeIsBoundary && afterIsBoundary)
            {
                return true;
            }

            index = haystack.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
