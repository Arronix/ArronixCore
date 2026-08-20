
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv;

/// <summary>
/// Projects the episodic catalog for the host, and produces the workbench proposals a front end edits.
/// </summary>
/// <remarks>
/// <para>Two levels, so every query names one and the host merges its own library state onto whichever it
/// asked for. Nothing here reports monitoring, progress, file counts or affordances: all four are host
/// state or host derivations.</para>
/// <para>The manual-import workbench is where the one-file-to-many-units binding becomes visible to a
/// person. A row's unit column is <b>multivalued</b>, the proposal fills it from the same parser the grab
/// path uses, and the commit refuses any row whose units straddle the outer ordinal — the declared span
/// constraint, enforced at the last point before the store.</para>
/// </remarks>
public sealed class TvItemSource : IMediaItemSource
{
    private readonly TvCatalog _catalog;
    private readonly TvQualityModel _quality = new();
    private readonly TvReleaseParser _parser = new();

    /// <summary>Creates a source over a catalog.</summary>
    /// <param name="catalog">The catalog to project.</param>
    public TvItemSource(TvCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    public Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 500);

        if (query.Level == TvIds.SeriesLevel)
        {
            return Task.FromResult(PageOf(QuerySeries(query), page, pageSize, ToView));
        }

        if (query.Level == TvIds.EpisodeLevel)
        {
            return Task.FromResult(PageOf(QueryEpisodes(query), page, pageSize, ToView));
        }

