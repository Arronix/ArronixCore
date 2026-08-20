using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Plugin.Music.Seed;

namespace Arronix.Plugin.Music;

/// <summary>
/// Projects this extension's catalog, and answers for the working surfaces it declared.
/// </summary>
/// <remarks>
/// <para>
/// Everything returned here is catalog: what exists, how it is titled, where it sits. Nothing here is
/// library state - not whether an item is wanted, not which pressing was selected, not which files
/// satisfy it. The host owns that half and merges the two on read, which is why
/// <see cref="ItemView"/> has no slot for any of it.
/// </para>
/// <para>
/// The projection is deliberately faithful about one asymmetry: recordings hang off a <em>pressing</em>,
/// not off the work. Asking for the recordings of a work is therefore not a question this source can
/// answer - the caller must name the pressing, because the answer is different for each of them. That is
/// not an implementation limitation, it is the shape.
/// </para>
/// </remarks>
public sealed class MusicItemSource : IMediaItemSource
{
    /// <summary>The working surface that assigns loose files to recordings.</summary>
    public const string ManualImportWorkbenchId = "manual-import";

    /// <summary>The working surface that chooses among candidate releases by hand.</summary>
    public const string InteractiveSearchWorkbenchId = "interactive-search";

    /// <summary>The input naming the folder a manual import proposal is built from.</summary>
    public const string FolderInputId = "folder";

    /// <summary>The input naming the file names a manual import proposal is built from.</summary>
    public const string FilesInputId = "files";

    /// <summary>The input naming the pressing a manual import proposal assigns against.</summary>
    public const string PressingInputId = "release";

    /// <inheritdoc />
    public MediaKindId MediaKind => MusicShape.Kind;

    /// <inheritdoc />
    public Task<ItemPage> QueryAsync(ItemQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var all = ProjectLevel(query.Level, query.Parent);

        if (!string.IsNullOrWhiteSpace(query.TextSearch))
        {
            all = all.Where(view =>
                    view.Title.Contains(query.TextSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var filter in query.Filters)
        {
            var fieldId = filter.Key;
            var allowed = filter.Value;
            all = all.Where(view => Matches(view, fieldId, allowed)).ToList();
        }

        all = Sort(all, query.SortFieldId, query.SortDescending);

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 500);
        var window = all.Skip((page - 1) * size).Take(size).ToList();

        return Task.FromResult(new ItemPage(window, page, size, all.Count));
    }

    /// <inheritdoc />
    public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var match = ProjectLevel(reference.Level, parent: null)
            .FirstOrDefault(view => view.Ref == reference);

        return Task.FromResult(match);
    }

    /// <inheritdoc />
    public Task<MediaItemRef?> ResolveExternalAsync(
        ExternalId externalId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(externalId.Scheme, MusicShape.CatalogScheme, StringComparison.Ordinal))
        {
            return Task.FromResult<MediaItemRef?>(null);
        }

