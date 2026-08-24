using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Arronix.Abstractions.Media;

namespace Arronix.Format.Video;

/// <summary>The video format-family contribution shared by every video media type.</summary>
/// <remarks>
/// <see cref="Definition"/> is one canonical object, created once and handed to every video media type's
/// constructor. That makes its contents process-global, so the extension vocabulary is wrapped rather than
/// exposed: a collection expression assigned to an <see cref="IReadOnlyList{T}"/> is still an array
/// underneath, and any caller could cast it back and edit the vocabulary every dependant reads. Read-only
/// at the declaration is not read-only at the boundary, and the difference matters exactly here, where one
/// object is shared installation-wide.
/// </remarks>
public static class VideoFormat
{
    private static readonly ReadOnlyCollection<string> Extensions = Array.AsReadOnly(
    [
        ".mkv", ".mp4", ".avi", ".m4v", ".mpg", ".mpeg", ".mov", ".wmv", ".ts", ".m2ts",
        ".webm", ".divx", ".flv", ".vob", ".ogv", ".ogm", ".rmvb", ".m4p", ".mk3d", ".iso",
        ".img"
    ]);

    /// <summary>The family definition, including the extension vocabulary Video owns.</summary>
    public static FormatFamilyDefinition<Video> Definition { get; } = new()
    {
        Id = "video",
        Name = "Video",
        FileExtensions = Extensions
    };
}
