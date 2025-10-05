using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IVideoFileInfoReader
    {
        MediaInfoModel GetMediaInfo(string filename);
        TimeSpan? GetRunTime(string filename);
    }

    // TODO: This is a stubbed implementation. HDR/Dolby Vision detection and advanced
    // video metadata extraction will be provided by format support extensions in the future.
    // For now, this returns basic placeholder information.
    public class VideoFileInfoReader : IVideoFileInfoReader
    {
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public const int MINIMUM_MEDIA_INFO_SCHEMA_REVISION = 12;
        public const int CURRENT_MEDIA_INFO_SCHEMA_REVISION = 12;

        public VideoFileInfoReader(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public MediaInfoModel GetMediaInfo(string filename)
        {
            if (!_diskProvider.FileExists(filename))
            {
                throw new FileNotFoundException("Media file does not exist: " + filename);
            }

            if (MediaFileExtensions.DiskExtensions.Contains(Path.GetExtension(filename)))
            {
                return null;
            }

            try
            {
                _logger.Debug("Getting media info from {0} (stubbed implementation)", filename);

                // TODO: Implement actual media info extraction via format support extensions
                // For now, return basic placeholder information
                var mediaInfoModel = new MediaInfoModel
                {
                    SchemaRevision = CURRENT_MEDIA_INFO_SCHEMA_REVISION,
                    ContainerFormat = "unknown",
                    VideoFormat = "unknown",
                    VideoBitDepth = 8,
                    VideoHdrFormat = HdrFormat.None,
                    Height = 0,
                    Width = 0,
                    AudioStreamCount = 0,
                    AudioChannels = 0,
                    VideoFps = 0,
                    AudioLanguages = new List<string>(),
                    Subtitles = new List<string>(),
                    ScanType = "Progressive",
                    RunTime = TimeSpan.Zero
                };

                return mediaInfoModel;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to parse media info from file: {0}", filename);
            }

            return null;
        }

        public TimeSpan? GetRunTime(string filename)
        {
            // TODO: Implement via format support extension
            return TimeSpan.Zero;
        }

        // Temporary HDR format inference stub to satisfy legacy migration logic.
        // The original implementation used MediaInfo fields plus potential HDR metadata.
        // Since HDR analysis is deferred to future extensions, we perform very light heuristics
        // and otherwise return HdrFormat.None to avoid misleading data.
        public static HdrFormat GetHdrFormat(int bitDepth, string colourPrimaries, string transferCharacteristics, object _)
        {
            if (bitDepth >= 10)
            {
                var primaries = (colourPrimaries ?? string.Empty).ToLowerInvariant();
                var transfer = (transferCharacteristics ?? string.Empty).ToLowerInvariant();

                // Basic heuristics: detect HDR10/PQ vs HLG. (Dolby Vision requires dedicated metadata not present here.)
                if (transfer.Contains("2084") || transfer.Contains("pq"))
                {
                    return HdrFormat.Hdr10; // Treat any PQ-based transfer as HDR10 for placeholder purposes
                }

                if (transfer.Contains("hlg") || transfer.Contains("arib-std-b67"))
                {
                    return HdrFormat.Hlg10;
                }

                // If we know it's wide gamut BT.2020 but no transfer match, mark unknown HDR rather than None.
                if (primaries.Contains("2020"))
                {
                    return HdrFormat.UnknownHdr;
                }
            }

            return HdrFormat.None;
        }
    }
}
