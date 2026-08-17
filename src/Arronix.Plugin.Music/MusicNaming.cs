// The file-facts record is an experimental shape contract; the rename seam carries it.
#pragma warning disable ARX0013

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Music.Seed;

namespace Arronix.Plugin.Music;

/// <summary>
/// Derives file names for recordings from a template.
/// </summary>
/// <remarks>
/// <para>
/// Two things make music's naming distinctive, and both are visible here. The first is that the template
/// applied depends on the item: a work pressed across several carriers wants the carrier number in the
/// name and a single-carrier one does not, so the extension chooses <em>which</em> template to apply
/// rather than only substituting into a fixed one.
/// </para>
/// <para>
/// The second is that the credited performer of a recording need not be the performer of the work above
/// it. Where they disagree the recording's own credit wins, which is the whole of what makes compilations
/// name correctly.
/// </para>
/// </remarks>
public sealed class MusicRenamePolicy : IRenamePolicy
{
    /// <summary>The template used when a pressing occupies a single carrier.</summary>
    public const string SingleCarrierTemplate =
        "{Track Number} - {Track Title}";

    /// <summary>The template used when a pressing spans several carriers.</summary>
    public const string MultiCarrierTemplate =
        "{Medium Number}-{Track Number} - {Track Title}";

    /// <inheritdoc />
    public MediaKindId MediaKind => MusicShape.Kind;

    /// <inheritdoc />
    /// <remarks>
    /// The declared token vocabulary carries no file-fact token yet — every music token resolves from the
    /// item's catalog record — so <paramref name="file"/> contributes nothing to the rendered name until
    /// one is declared.
    /// </remarks>
    public Task<string> GenerateFileNameAsync(
        MediaItemId itemId,
        MediaFileFacts? file,
        string namingTemplate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namingTemplate);
        cancellationToken.ThrowIfCancellationRequested();

        var recording = MusicSeed.Recordings.FirstOrDefault(
            candidate => candidate.Id == itemId.Value);

        if (recording is null)
        {
            return Task.FromResult(string.Empty);
        }

        var tokens = ResolveFor(recording);
        var name = namingTemplate;

        foreach (var token in tokens)
        {
            name = name.Replace(token.Key, token.Value, StringComparison.Ordinal);
        }

        return Task.FromResult(Sanitize(name));
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ResolveTokensAsync(
        MediaItemId itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var recording = MusicSeed.Recordings.FirstOrDefault(
            candidate => candidate.Id == itemId.Value);

        return Task.FromResult<IReadOnlyDictionary<string, string>>(
            recording is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ResolveFor(recording));
    }

    /// <inheritdoc />
    public bool ValidateTemplate(string namingTemplate)
    {
        if (string.IsNullOrWhiteSpace(namingTemplate))
        {
            return false;
        }

        var declared = MusicShape.Declaration.Tokens
            .Select(token => token.Name)
            .ToHashSet(StringComparer.Ordinal);

        var index = 0;

        while (index < namingTemplate.Length)
        {
            var open = namingTemplate.IndexOf('{', index);
            if (open < 0)
            {
                return true;
            }

            var close = namingTemplate.IndexOf('}', open);
            if (close < 0)
            {
                return false;
            }

            if (!declared.Contains(namingTemplate[open..(close + 1)]))
            {
                return false;
            }

            index = close + 1;
        }

        return true;
    }

    /// <summary>
    /// Returns the template appropriate to one pressing.
    /// </summary>
    /// <param name="pressingId">The pressing.</param>
    /// <returns>The template to apply.</returns>
    /// <remarks>
    /// Template <em>selection</em>, not only token substitution. A single naming string per media kind
    /// cannot express this, and every surveyed music application ended up needing it.
    /// </remarks>
    public static string TemplateForPressing(int pressingId)
    {
        var recordings = MusicSeed.RecordingsOf(pressingId);
        var carriers = recordings.Count == 0
            ? 1
            : recordings.Max(recording => recording.Carrier);

        return carriers > 1 ? MultiCarrierTemplate : SingleCarrierTemplate;
    }

