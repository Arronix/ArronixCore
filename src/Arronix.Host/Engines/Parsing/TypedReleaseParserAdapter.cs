using System.Globalization;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Providers;


namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// Projects the typed static parser onto the temporary non-generic parser used by unconverted consumers.
/// </summary>
internal sealed class TypedReleaseParserAdapter(
    IMediaTypeRuntime mediaType,
    ProviderRegistry providers) : IReleaseParser
{
    public Abstractions.Identity.MediaKindId MediaKind => mediaType.Kind;

    public ParsedRelease? Parse(string releaseTitle)
    {
        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            return null;
        }

        var ids = providers.ReadExternalIds(mediaType.Kind, releaseTitle);
        var release = mediaType.Parse(new ReleaseParseContext
        {
            Text = releaseTitle,
            Source = MatchSource.ReleaseName,
            ExternalIds = ids
        });

        if (release is null)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reading in ids)
        {
            metadata.TryAdd(DeclarativeParseFields.ExternalIdPrefix + reading.Id.Scheme, reading.Id.Value);
        }

        if (!string.IsNullOrWhiteSpace(release.Edition))
        {
            metadata[DeclarativeParseFields.TagPrefix + "edition"] = release.Edition;
        }

        return new ParsedRelease(
            MediaKind,
            release.Title,
            release.Year?.ToString(CultureInfo.InvariantCulture),
            AdditionalMetadata: metadata.Count == 0 ? null : metadata);
    }

    public bool CanParse(string releaseTitle) => Parse(releaseTitle) is not null;
}
