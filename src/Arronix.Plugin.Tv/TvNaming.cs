
using System.Globalization;
using System.Linq;
using System.Text;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv;

/// <summary>
/// How the inner ordinals of a file that satisfies several units are written.
/// </summary>
/// <remarks>
/// Six ways to write "this file contains units 3, 4 and 5", and the reference implementation supports all
/// six because release communities disagree about which is correct. This is the <b>rendering</b> of the
/// one-file-to-many-units binding, and its existence is the clearest evidence that the binding is real
/// rather than a modeling curiosity.
/// </remarks>
public enum TvMultiUnitStyle
{
    /// <summary><c>S01E01-02-03</c>. The default in most libraries.</summary>
    Extend = 0,

    /// <summary><c>S01E01.S01E02</c>. Repeats the whole coordinate.</summary>
    Duplicate = 1,

    /// <summary><c>S01E01E02</c>. Repeats only the inner marker.</summary>
    Repeat = 2,

    /// <summary><c>S01E01-E02</c>. The release-community spelling.</summary>
    Scene = 3,

    /// <summary><c>S01E01-03</c>. First and last only.</summary>
    Range = 4,

    /// <summary><c>S01E01-E03</c>. First and last, with the inner marker repeated.</summary>
    PrefixedRange = 5
}

/// <summary>
/// Substitutes <c>{Token}</c> placeholders and keeps the result usable as a path component.
/// </summary>
public static class TvNameFormatter
{
    private static readonly char[] Unsafe = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

    /// <summary>Replaces every declared token in a template.</summary>
    /// <param name="template">The template.</param>
    /// <param name="tokens">Token name (including braces and any format suffix) to value.</param>
    /// <returns>The rendered string, with unresolved tokens removed and whitespace collapsed.</returns>
    public static string Render(string template, IReadOnlyDictionary<string, string> tokens)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(tokens);

        var builder = new StringBuilder(template);

        // Longest first, so "{season:00}" is not partially consumed by "{season}".
        foreach (var token in tokens.Keys.OrderByDescending(key => key.Length))
        {
            builder.Replace(token, tokens[token]);
        }

        var rendered = builder.ToString();
        var cleaned = new StringBuilder(rendered.Length);
        var inToken = false;

        foreach (var character in rendered)
        {
            if (character == '{')
            {
                inToken = true;
                continue;
            }

            if (character == '}')
            {
                inToken = false;
                continue;
            }

            if (!inToken)
            {
                cleaned.Append(character);
            }
        }

        return Collapse(cleaned.ToString());
    }

    /// <summary>Removes characters that cannot appear in a path component on any supported platform.</summary>
    /// <param name="value">The raw component.</param>
    /// <returns>The sanitized component.</returns>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(Array.IndexOf(Unsafe, character) >= 0 ? ' ' : character);
        }

        return Collapse(builder.ToString()).TrimEnd('.');
    }

    /// <summary>
    /// Builds the replacement text for the inner-ordinal token when one file satisfies several units.
    /// </summary>
    /// <param name="season">The shared outer ordinal.</param>
    /// <param name="episodes">The inner ordinals, ascending.</param>
    /// <param name="style">How to write them.</param>
    /// <param name="padding">How many digits each ordinal is padded to.</param>
    /// <returns>The text that replaces the inner-ordinal token.</returns>
    /// <remarks>
    /// <see cref="TvMultiUnitStyle.Duplicate"/> re-states the outer ordinal, so it assumes the canonical
    /// <c>S{season:00}E{episode:00}</c> arrangement in the template. The other five styles are pure
    /// substitutions and work in any arrangement, including the flat-ordinal one.
    /// </remarks>
    public static string RenderInnerOrdinals(
        int season,
        IReadOnlyList<int> episodes,
        TvMultiUnitStyle style,
        int padding)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        if (episodes.Count == 0)
        {
            return string.Empty;
        }

        var format = new string('0', Math.Max(1, padding));
        var seasonText = season.ToString(new string('0', Math.Max(1, padding)), CultureInfo.InvariantCulture);

        string Pad(int value) => value.ToString(format, CultureInfo.InvariantCulture);

        if (episodes.Count == 1)
        {
            return Pad(episodes[0]);
        }

        return style switch
        {
            TvMultiUnitStyle.Extend => string.Join('-', episodes.Select(Pad)),
            TvMultiUnitStyle.Duplicate => string.Join(
                '.',
                episodes.Select((episode, index) =>
                    index == 0 ? Pad(episode) : $"S{seasonText}E{Pad(episode)}")),
            TvMultiUnitStyle.Repeat => string.Join('E', episodes.Select(Pad)),
            TvMultiUnitStyle.Scene => string.Join("-E", episodes.Select(Pad)),
            TvMultiUnitStyle.Range => $"{Pad(episodes[0])}-{Pad(episodes[^1])}",
            TvMultiUnitStyle.PrefixedRange => $"{Pad(episodes[0])}-E{Pad(episodes[^1])}",
            _ => string.Join('-', episodes.Select(Pad))
        };
    }

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value)
        {
            var isSpace = character is ' ' or '\t';

            if (isSpace && previousWasSpace)
            {
                continue;
            }

            previousWasSpace = isSpace;
            builder.Append(isSpace ? ' ' : character);
        }

        return builder.ToString().Trim();
    }
}

