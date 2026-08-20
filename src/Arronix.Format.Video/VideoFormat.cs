
using Arronix.Abstractions.Media;

namespace Arronix.Format.Video;

/// <summary>The video format-family contribution shared by every video media type.</summary>
public static class VideoFormat
{
    /// <summary>The family definition, including the extension vocabulary Video owns.</summary>
    public static FormatFamilyDefinition<Video> Definition { get; } = new()
    {
        Id = "video",
        Name = "Video",
        FileExtensions =
        [
            ".mkv", ".mp4", ".avi", ".m4v", ".mpg", ".mpeg", ".mov", ".wmv", ".ts", ".m2ts",
            ".webm", ".divx", ".flv", ".vob", ".ogv", ".ogm", ".rmvb", ".m4p", ".mk3d", ".iso",
            ".img"
        ]
    };
}
