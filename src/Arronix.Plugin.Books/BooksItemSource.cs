// Shape, provider, intent and wire contracts are experimental; this extension implements all four.
#pragma warning disable ARX0013, ARX0015, ARX0016, ARX0017
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Plugin.Books.Seed;

namespace Arronix.Plugin.Books;

/// <summary>
/// Projects this extension's catalog, and answers for the working surfaces it declared.
/// </summary>
/// <remarks>
/// Catalog only. What a reader wants, which manifestation they pinned and which files they hold are the
/// host's to store; nothing here can see any of it, which is why the projection has no slot for it.
/// </remarks>
public sealed class BooksItemSource : IMediaItemSource
{
    /// <summary>The working surface that assigns loose files to manifestations and their parts.</summary>
    public const string ManualImportWorkbenchId = "manual-import";

    /// <summary>The working surface that chooses among candidate releases by hand.</summary>
    public const string InteractiveSearchWorkbenchId = "interactive-search";

    /// <summary>The input naming the folder a manual import proposal is built from.</summary>
    public const string FolderInputId = "folder";

    /// <summary>The input naming the file names a manual import proposal is built from.</summary>
    public const string FilesInputId = "files";

    /// <summary>The input naming the manifestation a manual import proposal assigns against.</summary>
    public const string ManifestationInputId = "edition";

    /// <summary>The input naming the work an interactive search is for.</summary>
    public const string WorkInputId = "book";

    /// <inheritdoc />
    public MediaKindId MediaKind => BooksShape.Kind;

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

        // Books carry several competing public identifier namespaces, and a caller may hold any of them.
        // Resolving all three here is what stops every caller having to know which is which.
        if (string.Equals(externalId.Scheme, BooksShape.BookNumberScheme, StringComparison.Ordinal)
            || string.Equals(externalId.Scheme, BooksShape.RetailerScheme, StringComparison.Ordinal))
        {
            var byIdentifier = BooksSeed.Manifestations.FirstOrDefault(manifestation =>
                string.Equals(
                    string.Equals(externalId.Scheme, BooksShape.BookNumberScheme, StringComparison.Ordinal)
                        ? manifestation.BookNumber
                        : manifestation.RetailerId,
                    externalId.Value,
                    StringComparison.Ordinal));

            return Task.FromResult(
                byIdentifier is null
                    ? null
                    : (MediaItemRef?)Reference(BooksShape.ManifestationLevel, byIdentifier.Id));
        }

        if (!string.Equals(externalId.Scheme, BooksShape.CatalogScheme, StringComparison.Ordinal))
        {
            return Task.FromResult<MediaItemRef?>(null);
        }