        foreach (var performer in MusicSeed.Performers)
        {
            if (string.Equals(performer.CatalogId, externalId.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<MediaItemRef?>(Reference(MusicShape.PerformerLevel, performer.Id));
            }
        }

        foreach (var work in MusicSeed.Works)
        {
            if (string.Equals(work.CatalogId, externalId.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<MediaItemRef?>(Reference(MusicShape.WorkLevel, work.Id));
            }
        }

        foreach (var pressing in MusicSeed.Pressings)
        {
            if (string.Equals(pressing.CatalogId, externalId.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<MediaItemRef?>(Reference(MusicShape.PressingLevel, pressing.Id));
            }
        }

        foreach (var recording in MusicSeed.Recordings)
        {
            if (string.Equals(recording.CatalogId, externalId.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<MediaItemRef?>(Reference(MusicShape.RecordingLevel, recording.Id));
            }
        }

        return Task.FromResult<MediaItemRef?>(null);
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

        var proposal = workbenchId switch
        {
            ManualImportWorkbenchId => ProposeManualImport(inputs),
            InteractiveSearchWorkbenchId => ProposeInteractiveSearch(inputs),
            _ => new WorkbenchProposal
            {
                WorkbenchId = workbenchId,
                Rows = [],
                Issues =
                [
                    new ValidationFailure(null, "This extension declares no such working surface."),
                ],
            },
        };

        return Task.FromResult(proposal);
    }

    /// <inheritdoc />
    public Task<ActionResult> CommitAsync(
        WorkbenchCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        var accepted = commit.Rows.Count - commit.ExcludedRowIds.Count;

        return Task.FromResult(new ActionResult(
            true,
            null,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{accepted} of {commit.Rows.Count} rows accepted for {commit.WorkbenchId}."),
            ValidationOutcome.Success));
    }

    /// <summary>
    /// Returns the recordings one pressing's running order consists of, as item references.
    /// </summary>
    /// <param name="pressing">The pressing.</param>
    /// <returns>Its recordings, in absolute order.</returns>
    /// <remarks>
    /// The denominator of completeness, and the reason it is variant-relative: two pressings of the same
    /// work return different counts, so "eleven of twelve" and "eleven of seventeen" are both true
    /// statements about the same set of files.
    /// </remarks>
    public static IReadOnlyList<MediaItemRef> RunningOrderOf(MediaItemRef pressing) =>
        MusicSeed.RecordingsOf(pressing.Id.Value)
            .Select(recording => Reference(MusicShape.RecordingLevel, recording.Id))
            .ToList();

    private static MediaItemRef Reference(MediaLevelId level, int id) =>
        new(MusicShape.Kind, level, MediaItemId.FromInt64(id));

    private List<ItemView> ProjectLevel(MediaLevelId level, MediaItemRef? parent)
    {
        if (level == MusicShape.PerformerLevel)
        {
            return MusicSeed.Performers.Select(ProjectPerformer).ToList();
        }

        if (level == MusicShape.WorkLevel)
        {
            return MusicSeed.Works
                .Where(work => parent is null || work.PerformerId == parent.Value.Id.Value)
                .Select(ProjectWork)
                .ToList();
        }

        if (level == MusicShape.PressingLevel)
        {
            return MusicSeed.Pressings
                .Where(pressing => parent is null || pressing.WorkId == parent.Value.Id.Value)
                .Select(ProjectPressing)
                .ToList();
        }

        if (level == MusicShape.RecordingLevel)
        {
            return MusicSeed.Recordings
                .Where(recording => parent is null || recording.PressingId == parent.Value.Id.Value)
                .Select(ProjectRecording)
                .ToList();
        }

        return [];
    }

    private static ItemView ProjectPerformer(SeedPerformer performer) => new()
    {
        Ref = Reference(MusicShape.PerformerLevel, performer.Id),
        Parent = null,
        Title = performer.Name,
        SortTitle = performer.SortName,
        SortIndex = performer.Id,
        HasChildren = true,
        ExternalIds = [ExternalId.Of(MusicShape.CatalogScheme, performer.CatalogId)],
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [MusicFields.Name] = FieldValue.OfText(performer.Name),
            [MusicFields.SortName] = FieldValue.OfText(performer.SortName),
            [MusicFields.PerformerKind] = FieldValue.OfEnumerated(performer.PerformerKind),
            [MusicFields.Overview] = FieldValue.OfMultilineText(performer.Overview),
            [MusicFields.Disambiguation] = performer.Disambiguation is null
                ? FieldValue.Absent(FieldValueKind.Text)
                : FieldValue.OfText(performer.Disambiguation),
            [MusicFields.Genres] = FieldValue.OfItems(
                FieldValueKind.Text,
                performer.Genres.Select(FieldValue.OfText).ToList()),
            [MusicFields.CatalogId] = FieldValue.OfExternalIdentifier(
                ExternalId.Of(MusicShape.CatalogScheme, performer.CatalogId)),
            [MusicFields.Artwork] = FieldValue.Absent(FieldValueKind.Artwork),
        },
    };

    private static ItemView ProjectWork(SeedWork work) => new()
    {
        Ref = Reference(MusicShape.WorkLevel, work.Id),
        Parent = Reference(MusicShape.PerformerLevel, work.PerformerId),
        Title = work.Title,
        SortTitle = work.Title,
        SortIndex = work.ReleaseDate.DayNumber,
        HasChildren = true,
        FormatFamilyId = MusicShape.AudioFamilyId,
        ExternalIds = [ExternalId.Of(MusicShape.CatalogScheme, work.CatalogId)],
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [MusicFields.Title] = FieldValue.OfText(work.Title),
            [MusicFields.ReleaseDate] = FieldValue.OfDate(work.ReleaseDate),
            [MusicFields.PrimaryKind] = FieldValue.OfEnumerated(work.PrimaryKind),
            [MusicFields.SecondaryKinds] = FieldValue.OfItems(
                FieldValueKind.Enumerated,
                work.SecondaryKinds.Select(FieldValue.OfEnumerated).ToList()),
            [MusicFields.Disambiguation] = work.Disambiguation is null
                ? FieldValue.Absent(FieldValueKind.Text)
                : FieldValue.OfText(work.Disambiguation),

            // The catalog can say how many recordings the preferred pressing has. It cannot say how many
            // the library's selected pressing has, because it does not know which one that is.
            [MusicFields.RecordingCount] = FieldValue.OfCount(CatalogPreferredRunLength(work.Id)),
            [MusicFields.State] = FieldValue.OfEnumerated(
                work.ReleaseDate > DateOnly.FromDateTime(DateTime.UtcNow)
                    ? MusicStates.Unissued
                    : MusicStates.Missing),
            [MusicFields.CatalogId] = FieldValue.OfExternalIdentifier(
                ExternalId.Of(MusicShape.CatalogScheme, work.CatalogId)),
            [MusicFields.Artwork] = FieldValue.Absent(FieldValueKind.Artwork),
        },
    };

    private static ItemView ProjectPressing(SeedPressing pressing) => new()
    {
        Ref = Reference(MusicShape.PressingLevel, pressing.Id),
        Parent = Reference(MusicShape.WorkLevel, pressing.WorkId),
        Title = pressing.Title,
        SortTitle = pressing.Title,
        SortIndex = pressing.ReleaseDate.DayNumber,
        HasChildren = true,
        ExternalIds = [ExternalId.Of(MusicShape.CatalogScheme, pressing.CatalogId)],
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [MusicFields.Title] = FieldValue.OfText(pressing.Title),
            [MusicFields.Provenance] = FieldValue.OfEnumerated(pressing.Provenance),
            [MusicFields.Issuer] = FieldValue.OfText(pressing.Issuer),
            [MusicFields.Country] = FieldValue.OfText(pressing.Country),
            [MusicFields.CarrierFormat] = FieldValue.OfText(pressing.CarrierFormat),
            [MusicFields.CarrierCount] = FieldValue.OfCount(CarrierCount(pressing.Id)),
            [MusicFields.RecordingCount] = FieldValue.OfCount(MusicSeed.RecordingsOf(pressing.Id).Count),
            [MusicFields.ReleaseDate] = FieldValue.OfDate(pressing.ReleaseDate),
            [MusicFields.Disambiguation] = pressing.Disambiguation is null
                ? FieldValue.Absent(FieldValueKind.Text)
                : FieldValue.OfText(pressing.Disambiguation),
            [MusicFields.CatalogId] = FieldValue.OfExternalIdentifier(
                ExternalId.Of(MusicShape.CatalogScheme, pressing.CatalogId)),
        },
    };