/// <summary>
/// Renders episodic file names from the declared token vocabulary.
/// </summary>
/// <remarks>
/// <para>Three default templates, one per addressing scheme, selected from the library entry's own declared
/// scheme — and the flat one degrades to the canonical one when a unit has no flat ordinal, because the
/// coordinate is genuinely optional and a template that cannot be filled must not be used.</para>
/// <para><b>A gap in the stable contract, recorded rather than worked around.</b>
/// <see cref="IRenamePolicy.GenerateFileNameAsync(MediaItemId, MediaFileFacts?, string, CancellationToken)"/>
/// takes a single unit. This media kind's binding permits one file to satisfy several, so the multi-unit
/// form cannot be expressed through the stable contract at all. It is offered here as
/// <see cref="GenerateFileNameForUnitsAsync"/>, which no host seam currently calls.</para>
/// </remarks>
public sealed class TvRenamePolicy : IRenamePolicy
{
    private readonly TvCatalog _catalog;

    /// <summary>Creates a policy over a catalog.</summary>
    /// <param name="catalog">The catalog supplying token values.</param>
    public TvRenamePolicy(TvCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>The template for entries addressed by the canonical ordinal pair.</summary>
    public static string OrdinalTemplate =>
        "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}";

    /// <summary>The template for entries addressed by the calendar space.</summary>
    public static string CalendarTemplate =>
        "{Series Title} - {Air-Date} - {Episode Title} {Quality Full}";

    /// <summary>The template for entries addressed by the flat ordinal.</summary>
    public static string FlatTemplate =>
        "{Series Title} - {absolute:000} - {Episode Title} {Quality Full}";

    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    /// <remarks>
    /// The file-fact tokens — <c>{Quality Title}</c>, <c>{Quality Full}</c>, <c>{Release Group}</c> and
    /// <c>{Original Filename}</c> — were declared from the start but unreachable until the contract
    /// carried the file being named. They resolve from <paramref name="file"/> now, and render as absent
    /// when no file is passed, exactly as before the contract fix.
    /// </remarks>
    public async Task<string> GenerateFileNameAsync(
        MediaItemId itemId,
        MediaFileFacts? file,
        string namingTemplate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namingTemplate);

        var itemTokens = await ResolveTokensAsync(itemId, cancellationToken).ConfigureAwait(false);
        var tokens = new Dictionary<string, string>(itemTokens, StringComparer.Ordinal);

        if (file is not null)
        {
            AddFileTokens(tokens, file);
        }

        return TvNameFormatter.Sanitize(TvNameFormatter.Render(namingTemplate, tokens));
    }