        return Task.FromResult(new ItemPage([], page, pageSize, 0));
    }

    /// <inheritdoc />
    public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (reference.Level == TvIds.SeriesLevel
            && _catalog.TryGetSeries(reference.Id.Value, out var series)
            && series is not null)
        {
            return Task.FromResult<ItemView?>(ToView(series));
        }

        if (reference.Level == TvIds.EpisodeLevel
            && _catalog.TryGetEpisode(reference.Id.Value, out var episode)
            && episode is not null)
        {
            return Task.FromResult<ItemView?>(ToView(episode));
        }

        return Task.FromResult<ItemView?>(null);
    }

    /// <inheritdoc />
    public Task<MediaItemRef?> ResolveExternalAsync(
        ExternalId externalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _catalog.TryGetSeriesByExternalId(externalId, out var series) && series is not null
                ? TvCatalog.ReferenceTo(series)
                : (MediaItemRef?)null);
    }

    /// <inheritdoc />
    public Task<WorkbenchProposal> ProposeAsync(
        string workbenchId,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbenchId);
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(workbenchId switch
        {
            TvWorkbenches.ManualImport => Propose(
                TvWorkbenches.ManualImport,
                TvWorkbenches.FilesInput,
                inputs,
                MatchSource.FileName),
            TvWorkbenches.InteractiveSearch => Propose(
                TvWorkbenches.InteractiveSearch,
                TvWorkbenches.ReleasesInput,
                inputs,
                MatchSource.ReleaseName),
            _ => new WorkbenchProposal
            {
                WorkbenchId = workbenchId,
                Rows = [],
                Issues =
                [
                    new ValidationFailure(
                        null,
                        $"'{workbenchId}' is not a workbench this media kind declares.",
                        ValidationSeverity.Error,
                        CoreErrorCode.InvalidConfiguration)
                ]
            }
        });
    }

    /// <inheritdoc />
    public Task<ActionResult> CommitAsync(
        WorkbenchCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        var excluded = new HashSet<string>(commit.ExcludedRowIds, StringComparer.Ordinal);
        var failures = new List<ValidationFailure>();
        var claimed = new Dictionary<int, string>();
        var accepted = 0;

        foreach (var row in commit.Rows)
        {
            if (excluded.Contains(row.RowId))
            {
                continue;
            }

            var units = ReadUnits(row, failures);

            if (units.Count == 0)
            {
                continue;
            }

            // The binding declares at most one unit per file to be FALSE, so several units in one row are
            // legal. It declares at most one file per unit to be TRUE, so the same unit twice is not.
            foreach (var unit in units)
            {
                if (claimed.TryGetValue(unit.Id, out var owner))
                {
                    failures.Add(new ValidationFailure(
                        TvWorkbenches.UnitsColumn,
                        $"Rows '{owner}' and '{row.RowId}' both claim the same unit. The binding permits at "
                        + "most one file per unit.",
                        ValidationSeverity.Error,
                        CoreErrorCode.ImportValidationFailed));
                }
                else
                {
                    claimed[unit.Id] = row.RowId;
                }
            }

            if (units.Select(unit => unit.SeriesId).Distinct().Count() != 1)
            {
                failures.Add(new ValidationFailure(
                    TvWorkbenches.UnitsColumn,
                    $"Row '{row.RowId}' assigns units from more than one library entry.",
                    ValidationSeverity.Error,
                    CoreErrorCode.ImportValidationFailed));
                continue;
            }

            // The declared span constraint. A file may straddle the inner component and never the outer one.
            if (units.Select(unit => unit.SeasonNumber).Distinct().Count() != 1)
            {
                failures.Add(new ValidationFailure(
                    TvWorkbenches.UnitsColumn,
                    $"Row '{row.RowId}' assigns units in more than one run. The shape declares "
                    + $"'{TvIds.AiredSpaceId}.{TvIds.SeasonComponentId}' as must-not-span.",
                    ValidationSeverity.Error,
                    CoreErrorCode.ImportValidationFailed));
                continue;
            }

            accepted++;
        }

        var outcome = failures.Count == 0
            ? ValidationOutcome.Success
            : ValidationOutcome.Failed([.. failures]);

        // The extension validates against its own catalog and its own declared constraints; the host
        // performs the unit-to-file linking, because the store is host state.
        return Task.FromResult(new ActionResult(
            outcome.IsValid,
            null,
            outcome.IsValid
                ? $"{accepted.ToString(CultureInfo.InvariantCulture)} assignment(s) are ready to apply."
                : "The proposal has rows that violate the declared binding.",
            outcome));
    }

    private static ItemPage PageOf<T>(
        IReadOnlyList<T> ordered,
        int page,
        int pageSize,
        Func<T, ItemView> project)
    {
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(project)
            .ToList();

        return new ItemPage(items, page, pageSize, ordered.Count);
    }

    private IReadOnlyList<TvSeriesRecord> QuerySeries(ItemQuery query)
    {
        IEnumerable<TvSeriesRecord> matches = _catalog.Series;

        if (!string.IsNullOrWhiteSpace(query.TextSearch))
        {
            var needle = TvTitleNormalizer.Normalize(query.TextSearch);
            matches = matches.Where(series =>
                TvTitleNormalizer.Normalize(series.Title).Contains(needle, StringComparison.Ordinal));
        }

        foreach (var (fieldId, allowed) in query.Filters)
        {
            var values = allowed;
            matches = matches.Where(series => SeriesFieldMatches(series, fieldId, values));
        }

        var ordered = query.SortFieldId switch
        {
            TvSeriesFields.Year => matches.OrderBy(series => series.Year),
            TvSeriesFields.FirstAired => matches.OrderBy(series => series.FirstAired ?? DateOnly.MaxValue),
            TvSeriesFields.Network => matches.OrderBy(series => series.Network, StringComparer.Ordinal),
            _ => matches.OrderBy(series => series.SortTitle, StringComparer.Ordinal)
        };

        var withTieBreak = ordered.ThenBy(series => series.SortTitle, StringComparer.Ordinal);

        return [.. query.SortDescending ? withTieBreak.Reverse() : withTieBreak];
    }

    private IReadOnlyList<TvEpisodeRecord> QueryEpisodes(ItemQuery query)
    {
        IEnumerable<TvEpisodeRecord> matches = query.Parent is { } parent
            && parent.Level == TvIds.SeriesLevel
                ? _catalog.EpisodesOf(parent.Id.Value)
                : _catalog.Episodes;

        if (!string.IsNullOrWhiteSpace(query.TextSearch))
        {
            var needle = TvTitleNormalizer.Normalize(query.TextSearch);
            matches = matches.Where(episode =>
                TvTitleNormalizer.Normalize(episode.Title).Contains(needle, StringComparison.Ordinal));
        }

        foreach (var (fieldId, allowed) in query.Filters)
        {
            var values = allowed;
            matches = matches.Where(episode => EpisodeFieldMatches(episode, fieldId, values));
        }

        var ordered = query.SortFieldId switch
        {
            TvEpisodeFields.AirDate => matches.OrderBy(episode => episode.AirDate ?? DateOnly.MaxValue),
            _ => matches.OrderBy(episode => episode.SeasonNumber).ThenBy(episode => episode.EpisodeNumber)
        };

        var withTieBreak = ordered
            .ThenBy(episode => episode.SeasonNumber)
            .ThenBy(episode => episode.EpisodeNumber);

        return [.. query.SortDescending ? withTieBreak.Reverse() : withTieBreak];
    }

    private static bool SeriesFieldMatches(
        TvSeriesRecord series,
        string fieldId,
        IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            return true;
        }

        return fieldId switch
        {
            TvSeriesFields.Year => allowed.Contains(
                series.Year.ToString(CultureInfo.InvariantCulture),
                StringComparer.Ordinal),
            TvSeriesFields.Status => allowed.Contains(series.Status, StringComparer.OrdinalIgnoreCase),
            TvSeriesFields.AddressingScheme => allowed.Contains(
                series.AddressingScheme,
                StringComparer.OrdinalIgnoreCase),
            TvSeriesFields.Network => series.Network is not null
                && allowed.Contains(series.Network, StringComparer.OrdinalIgnoreCase),
            TvSeriesFields.Genres => series.Genres.Any(genre =>
                allowed.Contains(genre, StringComparer.OrdinalIgnoreCase)),
            _ => true
        };
    }

    private static bool EpisodeFieldMatches(
        TvEpisodeRecord episode,
        string fieldId,
        IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            return true;
        }

        return fieldId switch
        {
            TvEpisodeFields.SequenceKind => allowed.Contains(
                episode.IsOutOfRun ? TvSequenceKinds.OutOfRun : TvSequenceKinds.Regular,
                StringComparer.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static ItemView ToView(TvSeriesRecord series)
    {
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [TvSeriesFields.Title] = FieldValue.OfText(series.Title),
            [TvSeriesFields.SortTitle] = FieldValue.OfText(series.SortTitle),
            [TvSeriesFields.Year] = FieldValue.OfInteger(series.Year),
            [TvSeriesFields.Status] = FieldValue.OfEnumerated(series.Status),
            [TvSeriesFields.AddressingScheme] = FieldValue.OfEnumerated(series.AddressingScheme),
            [TvSeriesFields.UsesAliasNumbering] = FieldValue.OfBoolean(series.UsesAliasNumbering),
            [TvSeriesFields.Network] = Text(series.Network),
            [TvSeriesFields.Overview] = series.Overview is null
                ? FieldValue.Absent(FieldValueKind.MultilineText)
                : FieldValue.OfMultilineText(series.Overview),
            [TvSeriesFields.Certification] = Text(series.Certification),
            [TvSeriesFields.Runtime] = series.RuntimeMinutes > 0
                ? FieldValue.OfDuration(TimeSpan.FromMinutes(series.RuntimeMinutes))
                : FieldValue.Absent(FieldValueKind.Duration),
            [TvSeriesFields.Genres] = series.Genres.Count == 0
                ? FieldValue.Absent(FieldValueKind.Text)
                : FieldValue.OfItems(FieldValueKind.Text, [.. series.Genres.Select(FieldValue.OfText)]),
            [TvSeriesFields.Poster] = series.Poster is null
                ? FieldValue.Absent(FieldValueKind.Artwork)
                : FieldValue.OfArtwork(series.Poster),
            [TvSeriesFields.FirstAired] = series.FirstAired is { } firstAired
                ? FieldValue.OfDate(firstAired)
                : FieldValue.Absent(FieldValueKind.Date),
            [TvSeriesFields.TvdbId] = FieldValue.OfExternalIdentifier(
                ExternalId.Of(TvIds.TvdbScheme, series.TvdbId.ToString(CultureInfo.InvariantCulture))),
            [TvSeriesFields.ImdbId] = string.IsNullOrEmpty(series.ImdbId)
                ? FieldValue.Absent(FieldValueKind.ExternalIdentifier)
                : FieldValue.OfExternalIdentifier(ExternalId.Of(TvIds.ImdbScheme, series.ImdbId))
        };

        var externalIds = new List<ExternalId>
        {
            ExternalId.Of(TvIds.TvdbScheme, series.TvdbId.ToString(CultureInfo.InvariantCulture))
        };

        if (!string.IsNullOrEmpty(series.ImdbId))
        {
            externalIds.Add(ExternalId.Of(TvIds.ImdbScheme, series.ImdbId));
        }

        return new ItemView
        {
            Ref = TvCatalog.ReferenceTo(series),
            Parent = null,
            Title = series.Title,
            SortTitle = series.SortTitle,
            Fields = fields,

            // The library entry carries no position. Only units do.
            Coordinates = CoordinateSet.Empty,
            ExternalIds = externalIds,
            SortIndex = series.Year,
            HasChildren = true,
            FormatFamilyId = null
        };
    }

    private static ItemView ToView(TvEpisodeRecord episode)
    {
        var position = OrdinalPath.Of(episode.SeasonNumber, episode.EpisodeNumber);

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [TvEpisodeFields.Title] = FieldValue.OfText(episode.Title),
            [TvEpisodeFields.Position] = FieldValue.OfOrdinal(position),
            [TvEpisodeFields.SequenceKind] = FieldValue.OfEnumerated(
                episode.IsOutOfRun ? TvSequenceKinds.OutOfRun : TvSequenceKinds.Regular),
            [TvEpisodeFields.Overview] = episode.Overview is null
                ? FieldValue.Absent(FieldValueKind.MultilineText)
                : FieldValue.OfMultilineText(episode.Overview),
            [TvEpisodeFields.AirDate] = episode.AirDate is { } airDate
                ? FieldValue.OfDate(airDate)
                : FieldValue.Absent(FieldValueKind.Date),
            [TvEpisodeFields.Runtime] = episode.RuntimeMinutes > 0
                ? FieldValue.OfDuration(TimeSpan.FromMinutes(episode.RuntimeMinutes))
                : FieldValue.Absent(FieldValueKind.Duration),
            [TvEpisodeFields.Still] = episode.Still is null
                ? FieldValue.Absent(FieldValueKind.Artwork)
                : FieldValue.OfArtwork(episode.Still)
        };

        return new ItemView
        {
            Ref = TvCatalog.ReferenceTo(episode),
            Parent = new MediaItemRef(
                TvIds.MediaKind,
                TvIds.SeriesLevel,
                MediaItemId.FromInt64(episode.SeriesId)),
            Title = episode.Title,
            SortTitle = episode.Title,
            Fields = fields,

            // Every reading the catalog knows, each with its own confidence. Up to five for one unit.
            Coordinates = TvCatalog.CoordinatesOf(episode),
            ExternalIds = [],
            SortIndex = (episode.SeasonNumber * 10_000L) + episode.EpisodeNumber,
            HasChildren = false,
            FormatFamilyId = TvIds.VideoFamilyId
        };
    }

    private static FieldValue Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? FieldValue.Absent(FieldValueKind.Text) : FieldValue.OfText(value);

    private WorkbenchProposal Propose(
        string workbenchId,
        string inputKey,
        IReadOnlyDictionary<string, string> inputs,
        MatchSource source)
    {
        var lines = ReadLines(inputs, inputKey);

        if (lines.Count == 0)
        {
            return new WorkbenchProposal
            {
                WorkbenchId = workbenchId,
                Rows = [],
                Issues =
                [
                    new ValidationFailure(
                        inputKey,
                        "No candidates were supplied.",
                        ValidationSeverity.Warning,
                        CoreErrorCode.InvalidConfiguration)
                ]
            };
        }

        var scope = ReadScope(inputs);
        var matcher = new TvReleaseMatcher(_catalog);
        var rows = new List<WorkbenchRow>(lines.Count);
        var index = 0;

        foreach (var line in lines)
        {
            var rowId = index.ToString(CultureInfo.InvariantCulture);
            index++;

            var subject = source == MatchSource.FileName ? LeafOf(line) : line.Split('|')[0].Trim();
            rows.Add(BuildRow(workbenchId, rowId, line, subject, source, scope, matcher));
        }

        return new WorkbenchProposal { WorkbenchId = workbenchId, Rows = rows };
    }

    private WorkbenchRow BuildRow(
        string workbenchId,
        string rowId,
        string rawLine,
        string subject,
        MatchSource source,
        MediaItemRef? scope,
        TvReleaseMatcher matcher)
    {
        var isImport = string.Equals(workbenchId, TvWorkbenches.ManualImport, StringComparison.Ordinal);

        var values = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [isImport ? TvWorkbenches.PathColumn : TvWorkbenches.ReleaseColumn] = isImport
                ? FieldValue.OfFilePath(rawLine)
                : FieldValue.OfText(subject)
        };

        var parsedRelease = _parser.Parse(subject);

        values[TvWorkbenches.QualityColumn] = parsedRelease is null
            ? FieldValue.Absent(FieldValueKind.Quality)
            : FieldValue.OfQuality(_quality.EvaluateQuality(parsedRelease));

        if (!TvTitleParser.TryParse(subject, out var parsed) || parsed is null)
        {
            values[TvWorkbenches.SchemeColumn] = FieldValue.Absent(FieldValueKind.Enumerated);
            values[TvWorkbenches.PositionColumn] = FieldValue.Absent(FieldValueKind.Text);
            values[TvWorkbenches.UnitsColumn] = FieldValue.Absent(FieldValueKind.Reference);

            return new WorkbenchRow
            {
                RowId = rowId,
                Values = values,
                Confidence = MatchConfidence.None,
                IncludedByDefault = false,
                Issues =
                [
                    new ValidationFailure(
                        isImport ? TvWorkbenches.PathColumn : TvWorkbenches.ReleaseColumn,
                        "No position in any declared coordinate space could be read from this text.",
                        ValidationSeverity.Warning,
                        CoreErrorCode.ParsingFailed)
                ]
            };
        }

        values[TvWorkbenches.SchemeColumn] = FieldValue.OfEnumerated(SchemeOf(parsed.Kind));
        values[TvWorkbenches.PositionColumn] = FieldValue.OfText(Describe(parsed));

        // The proposal uses the same matcher the grab path uses. A workbench that guessed differently from
        // the automatic path would be a second, silently divergent implementation of the hardest step.
        var outcome = matcher.Match(new MatchRequest
        {
            MediaKind = TvIds.MediaKind,
            Text = subject,
            Parsed = parsedRelease,
            Scope = scope,
            Source = source
        });

        values[TvWorkbenches.UnitsColumn] = outcome.Units.Count == 0
            ? FieldValue.Absent(FieldValueKind.Reference)
            : FieldValue.OfItems(
                FieldValueKind.Reference,
                [.. outcome.Units.Select(FieldValue.OfReference)]);

        var issues = new List<ValidationFailure>();

        if (outcome.RejectionReason is { } rejection)
        {
            issues.Add(new ValidationFailure(
                TvWorkbenches.UnitsColumn,
                rejection,
                ValidationSeverity.Warning,
                CoreErrorCode.MediaItemNotFound));
        }

        foreach (var warning in outcome.Warnings)
        {
            issues.Add(new ValidationFailure(
                TvWorkbenches.UnitsColumn,
                warning,
                ValidationSeverity.Warning,
                CoreErrorCode.Unknown));
        }

        return new WorkbenchRow
        {
            RowId = rowId,
            Values = values,
            Confidence = outcome.Confidence,
            IncludedByDefault = outcome.Units.Count != 0 && outcome.Confidence >= MatchConfidence.Medium,
            Issues = issues
        };
    }

    private List<TvEpisodeRecord> ReadUnits(WorkbenchRow row, List<ValidationFailure> failures)
    {
        var units = new List<TvEpisodeRecord>();

        if (!row.Values.TryGetValue(TvWorkbenches.UnitsColumn, out var value) || value.IsAbsent)
        {
            failures.Add(new ValidationFailure(
                TvWorkbenches.UnitsColumn,
                $"Row '{row.RowId}' has no unit assigned.",
                ValidationSeverity.Error,
                CoreErrorCode.ImportValidationFailed));
            return units;
        }

        var references = new List<MediaItemRef>();

        if (value.Items is { } items)
        {
            references.AddRange(items
                .Where(item => item.Reference is not null)
                .Select(item => item.Reference!.Value));
        }
        else if (value.Reference is { } single)
        {
            references.Add(single);
        }

        if (references.Count == 0)
        {
            failures.Add(new ValidationFailure(
                TvWorkbenches.UnitsColumn,
                $"Row '{row.RowId}' has no unit assigned.",
                ValidationSeverity.Error,
                CoreErrorCode.ImportValidationFailed));
            return units;
        }

        foreach (var reference in references)
        {
            if (reference.Level != TvIds.EpisodeLevel
                || !_catalog.TryGetEpisode(reference.Id.Value, out var unit)
                || unit is null)
            {
                failures.Add(new ValidationFailure(
                    TvWorkbenches.UnitsColumn,
                    $"Row '{row.RowId}' names '{reference}', which is not a unit in this catalog.",
                    ValidationSeverity.Error,
                    CoreErrorCode.MediaItemNotFound));
                continue;
            }

            units.Add(unit);
        }

        return units;
    }

    private static string SchemeOf(TvReleaseKind kind) => kind switch
    {
        TvReleaseKind.AirDate => TvAddressingSchemes.Calendar,
        TvReleaseKind.Absolute => TvAddressingSchemes.Flat,
        _ => TvAddressingSchemes.Ordinal
    };

    private static string Describe(TvParsedTitle parsed) => parsed.Kind switch
    {
        TvReleaseKind.SeasonEpisode => string.Join(
            ", ",
            parsed.EpisodeNumbers.Select(episode =>
                $"S{(parsed.SeasonNumber ?? 0).ToString("00", CultureInfo.InvariantCulture)}"
                + $"E{episode.ToString("00", CultureInfo.InvariantCulture)}")),
        TvReleaseKind.Absolute => string.Join(
            ", ",
            parsed.AbsoluteNumbers.Select(absolute =>
                absolute.ToString("000", CultureInfo.InvariantCulture))),
        TvReleaseKind.AirDate => parsed.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ?? string.Empty,
        TvReleaseKind.FullSeason =>
            $"S{(parsed.SeasonNumber ?? 0).ToString("00", CultureInfo.InvariantCulture)} (whole run)",
        _ => string.Empty
    };

    private static MediaItemRef? ReadScope(IReadOnlyDictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue(TvWorkbenches.ScopeInput, out var raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        return new MediaItemRef(TvIds.MediaKind, TvIds.SeriesLevel, MediaItemId.FromInt64(id));
    }

    private static IReadOnlyList<string> ReadLines(IReadOnlyDictionary<string, string> inputs, string key)
    {
        if (!inputs.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return
        [
            .. raw
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ];
    }

    private static string LeafOf(string path)
    {
        var lastSeparator = path.LastIndexOfAny(['/', '\\']);
        var leaf = lastSeparator >= 0 ? path[(lastSeparator + 1)..] : path;
        var extension = leaf.LastIndexOf('.');

        return extension > 0 ? leaf[..extension] : leaf;
    }
}