    private static Dictionary<string, string> ResolveFor(SeedRecording recording)
    {
        var pressing = MusicSeed.Pressings.First(candidate => candidate.Id == recording.PressingId);
        var work = MusicSeed.Works.First(candidate => candidate.Id == pressing.WorkId);
        var performer = MusicSeed.Performers.First(candidate => candidate.Id == work.PerformerId);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{Artist Name}"] = performer.Name,
            ["{Artist Sort Name}"] = performer.SortName,
            ["{Album Title}"] = work.Title,
            ["{Album Type}"] = work.PrimaryKind,
            ["{Release Year}"] = work.ReleaseDate.Year.ToString(CultureInfo.InvariantCulture),
            ["{Medium Number}"] = recording.Carrier.ToString(CultureInfo.InvariantCulture),
            ["{Medium Format}"] = pressing.CarrierFormat,
            ["{Track Number}"] = recording.Position.ToString("D2", CultureInfo.InvariantCulture),
            ["{Track Title}"] = recording.Title,

            // The credit of the recording wins over the credit of the work when they disagree.
            ["{Track Artist}"] = recording.Performer ?? performer.Name,
        };
    }

    private static string Sanitize(string value)
    {
        var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
        var buffer = new char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            buffer[length++] = invalid.Contains(character) ? '+' : character;
        }

        return new string(buffer, 0, length).Trim();
    }
}

/// <summary>
/// Decides where in a library a work's files live.
/// </summary>
/// <remarks>
/// The folder spine follows the containment hierarchy down to the acquisition level and stops there.
/// Below it lies the running order, which is a coordinate rather than a hierarchy, so it becomes part of
/// the file name and never a folder of its own. That is why the pressing - the level between them - never
/// appears in a path: the library holds one selected pressing at a time and putting it in the path would
/// mean moving every file whenever the selection changed.
/// </remarks>
public sealed class MusicLibraryLayout : ILibraryLayout
{
    /// <summary>The folder template applied when none is configured.</summary>
    public const string DefaultFolderTemplate = "{Artist Name}/{Album Title} ({Release Year})";

    /// <inheritdoc />
    public MediaKindId MediaKind => MusicShape.Kind;

    /// <inheritdoc />
    public Task<string> GenerateFolderPathAsync(
        MediaItemId itemId,
        LibraryPathSpec pathSpec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathSpec);
        cancellationToken.ThrowIfCancellationRequested();

        var work = MusicSeed.Works.FirstOrDefault(candidate => candidate.Id == itemId.Value);

        if (work is null)
        {
            return Task.FromResult(pathSpec.RootPath);
        }

        var performer = MusicSeed.Performers.First(candidate => candidate.Id == work.PerformerId);

        var template = string.IsNullOrWhiteSpace(pathSpec.FolderTemplate)
            ? DefaultFolderTemplate
            : pathSpec.FolderTemplate;

        var resolved = template
            .Replace("{Artist Name}", performer.Name, StringComparison.Ordinal)
            .Replace("{Artist Sort Name}", performer.SortName, StringComparison.Ordinal)
            .Replace("{Album Title}", work.Title, StringComparison.Ordinal)
            .Replace("{Album Type}", work.PrimaryKind, StringComparison.Ordinal)
            .Replace(
                "{Release Year}",
                work.ReleaseDate.Year.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        if (pathSpec.CustomTokens is { } custom)
        {
            foreach (var token in custom)
            {
                resolved = resolved.Replace(token.Key, token.Value, StringComparison.Ordinal);
            }
        }

        var segments = resolved.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return Task.FromResult(
            pathSpec.CreateSubfolders
                ? System.IO.Path.Combine([pathSpec.RootPath, .. segments])
                : System.IO.Path.Combine(pathSpec.RootPath, string.Join(" - ", segments)));
    }

    /// <inheritdoc />
    public bool ValidatePathSpec(LibraryPathSpec pathSpec)
    {
        ArgumentNullException.ThrowIfNull(pathSpec);

        return !string.IsNullOrWhiteSpace(pathSpec.RootPath)
            && !string.IsNullOrWhiteSpace(pathSpec.FolderTemplate)
            && pathSpec.FolderTemplate.Contains("{Album Title}", StringComparison.Ordinal);
    }
}