        foreach (var writer in BooksSeed.Writers)
        {
            if (string.Equals(writer.CatalogId, externalId.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<MediaItemRef?>(Reference(BooksShape.WriterLevel, writer.Id));
            }
        }

        foreach (var work in BooksSeed.Works)
        {
            if (string.Equals(work.CatalogId, externalId.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<MediaItemRef?>(Reference(BooksShape.WorkLevel, work.Id));
            }
        }

        foreach (var manifestation in BooksSeed.Manifestations)
        {
            if (string.Equals(manifestation.CatalogId, externalId.Value, StringComparison.Ordinal))
            {
                return Task.FromResult<MediaItemRef?>(
                    Reference(BooksShape.ManifestationLevel, manifestation.Id));
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

    private static MediaItemRef Reference(MediaLevelId level, int id) =>
        new(BooksShape.Kind, level, MediaItemId.FromInt64(id));

    private List<ItemView> ProjectLevel(MediaLevelId level, MediaItemRef? parent)
    {
        if (level == BooksShape.WriterLevel)
        {
            return BooksSeed.Writers.Select(ProjectWriter).ToList();
        }

        if (level == BooksShape.WorkLevel)
        {
            return BooksSeed.Works
                .Where(work => parent is null || work.WriterId == parent.Value.Id.Value)
                .Select(ProjectWork)
                .ToList();
        }

        if (level == BooksShape.ManifestationLevel)
        {
            return BooksSeed.Manifestations
                .Where(manifestation => parent is null || manifestation.WorkId == parent.Value.Id.Value)
                .Select(ProjectManifestation)
                .ToList();
        }

        return [];
    }

    private static ItemView ProjectWriter(SeedWriter writer) => new()
    {
        Ref = Reference(BooksShape.WriterLevel, writer.Id),
        Parent = null,
        Title = writer.Name,
        SortTitle = writer.SortName,
        SortIndex = writer.Id,
        HasChildren = true,
        ExternalIds = [ExternalId.Of(BooksShape.CatalogScheme, writer.CatalogId)],
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [BooksFields.Name] = FieldValue.OfText(writer.Name),
            [BooksFields.SortName] = FieldValue.OfText(writer.SortName),
            [BooksFields.Overview] = FieldValue.OfMultilineText(writer.Overview),
            [BooksFields.Genres] = FieldValue.OfItems(
                FieldValueKind.Text,
                writer.Genres.Select(FieldValue.OfText).ToList()),
            [BooksFields.Disambiguation] = FieldValue.Absent(FieldValueKind.Text),
            [BooksFields.CatalogId] = FieldValue.OfExternalIdentifier(
                ExternalId.Of(BooksShape.CatalogScheme, writer.CatalogId)),
            [BooksFields.Artwork] = FieldValue.Absent(FieldValueKind.Artwork),
        },
    };

    private static ItemView ProjectWork(SeedWork work)
    {
        var membership = BooksSeed.PrimaryMembershipOf(work.Id);
        var collection = membership is null
            ? null
            : BooksSeed.Collections.FirstOrDefault(
                candidate => candidate.Id == membership.Value.CollectionId);

        return new ItemView
        {
            Ref = Reference(BooksShape.WorkLevel, work.Id),
            Parent = Reference(BooksShape.WriterLevel, work.WriterId),
            Title = work.Title,
            SortTitle = work.Title,
            SortIndex = work.ReleaseDate.DayNumber,
            HasChildren = true,
            Coordinates = CoordinateSet.Of(
                new CoordinateReading(
                    BooksShape.WorkSpaceId,
                    Coordinate.Singleton,
                    CoordinateConfidence.Verified)),
            ExternalIds = [ExternalId.Of(BooksShape.CatalogScheme, work.CatalogId)],
            Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
            {
                [BooksFields.Title] = FieldValue.OfText(work.Title),
                [BooksFields.Subtitle] = work.Subtitle is null
                    ? FieldValue.Absent(FieldValueKind.Text)
                    : FieldValue.OfText(work.Subtitle),
                [BooksFields.ReleaseDate] = FieldValue.OfDate(work.ReleaseDate),
                [BooksFields.Popularity] = FieldValue.OfDecimal(work.Popularity),
                [BooksFields.CollectionName] = collection is null
                    ? FieldValue.Absent(FieldValueKind.Text)
                    : FieldValue.OfText(collection.Title),
                [BooksFields.CollectionPosition] = membership is null
                    ? FieldValue.Absent(FieldValueKind.Text)
                    : FieldValue.OfText(membership.Value.Position),
                [BooksFields.ManifestationCount] = FieldValue.OfCount(
                    BooksSeed.ManifestationsOf(work.Id).Count),
                [BooksFields.State] = FieldValue.OfEnumerated(
                    work.ReleaseDate > DateOnly.FromDateTime(DateTime.UtcNow)
                        ? BooksStates.Unpublished
                        : BooksStates.Missing),
                [BooksFields.Overview] = FieldValue.Absent(FieldValueKind.MultilineText),
                [BooksFields.CatalogId] = FieldValue.OfExternalIdentifier(
                    ExternalId.Of(BooksShape.CatalogScheme, work.CatalogId)),
                [BooksFields.Artwork] = FieldValue.Absent(FieldValueKind.Artwork),
            },
        };
    }

    private static ItemView ProjectManifestation(SeedManifestation manifestation) => new()
    {
        Ref = Reference(BooksShape.ManifestationLevel, manifestation.Id),
        Parent = Reference(BooksShape.WorkLevel, manifestation.WorkId),
        Title = manifestation.Title,
        SortTitle = manifestation.Title,
        SortIndex = manifestation.ReleaseDate.DayNumber,
        HasChildren = false,

        // The one place the two families become a per-item fact rather than a declaration.
        FormatFamilyId = manifestation.Flavor,
        Coordinates = CoordinateSet.Of(
            new CoordinateReading(
                BooksShape.ManifestationSpaceId,
                Coordinate.OfLabel(manifestation.CarrierDescription),
                CoordinateConfidence.Asserted)),
        ExternalIds = BuildExternalIds(manifestation),
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [BooksFields.Title] = FieldValue.OfText(manifestation.Title),
            [BooksFields.Flavor] = FieldValue.OfEnumerated(manifestation.Flavor),
            [BooksFields.CarrierDescription] = FieldValue.OfText(manifestation.CarrierDescription),
            [BooksFields.Issuer] = FieldValue.OfText(manifestation.Issuer),
            [BooksFields.PageCount] = manifestation.PageCount is { } pages
                ? FieldValue.OfCount(pages)
                : FieldValue.Absent(FieldValueKind.Count),
            [BooksFields.RunningTime] = manifestation.RunningTime is { } running
                ? FieldValue.OfDuration(running)
                : FieldValue.Absent(FieldValueKind.Duration),
            [BooksFields.Language] = FieldValue.OfLanguage(
                new Language(manifestation.LanguageCode, manifestation.LanguageCode)),
            [BooksFields.ReleaseDate] = FieldValue.OfDate(manifestation.ReleaseDate),
            [BooksFields.BookNumber] = manifestation.BookNumber is null
                ? FieldValue.Absent(FieldValueKind.ExternalIdentifier)
                : FieldValue.OfExternalIdentifier(
                    ExternalId.Of(BooksShape.BookNumberScheme, manifestation.BookNumber)),
            [BooksFields.RetailerId] = manifestation.RetailerId is null
                ? FieldValue.Absent(FieldValueKind.ExternalIdentifier)
                : FieldValue.OfExternalIdentifier(
                    ExternalId.Of(BooksShape.RetailerScheme, manifestation.RetailerId)),
            [BooksFields.Disambiguation] = FieldValue.Absent(FieldValueKind.Text),
            [BooksFields.State] = FieldValue.OfEnumerated(BooksStates.Missing),
            [BooksFields.CatalogId] = FieldValue.OfExternalIdentifier(
                ExternalId.Of(BooksShape.CatalogScheme, manifestation.CatalogId)),
        },
    };

    private static List<ExternalId> BuildExternalIds(SeedManifestation manifestation)
    {
        var ids = new List<ExternalId>
        {
            ExternalId.Of(BooksShape.CatalogScheme, manifestation.CatalogId),
        };

        if (manifestation.BookNumber is not null)
        {
            ids.Add(ExternalId.Of(BooksShape.BookNumberScheme, manifestation.BookNumber));
        }

        if (manifestation.RetailerId is not null)
        {
            ids.Add(ExternalId.Of(BooksShape.RetailerScheme, manifestation.RetailerId));
        }

        return ids;
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
        FieldValueKind.Language => value.Language?.Code ?? string.Empty,
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
        var manifestationId = ReadInt(inputs, ManifestationInputId);
        var files = ReadList(inputs, FilesInputId);
        var manifestation = manifestationId is null
            ? null
            : BooksSeed.Manifestations.FirstOrDefault(
                candidate => candidate.Id == manifestationId.Value);

        var issues = new List<ValidationFailure>();

        if (manifestation is null)
        {
            issues.Add(new ValidationFailure(
                ManifestationInputId,
                "Name the edition these files belong to.",
                ValidationSeverity.Warning));
        }
        else if (manifestation.PartCount > 1 && files.Count != manifestation.PartCount)
        {
            // The reason the link's ordinal exists. A manifestation spread over parts is complete only
            // when every part is present, and the count is the extension's knowledge, not the platform's.
            issues.Add(new ValidationFailure(
                null,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"This edition is in {manifestation.PartCount} parts and {files.Count} files were offered."),
                ValidationSeverity.Warning));
        }

        var rows = new List<WorkbenchRow>(files.Count);

        for (var index = 0; index < files.Count; index++)
        {
            var family = BooksQualityModel.FamilyForExtension(files[index]);
            var flavorMatches = manifestation is not null
                && string.Equals(family, manifestation.Flavor, StringComparison.Ordinal);

            var rowIssues = new List<ValidationFailure>();

            if (family is null)
            {
                rowIssues.Add(new ValidationFailure(
                    BooksWorkbench.PathColumnId,
                    "No format family claims this extension.",
                    ValidationSeverity.Error));
            }
            else if (manifestation is not null && !flavorMatches)
            {
                rowIssues.Add(new ValidationFailure(
                    BooksWorkbench.PartColumnId,
                    "This file's family is not the family of the edition it is being assigned to.",
                    ValidationSeverity.Warning));
            }

            rows.Add(new WorkbenchRow
            {
                RowId = string.Create(CultureInfo.InvariantCulture, $"row-{index + 1}"),
                Confidence = manifestation is null
                    ? MatchConfidence.None
                    : flavorMatches ? MatchConfidence.Medium : MatchConfidence.Low,
                IncludedByDefault = manifestation is not null && family is not null,
                Values = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [BooksWorkbench.PathColumnId] = FieldValue.OfFilePath(files[index]),
                    [BooksWorkbench.TargetColumnId] = manifestation is null
                        ? FieldValue.Absent(FieldValueKind.Reference)
                        : FieldValue.OfReference(
                            Reference(BooksShape.ManifestationLevel, manifestation.Id)),
                    [BooksWorkbench.PartColumnId] = FieldValue.OfInteger(index + 1),
                    [BooksWorkbench.FlavorColumnId] = family is null
                        ? FieldValue.Absent(FieldValueKind.Enumerated)
                        : FieldValue.OfEnumerated(family),
                    [BooksWorkbench.QualityColumnId] = FieldValue.OfEnumerated(
                        BooksQualityModel.TierForExtension(files[index]).Name),
                },
                Issues = rowIssues,
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
        var workId = ReadInt(inputs, WorkInputId);
        var work = BooksSeed.Works.FirstOrDefault(candidate => candidate.Id == workId);

        if (work is null)
        {
            return new WorkbenchProposal
            {
                WorkbenchId = InteractiveSearchWorkbenchId,
                Rows = [],
                Issues = [new ValidationFailure(WorkInputId, "Name a book to search for.")],
            };
        }

        var rows = BooksSeed.ManifestationsOf(work.Id)
            .Select((manifestation, index) => new WorkbenchRow
            {
                RowId = string.Create(CultureInfo.InvariantCulture, $"edition-{manifestation.Id}"),
                Confidence = manifestation.IsCatalogPreference
                    ? MatchConfidence.High
                    : MatchConfidence.Medium,
                IncludedByDefault = index == 0,
                Values = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [BooksWorkbench.CandidateTitleColumnId] = FieldValue.OfText(
                        BooksQueryPlanner.CandidateTitle(work, manifestation)),
                    [BooksWorkbench.TargetColumnId] = FieldValue.OfReference(
                        Reference(BooksShape.ManifestationLevel, manifestation.Id)),
                    [BooksWorkbench.FlavorColumnId] = FieldValue.OfEnumerated(manifestation.Flavor),
                    [BooksWorkbench.QualityColumnId] = FieldValue.OfEnumerated(
                        manifestation.Flavor == BooksShape.WrittenFamilyId ? "EPUB" : "M4B"),
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
