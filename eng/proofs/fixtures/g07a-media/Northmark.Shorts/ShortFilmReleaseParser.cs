using System;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Format.Video;

namespace Northmark.Shorts.Extension;

/// <summary>Interprets a short-film release name.</summary>
/// <remarks>Reads the title and a trailing year. Representation facts belong to the video format.</remarks>
public sealed class ShortFilmReleaseParser : IReleaseParser<Release<Video>>
{
    /// <inheritdoc />
    public static ReleaseParseResult<Release<Video>> Parse(ReleaseParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var text = context.Text.Replace('.', ' ').Trim();

        if (text.Length == 0)
        {
            return ReleaseParseResult<Release<Video>>.Rejected("The release states no title.");
        }

        var separator = text.LastIndexOf(' ');

        return separator > 0
            && int.TryParse(text.AsSpan(separator + 1), out var year)
            && year is >= 1888 and <= 2999
            ? ReleaseParseResult<Release<Video>>.Accepted(
                new Release<Video>(text[..separator].Trim(), year),
                context.ExternalIds)
            : ReleaseParseResult<Release<Video>>.Accepted(
                new Release<Video>(text, null),
                context.ExternalIds);
    }
}