    /// <summary>
    /// Renders the name of a file that satisfies several units.
    /// </summary>
    /// <param name="itemIds">The units the file satisfies, in any order.</param>
    /// <param name="namingTemplate">The template.</param>
    /// <param name="style">How to write the inner ordinals.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The rendered file name.</returns>
    /// <exception cref="ArgumentException">
    /// The units do not all belong to one library entry, or they do not share an outer ordinal — which the
    /// declared span constraint forbids.
    /// </exception>
    public Task<string> GenerateFileNameForUnitsAsync(
        IReadOnlyList<MediaItemId> itemIds,
        string namingTemplate,
        TvMultiUnitStyle style = TvMultiUnitStyle.Extend,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(namingTemplate);
        cancellationToken.ThrowIfCancellationRequested();

        var units = itemIds
            .Select(itemId => _catalog.TryGetEpisode(itemId.Value, out var unit) ? unit : null)
            .Where(unit => unit is not null)
            .Select(unit => unit!)
            .OrderBy(unit => unit.SeasonNumber)
            .ThenBy(unit => unit.EpisodeNumber)
            .ToList();

        if (units.Count == 0)
        {
            throw new ArgumentException("No unit in the catalog matches any of the ids.", nameof(itemIds));
        }

        if (units.Select(unit => unit.SeriesId).Distinct().Count() != 1)
        {
            throw new ArgumentException(
                "One file cannot satisfy units belonging to different library entries.",
                nameof(itemIds));
        }

        if (units.Select(unit => unit.SeasonNumber).Distinct().Count() != 1)
        {
            throw new ArgumentException(
                $"The shape declares '{TvIds.AiredSpaceId}.{TvIds.SeasonComponentId}' as must-not-span, so "
                + "one file cannot satisfy units in different runs.",
                nameof(itemIds));
        }

        if (!_catalog.TryGetSeries(units[0].SeriesId, out var series) || series is null)
        {
            throw new ArgumentException("The units belong to an unknown library entry.", nameof(itemIds));
        }

        var tokens = BuildTokens(series, units[0]);
        var innerOrdinals = units.Select(unit => unit.EpisodeNumber).ToList();

        tokens["{episode}"] = TvNameFormatter.RenderInnerOrdinals(
            units[0].SeasonNumber, innerOrdinals, style, 1);
        tokens["{episode:00}"] = TvNameFormatter.RenderInnerOrdinals(
            units[0].SeasonNumber, innerOrdinals, style, 2);

        if (units.All(unit => unit.AbsoluteNumber.HasValue))
        {
            tokens["{absolute:000}"] = TvNameFormatter.RenderInnerOrdinals(
                units[0].SeasonNumber,
                [.. units.Select(unit => unit.AbsoluteNumber!.Value)],
                style == TvMultiUnitStyle.Duplicate ? TvMultiUnitStyle.Extend : style,
                3);
        }

        // Every unit's title, joined - the reference implementation does the same, and it is why a
        // multi-unit file name is so much longer than a single one.
        tokens["{Episode Title}"] = string.Join(" + ", units.Select(unit => unit.Title).Distinct());

        return Task.FromResult(
            TvNameFormatter.Sanitize(TvNameFormatter.Render(namingTemplate, tokens)));
    }

    /// <summary>Chooses the default template for a library entry.</summary>
    /// <param name="series">The entry.</param>
    /// <param name="unit">The unit being named.</param>
    /// <returns>The template.</returns>
    /// <remarks>
    /// The flat template degrades to the canonical one when the unit has no flat ordinal. The reference
    /// implementation carries the same guard, and it is the behavior that keeps an optional coordinate
    /// optional instead of quietly producing a name with a hole in it.
    /// </remarks>
    public static string TemplateFor(TvSeriesRecord series, TvEpisodeRecord unit)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(unit);