    private static ItemView ProjectRecording(SeedRecording recording)
    {
        var readings = new List<CoordinateReading>
        {
            new(
                MusicShape.CarrierPositionSpaceId,
                Coordinate.OfOrdinals(OrdinalPath.Of(recording.Carrier, recording.Position)),
                CoordinateConfidence.Verified),
            new(
                MusicShape.AbsolutePositionSpaceId,
                Coordinate.OfOrdinals(OrdinalPath.Of(recording.AbsolutePosition)),
                CoordinateConfidence.Derived),
        };

        if (recording.SidePosition is not null)
        {
            readings.Add(new CoordinateReading(
                MusicShape.SidePositionSpaceId,
                Coordinate.OfLabel(recording.SidePosition),
                CoordinateConfidence.Asserted));
        }

        return new ItemView
        {
            Ref = Reference(MusicShape.RecordingLevel, recording.Id),
            Parent = Reference(MusicShape.PressingLevel, recording.PressingId),
            Title = recording.Title,
            SortTitle = recording.Title,
            SortIndex = recording.AbsolutePosition,
            HasChildren = false,
            FormatFamilyId = MusicShape.AudioFamilyId,
            Coordinates = CoordinateSet.Of([.. readings]),
            ExternalIds = [ExternalId.Of(MusicShape.CatalogScheme, recording.CatalogId)],
            Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                [MusicFields.Title] = FieldValue.OfText(recording.Title),
                [MusicFields.Position] = FieldValue.OfOrdinal(
                    OrdinalPath.Of(recording.Carrier, recording.Position)),
                [MusicFields.Duration] = FieldValue.OfDuration(recording.Duration),
                [MusicFields.Performer] = recording.Performer is null
                    ? FieldValue.Absent(FieldValueKind.Text)
                    : FieldValue.OfText(recording.Performer),
                [MusicFields.Explicit] = FieldValue.OfBoolean(false),
                [MusicFields.State] = FieldValue.OfEnumerated(MusicStates.Missing),
                [MusicFields.CatalogId] = FieldValue.OfExternalIdentifier(
                    ExternalId.Of(MusicShape.CatalogScheme, recording.CatalogId)),
            },
        };
    }

    private static int CatalogPreferredRunLength(int workId)
    {
        var pressings = MusicSeed.PressingsOf(workId);
        if (pressings.Count == 0)
        {
            return 0;
        }

        var preferred = pressings.FirstOrDefault(pressing => pressing.IsCatalogPreference)
            ?? pressings[0];

        return MusicSeed.RecordingsOf(preferred.Id).Count;
    }

    private static int CarrierCount(int pressingId)
    {
        var recordings = MusicSeed.RecordingsOf(pressingId);
        return recordings.Count == 0 ? 0 : recordings.Max(recording => recording.Carrier);
    }

    private static bool Matches(ItemView view, string fieldId, IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0 || !view.Fields.TryGetValue(fieldId, out var value))
        {
            return true;
        }

        if (value.Items is { } items)
        {
            return items.Any(item => allowed.Contains(Canonical(item), StringComparer.Ordinal));
        }

        return allowed.Contains(Canonical(value), StringComparer.Ordinal);
    }

    private static string Canonical(FieldValue value) => value.Kind switch
    {
        FieldValueKind.Boolean => value.Flag?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        FieldValueKind.Integer or FieldValueKind.ByteSize or FieldValueKind.Count =>
            value.Number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        FieldValueKind.Decimal or FieldValueKind.Ratio =>
            value.Real?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        FieldValueKind.Date => value.Date?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        FieldValueKind.Instant => value.Instant?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        FieldValueKind.Ordinal => value.Ordinals?.ToString() ?? string.Empty,
        FieldValueKind.ExternalIdentifier => value.External?.ToString() ?? string.Empty,
        _ => value.Text ?? string.Empty,
    };

    private static List<ItemView> Sort(List<ItemView> views, string? sortFieldId, bool descending)
    {
        var ordered = sortFieldId is null
            ? views.OrderBy(view => view.SortIndex).ThenBy(view => view.SortTitle, StringComparer.Ordinal)
            : views.OrderBy(view => SortKey(view, sortFieldId), StringComparer.Ordinal);

        return descending ? ordered.Reverse().ToList() : ordered.ToList();
    }

    private static string SortKey(ItemView view, string fieldId) =>
        view.Fields.TryGetValue(fieldId, out var value) ? Canonical(value) : view.Title;

    private static WorkbenchProposal ProposeManualImport(IReadOnlyDictionary<string, string> inputs)
    {
        var pressingId = ReadInt(inputs, PressingInputId);
        var files = ReadList(inputs, FilesInputId);
        var recordings = pressingId is null
            ? []
            : MusicSeed.RecordingsOf(pressingId.Value);

        var issues = new List<ValidationFailure>();

        if (pressingId is null)
        {
            issues.Add(new ValidationFailure(
                PressingInputId,
                "Name the pressing the files belong to; the running order differs between them.",
                ValidationSeverity.Warning));
        }

        var rows = new List<WorkbenchRow>(files.Count);

        for (var index = 0; index < files.Count; index++)
        {
            var candidate = index < recordings.Count ? recordings[index] : null;

            rows.Add(new WorkbenchRow
            {
                RowId = string.Create(CultureInfo.InvariantCulture, $"row-{index + 1}"),

                // Position in the folder listing is a weak signal and is declared as one. A real
                // implementation scores every file against every recording and solves the assignment; the
                // shape of the answer - a row per file, an editable target, a confidence - is the same.
                Confidence = candidate is null ? MatchConfidence.None : MatchConfidence.Low,
                IncludedByDefault = candidate is not null,
                Values = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [MusicWorkbench.PathColumnId] = FieldValue.OfFilePath(files[index]),
                    [MusicWorkbench.TargetColumnId] = candidate is null
                        ? FieldValue.Absent(FieldValueKind.Reference)
                        : FieldValue.OfReference(
                            Reference(MusicShape.RecordingLevel, candidate.Id)),
                    [MusicWorkbench.TargetTitleColumnId] = FieldValue.OfText(
                        candidate?.Title ?? string.Empty),
                    [MusicWorkbench.PositionColumnId] = candidate is null
                        ? FieldValue.Absent(FieldValueKind.Ordinal)
                        : FieldValue.OfOrdinal(
                            OrdinalPath.Of(candidate.Carrier, candidate.Position)),
                    [MusicWorkbench.QualityColumnId] = FieldValue.OfEnumerated(
                        MusicQualityModel.TierForExtension(files[index]).Name),
                },
                Issues = candidate is null
                    ?
                    [
                        new ValidationFailure(
                            MusicWorkbench.TargetColumnId,
                            "No recording is free for this file.",
                            ValidationSeverity.Warning),
                    ]
                    : [],
            });
        }

        return new WorkbenchProposal
        {
            WorkbenchId = ManualImportWorkbenchId,
            Rows = rows,
            Issues = issues,
        };
    }

    private static WorkbenchProposal ProposeInteractiveSearch(IReadOnlyDictionary<string, string> inputs)
    {
        var workId = ReadInt(inputs, MusicWorkbench.WorkInputId);
        var work = MusicSeed.Works.FirstOrDefault(candidate => candidate.Id == workId);

        if (work is null)
        {
            return new WorkbenchProposal
            {
                WorkbenchId = InteractiveSearchWorkbenchId,
                Rows = [],
                Issues =
                [
                    new ValidationFailure(MusicWorkbench.WorkInputId, "Name a work to search for."),
                ],
            };
        }

        var rows = MusicSeed.PressingsOf(work.Id)
            .Select((pressing, index) => new WorkbenchRow
            {
                RowId = string.Create(CultureInfo.InvariantCulture, $"pressing-{pressing.Id}"),
                Confidence = pressing.IsCatalogPreference
                    ? MatchConfidence.High
                    : MatchConfidence.Medium,
                IncludedByDefault = index == 0,
                Values = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [MusicWorkbench.CandidateTitleColumnId] = FieldValue.OfText(
                        MusicQueryPlanner.CandidateTitle(work, pressing)),
                    [MusicWorkbench.TargetColumnId] = FieldValue.OfReference(
                        Reference(MusicShape.PressingLevel, pressing.Id)),
                    [MusicWorkbench.QualityColumnId] = FieldValue.OfEnumerated("FLAC"),
                },
            })
            .ToList();

        return new WorkbenchProposal
        {
            WorkbenchId = InteractiveSearchWorkbenchId,
            Rows = rows,
        };
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string> inputs, string key) =>
        inputs.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static List<string> ReadList(IReadOnlyDictionary<string, string> inputs, string key) =>
        inputs.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)
            ? [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];
}
