
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Books.Seed;

namespace Arronix.Plugin.Books;

/// <summary>
/// Derives file names for manifestations from a template.
/// </summary>
/// <remarks>
/// <para>
/// Two dates, not one: the year the work was first published and the year this manifestation was issued
/// are both meaningful and frequently decades apart. A kind that fuses work and manifestation has only
/// one; a kind that separates them has to name both, and the token set says so.
/// </para>
/// <para>
/// The template also depends on the item, as it does for recorded music but for a different reason: a
/// manifestation spread over parts needs the part number in the name and a single-file one does not.
/// </para>
/// </remarks>
public sealed class BooksRenamePolicy : IRenamePolicy
{
    /// <summary>The template used when a manifestation is a single file.</summary>
    public const string SinglePartTemplate = "{Author Name} - {Book Title}";

    /// <summary>The template used when a manifestation is spread over parts.</summary>
    public const string MultiPartTemplate = "{Author Name} - {Book Title} - {Part Number}";

    /// <summary>The template used when the work belongs to a collection at a labeled position.</summary>
    public const string CollectedTemplate =
        "{Author Name} - {Book Series} {Book SeriesPosition} - {Book Title}";

    /// <inheritdoc />
    public MediaKindId MediaKind => BooksShape.Kind;

    /// <inheritdoc />
    /// <remarks>
    /// The declared token vocabulary carries no file-fact token — even <c>{Part Number}</c> is fixed at
    /// one here because which part a file carries is not a member of the file-facts record — so
    /// <paramref name="file"/> contributes nothing to the rendered name until one is declared.
    /// </remarks>
    public Task<string> GenerateFileNameAsync(
        MediaItemId itemId,
        MediaFileFacts? file,
        string namingTemplate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namingTemplate);
        cancellationToken.ThrowIfCancellationRequested();

        var manifestation = BooksSeed.Manifestations.FirstOrDefault(
            candidate => candidate.Id == itemId.Value);

        if (manifestation is null)
        {
            return Task.FromResult(string.Empty);
        }

        var name = namingTemplate;

        foreach (var token in ResolveFor(manifestation, part: 1))
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

        var manifestation = BooksSeed.Manifestations.FirstOrDefault(
            candidate => candidate.Id == itemId.Value);

        return Task.FromResult<IReadOnlyDictionary<string, string>>(
            manifestation is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ResolveFor(manifestation, part: 1));
    }

    /// <inheritdoc />
    public bool ValidateTemplate(string namingTemplate)
    {
        if (string.IsNullOrWhiteSpace(namingTemplate))
        {
            return false;
        }

        var declared = BooksShape.Declaration.Tokens
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

            if (close < 0 || !declared.Contains(namingTemplate[open..(close + 1)]))
            {
                return false;
            }

            index = close + 1;
        }

        return true;
    }

    /// <summary>
    /// Returns the template appropriate to one manifestation.
    /// </summary>
    /// <param name="manifestationId">The manifestation.</param>
    /// <returns>The template to apply.</returns>
    public static string TemplateForManifestation(int manifestationId)
    {
        var manifestation = BooksSeed.Manifestations.FirstOrDefault(
            candidate => candidate.Id == manifestationId);

        if (manifestation is null)
        {
            return SinglePartTemplate;
        }

        if (manifestation.PartCount > 1)
        {
            return MultiPartTemplate;
        }

        var membership = BooksSeed.PrimaryMembershipOf(manifestation.WorkId);

        return membership is { Position.Length: > 0 } ? CollectedTemplate : SinglePartTemplate;
    }

    private static Dictionary<string, string> ResolveFor(SeedManifestation manifestation, int part)
    {
        var work = BooksSeed.Works.First(candidate => candidate.Id == manifestation.WorkId);
        var writer = BooksSeed.Writers.First(candidate => candidate.Id == work.WriterId);
        var membership = BooksSeed.PrimaryMembershipOf(work.Id);

        var collection = membership is null
            ? null
            : BooksSeed.Collections.FirstOrDefault(
                candidate => candidate.Id == membership.Value.CollectionId);

        var colon = work.Title.IndexOf(':', StringComparison.Ordinal);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{Author Name}"] = writer.Name,
            ["{Author Sort Name}"] = writer.SortName,
            ["{Book Title}"] = work.Title,
            ["{Book TitleNoSub}"] = colon < 0 ? work.Title : work.Title[..colon].Trim(),
            ["{Book Subtitle}"] = colon < 0 ? string.Empty : work.Title[(colon + 1)..].Trim(),

            // Single-valued tokens over a many-valued relation, resolved through the designated member.
            ["{Book Series}"] = collection?.Title ?? string.Empty,
            ["{Book SeriesPosition}"] = membership?.Position ?? string.Empty,
            ["{Edition Title}"] = manifestation.Title,
            ["{Edition Format}"] = manifestation.CarrierDescription,

            // Two dates, decades apart, and both wanted.
            ["{Release Year}"] = work.ReleaseDate.Year.ToString(CultureInfo.InvariantCulture),
            ["{Edition Year}"] = manifestation.ReleaseDate.Year.ToString(CultureInfo.InvariantCulture),
            ["{Part Number}"] = part.ToString("D2", CultureInfo.InvariantCulture),
            ["{Part Count}"] = manifestation.PartCount.ToString("D2", CultureInfo.InvariantCulture),
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
/// The collection is deliberately not part of the path even though it is part of the name. A work may
/// belong to several collections at once, so a collection folder would have to pick one arbitrarily and
/// would move every file whenever the catalog changed its mind about which is primary. The hierarchy is
/// what a path follows; the grouping axis is what a name and a browse view read.
/// </remarks>
public sealed class BooksLibraryLayout : ILibraryLayout
{
    /// <summary>The folder template applied when none is configured.</summary>
    public const string DefaultFolderTemplate = "{Author Sort Name}/{Book Title}";

    /// <inheritdoc />
    public MediaKindId MediaKind => BooksShape.Kind;

    /// <inheritdoc />
    public Task<string> GenerateFolderPathAsync(
        MediaItemId itemId,
        LibraryPathSpec pathSpec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathSpec);
        cancellationToken.ThrowIfCancellationRequested();

        var work = BooksSeed.Works.FirstOrDefault(candidate => candidate.Id == itemId.Value);

        if (work is null)
        {
            return Task.FromResult(pathSpec.RootPath);
        }

        var writer = BooksSeed.Writers.First(candidate => candidate.Id == work.WriterId);
        var colon = work.Title.IndexOf(':', StringComparison.Ordinal);

        var template = string.IsNullOrWhiteSpace(pathSpec.FolderTemplate)
            ? DefaultFolderTemplate
            : pathSpec.FolderTemplate;

        var resolved = template
            .Replace("{Author Name}", writer.Name, StringComparison.Ordinal)
            .Replace("{Author Sort Name}", writer.SortName, StringComparison.Ordinal)
            .Replace(
                "{Book TitleNoSub}",
                colon < 0 ? work.Title : work.Title[..colon].Trim(),
                StringComparison.Ordinal)
            .Replace("{Book Title}", work.Title, StringComparison.Ordinal)
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
            && pathSpec.FolderTemplate.Contains("{Book Title", StringComparison.Ordinal);
    }
}