        return series.AddressingScheme switch
        {
            TvAddressingSchemes.Calendar when unit.AirDate.HasValue => CalendarTemplate,
            TvAddressingSchemes.Flat when unit.AbsoluteNumber.HasValue => FlatTemplate,
            _ => OrdinalTemplate
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ResolveTokensAsync(
        MediaItemId itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_catalog.TryGetEpisode(itemId.Value, out var unit)
            || unit is null
            || !_catalog.TryGetSeries(unit.SeriesId, out var series)
            || series is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(BuildTokens(series, unit));
    }

    /// <inheritdoc />
    public bool ValidateTemplate(string namingTemplate)
    {
        if (string.IsNullOrWhiteSpace(namingTemplate))
        {
            return false;
        }

        foreach (var token in EnumerateTokens(namingTemplate))
        {
            if (!TvShape.DeclaredTokenNames.Contains(token, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return TvShape.RequiredTokenNames.All(
            token => namingTemplate.Contains(token, StringComparison.Ordinal));
    }

    private static Dictionary<string, string> BuildTokens(TvSeriesRecord series, TvEpisodeRecord unit)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{Series Title}"] = series.Title,
            ["{Series CleanTitle}"] = TvTitleNormalizer.ToQueryText(series.Title),
            ["{Series TitleYear}"] = $"{series.Title} ({series.Year.ToString(CultureInfo.InvariantCulture)})",
            ["{Series TitleThe}"] = MoveLeadingArticle(series.Title),
            ["{season}"] = unit.SeasonNumber.ToString(CultureInfo.InvariantCulture),
            ["{season:00}"] = unit.SeasonNumber.ToString("00", CultureInfo.InvariantCulture),
            ["{episode}"] = unit.EpisodeNumber.ToString(CultureInfo.InvariantCulture),
            ["{episode:00}"] = unit.EpisodeNumber.ToString("00", CultureInfo.InvariantCulture),
            ["{Episode Title}"] = unit.Title,
            ["{Episode CleanTitle}"] = TvTitleNormalizer.ToQueryText(unit.Title),
            ["{Season Title}"] = unit.SeasonNumber == TvIds.SpecialsOrdinal
                ? "Specials"
                : $"Season {unit.SeasonNumber.ToString(CultureInfo.InvariantCulture)}",
            ["{TvdbId}"] = series.TvdbId.ToString(CultureInfo.InvariantCulture)
        };

        if (unit.AbsoluteNumber is { } absolute)
        {
            tokens["{absolute:000}"] = absolute.ToString("000", CultureInfo.InvariantCulture);
        }

        if (unit.AirDate is { } airDate)
        {
            tokens["{Air-Date}"] = airDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(series.ImdbId))
        {
            tokens["{ImdbId}"] = series.ImdbId;
        }

        return tokens;
    }

    private static void AddFileTokens(Dictionary<string, string> tokens, MediaFileFacts file)
    {
        tokens["{Quality Title}"] = file.Quality.Name;
        tokens["{Quality Full}"] = QualityFull(file.Quality);

        if (!string.IsNullOrEmpty(file.ReleaseGroup))
        {
            tokens["{Release Group}"] = file.ReleaseGroup;
        }

        if (!string.IsNullOrEmpty(file.OriginalFileName))
        {
            tokens["{Original Filename}"] = file.OriginalFileName;
        }
    }

    private static string QualityFull(QualityTier quality)
    {
        var revision = quality.Revision ?? QualityRevision.Initial;
        var builder = new StringBuilder(quality.Name);

        if (revision.Version > 1)
        {
            builder.Append(revision.IsRepack ? " Repack" : " Proper");
        }

        if (revision.Real > 0)
        {
            builder.Append(" REAL");
        }

        return builder.ToString();
    }

    private static IEnumerable<string> EnumerateTokens(string template)
    {
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);

            if (open < 0)
            {
                yield break;
            }

            var close = template.IndexOf('}', open);

            if (close < 0)
            {
                yield break;
            }

            yield return template[open..(close + 1)];
            index = close + 1;
        }
    }

    private static string MoveLeadingArticle(string title)
    {
        string[] articles = ["The ", "A ", "An "];

        foreach (var article in articles)
        {
            if (title.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                return $"{title[article.Length..]}, {title[..(article.Length - 1)]}";
            }
        }

        return title;
    }
}

/// <summary>
/// Decides the folder a unit's files live in.
/// </summary>
/// <remarks>
/// Two segments below the root: the library entry, then the sequence axis. The second one is the visible
/// consequence of the axis carrying a policy record — a bare coordinate would not justify a folder, and a
/// level would have justified a row. It is neither, and it gets a folder and a monitored bit and nothing
/// else.
/// </remarks>
public sealed class TvLibraryLayout : ILibraryLayout
{
    private readonly TvCatalog _catalog;

    /// <summary>Creates a layout over a catalog.</summary>
    /// <param name="catalog">The catalog supplying folder names.</param>
    public TvLibraryLayout(TvCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Custom token switching the sequence-axis subfolder off.</summary>
    public const string FlattenSequenceToken = "flattenSequence";

    /// <summary>The folder template used when configuration supplies none.</summary>
    public static string DefaultFolderTemplate => "{Series TitleYear}";

    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    public Task<string> GenerateFolderPathAsync(
        MediaItemId itemId,
        LibraryPathSpec pathSpec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathSpec);
        cancellationToken.ThrowIfCancellationRequested();

        var root = pathSpec.RootPath.TrimEnd('/', '\\');

        // The id may name either level. A library entry resolves to its own folder; a unit resolves to its
        // entry's folder plus the sequence-axis folder.
        if (_catalog.TryGetSeries(itemId.Value, out var directSeries) && directSeries is not null)
        {
            return Task.FromResult(pathSpec.CreateSubfolders
                ? string.Join('/', root, EntryFolder(directSeries))
                : root);
        }

        if (!_catalog.TryGetEpisode(itemId.Value, out var unit)
            || unit is null
            || !_catalog.TryGetSeries(unit.SeriesId, out var series)
            || series is null)
        {
            return Task.FromResult(root);
        }

        if (!pathSpec.CreateSubfolders)
        {
            return Task.FromResult(root);
        }

        var segments = new List<string> { root, EntryFolder(series) };

        var flatten = pathSpec.CustomTokens is { } custom
            && custom.TryGetValue(FlattenSequenceToken, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        if (!flatten)
        {
            segments.Add(unit.SeasonNumber == TvIds.SpecialsOrdinal
                ? "Specials"
                : $"Season {unit.SeasonNumber.ToString("00", CultureInfo.InvariantCulture)}");
        }

        return Task.FromResult(string.Join('/', segments));
    }

    /// <inheritdoc />
    public bool ValidatePathSpec(LibraryPathSpec pathSpec)
    {
        ArgumentNullException.ThrowIfNull(pathSpec);

        return !string.IsNullOrWhiteSpace(pathSpec.RootPath)
            && !string.IsNullOrWhiteSpace(pathSpec.FolderTemplate);
    }

    private static string EntryFolder(TvSeriesRecord series)
        => TvNameFormatter.Sanitize(
            $"{series.Title} ({series.Year.ToString(CultureInfo.InvariantCulture)}) "
            + $"{{tvdb-{series.TvdbId.ToString(CultureInfo.InvariantCulture)}}}");
}

/// <summary>
/// Resolves a TheTVDB identifier to a catalog item.
/// </summary>
public sealed class TvIdResolver : IMediaIdResolver
{
    private readonly TvCatalog _catalog;

    /// <summary>Creates a resolver over a catalog.</summary>
    /// <param name="catalog">The catalog to resolve against.</param>
    public TvIdResolver(TvCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    public string IdentifierSystem => TvIds.TvdbScheme;

    /// <inheritdoc />
    public Task<MediaItemId?> ResolveAsync(string externalId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Resolve(externalId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, MediaItemId>> ResolveManyAsync(
        IEnumerable<string> externalIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = new Dictionary<string, MediaItemId>(StringComparer.Ordinal);

        foreach (var externalId in externalIds)
        {
            if (Resolve(externalId) is { } itemId)
            {
                resolved[externalId] = itemId;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, MediaItemId>>(resolved);
    }

    private MediaItemId? Resolve(string externalId)
    {
        var candidate = ExternalId.TryParse(externalId, out var parsed)
            ? parsed
            : ExternalId.Of(TvIds.TvdbScheme, externalId);

        return _catalog.TryGetSeriesByExternalId(candidate, out var series) && series is not null
            ? MediaItemId.FromInt64(series.Id)
            : null;
    }
}
