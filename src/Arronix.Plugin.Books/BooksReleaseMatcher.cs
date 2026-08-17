// The media-shape contracts are experimental; this extension is a reference implementer.
#pragma warning disable ARX0013
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Books.Seed;

namespace Arronix.Plugin.Books;

/// <summary>
/// Decides which items a release name, a file name or a folder name refers to.
/// </summary>
/// <remarks>
/// <para>
/// A name resolves to a single manifestation, and that manifestation is the only item it satisfies -
/// unlike recorded music, where a name resolves to a pressing and satisfies its whole running order. Both
/// are expressible because the outcome is a list of items and the shape says how many to expect.
/// </para>
/// <para>
/// Book names carry more identifying signal than any other kind and less structure. A thirteen-digit
/// number settles the question outright; failing that, the collection a work belongs to is often embedded
/// in the name in place of the title, so a candidate's collection labels have to be tried as alternative
/// titles. That is a use of the grouping axis in matching, and it is the reason a grouping axis has to be
/// visible to the extension rather than being purely a way to browse.
/// </para>
/// </remarks>
public sealed class BooksReleaseMatcher : IReleaseMatcher
{
    private readonly BooksReleaseParser _parser = new();

    /// <inheritdoc />
    public MediaKindId MediaKind => BooksShape.Kind;

    /// <inheritdoc />
    public Task<MatchOutcome> MatchAsync(
        MatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var externalId in request.ExternalIds)
        {
            var direct = ByExternalId(externalId);

            if (direct is not null)
            {
                return Task.FromResult(Matched(direct, MatchConfidence.Exact, []));
            }
        }

        var parsed = request.Parsed ?? _parser.Parse(request.Text);

        if (parsed is null)
        {
            return Task.FromResult(Rejected("The name does not read as a book release."));
        }

        var metadata = parsed.AdditionalMetadata;

        if (Read(metadata, BooksReleaseParser.BookNumberKey) is { } bookNumber)
        {
            var byNumber = ByExternalId(ExternalId.Of(BooksShape.BookNumberScheme, bookNumber));

            if (byNumber is not null)
            {
                return Task.FromResult(Matched(byNumber, MatchConfidence.Exact, []));
            }
        }

        var work = FindWork(parsed, request.Scope);

        if (work is null)
        {
            return Task.FromResult(Rejected("No book in the catalog answers to that title."));
        }

        var flavor = Read(metadata, BooksReleaseParser.FlavorKey);
        var manifestation = ChooseManifestation(work, flavor);

        if (manifestation is null)
        {
            return Task.FromResult(Rejected(
                flavor is null
                    ? "The book has no edition to match against."
                    : "The book has no edition in the format this release carries."));
        }

        var warnings = new List<string>();

        if (flavor is null)
        {
            // Stated rather than guessed at silently. The flavor decides which ladder the copy is ranked
            // on, so getting it wrong is not a cosmetic error.
            warnings.Add(
                "The release name does not say whether this is a written or a spoken copy; the edition's "
                + "own format was assumed.");
        }

        if (manifestation.PartCount > 1)
        {
            warnings.Add(
                "This edition is spread over several files, so one file alone does not complete it.");
        }

        return Task.FromResult(Matched(
            manifestation,
            Confidence(work, parsed, flavor, manifestation),
            warnings));
    }

    private static SeedManifestation? ByExternalId(ExternalId externalId)
    {
        if (string.Equals(externalId.Scheme, BooksShape.BookNumberScheme, StringComparison.Ordinal))
        {
            return BooksSeed.Manifestations.FirstOrDefault(manifestation =>
                string.Equals(manifestation.BookNumber, externalId.Value, StringComparison.Ordinal));
        }

        if (string.Equals(externalId.Scheme, BooksShape.RetailerScheme, StringComparison.Ordinal))
        {
            return BooksSeed.Manifestations.FirstOrDefault(manifestation =>
                string.Equals(manifestation.RetailerId, externalId.Value, StringComparison.Ordinal));
        }

        if (string.Equals(externalId.Scheme, BooksShape.CatalogScheme, StringComparison.Ordinal))
        {
            return BooksSeed.Manifestations.FirstOrDefault(manifestation =>
                string.Equals(manifestation.CatalogId, externalId.Value, StringComparison.Ordinal));
        }

        return null;
    }

    private static SeedWork? FindWork(ParsedRelease parsed, MediaItemRef? scope)
    {
        var candidates = BooksSeed.Works.AsEnumerable();
        var writerId = ScopeToWriter(scope)
            ?? WriterIdFor(Read(parsed.AdditionalMetadata, BooksReleaseParser.CreatorKey));

        if (writerId is { } id)
        {
            candidates = candidates.Where(work => work.WriterId == id);
        }

        var materialized = candidates.ToList();
        var wanted = Normalize(parsed.Title);
        var wantedWithoutSubtitle = Normalize(
            Read(parsed.AdditionalMetadata, BooksReleaseParser.TitleWithoutSubtitleKey) ?? parsed.Title);

        foreach (var work in materialized)
        {
            var full = Normalize(work.Title);
            var stem = Normalize(StripSubtitle(work.Title));

            if (full == wanted || stem == wanted || full == wantedWithoutSubtitle
                || stem == wantedWithoutSubtitle)
            {
                return work;
            }
        }

        // Release names embed a collection and a position where a title would go far more often for books
        // than for anything else, so the collection labels are tried as alternative titles.
        foreach (var work in materialized)
        {
            foreach (var membership in BooksSeed.MembershipsOf(work.Id))
            {
                var collection = BooksSeed.Collections.FirstOrDefault(
                    candidate => candidate.Id == membership.CollectionId);

                if (collection is null)
                {
                    continue;
                }

                var synthesized = Normalize(collection.Title + membership.Position + work.Title);

                if (synthesized == wanted || synthesized == wantedWithoutSubtitle)
                {
                    return work;
                }
            }
        }

        return null;
    }

    private static SeedManifestation? ChooseManifestation(SeedWork work, string? flavor)
    {
        var manifestations = BooksSeed.ManifestationsOf(work.Id);

        if (manifestations.Count == 0)
        {
            return null;
        }

        if (flavor is not null)
        {
            var inFlavor = manifestations
                .Where(manifestation =>
                    string.Equals(manifestation.Flavor, flavor, StringComparison.Ordinal))
                .ToList();

            if (inFlavor.Count == 0)
            {
                return null;
            }

            return inFlavor.FirstOrDefault(manifestation => manifestation.IsCatalogPreference)
                ?? inFlavor[0];
        }

        return manifestations.FirstOrDefault(manifestation => manifestation.IsCatalogPreference)
            ?? manifestations[0];
    }

    private static long? ScopeToWriter(MediaItemRef? scope)
    {
        if (scope is not { } reference)
        {
            return null;
        }

        if (reference.Level == BooksShape.WriterLevel)
        {
            return reference.Id.Value;
        }

        if (reference.Level == BooksShape.WorkLevel)
        {
            return BooksSeed.Works.FirstOrDefault(work => work.Id == reference.Id.Value)?.WriterId;
        }

        if (reference.Level == BooksShape.ManifestationLevel)
        {
            var manifestation = BooksSeed.Manifestations.FirstOrDefault(
                candidate => candidate.Id == reference.Id.Value);

            return manifestation is null
                ? null
                : BooksSeed.Works.FirstOrDefault(work => work.Id == manifestation.WorkId)?.WriterId;
        }

        return null;
    }

    private static int? WriterIdFor(string? credit)
    {
        if (credit is null)
        {
            return null;
        }

        var wanted = Normalize(credit);

        foreach (var writer in BooksSeed.Writers)
        {
            // "Le Guin, Ursula K." and "Ursula K. Le Guin" are the same person written two ways, and both
            // forms appear in the wild. Normalizing away punctuation and order handles the common case.
            if (Normalize(writer.Name) == wanted || Normalize(writer.SortName) == wanted
                || Normalize(Reorder(writer.SortName)) == wanted)
            {
                return writer.Id;
            }
        }

        return null;
    }

    private static string Reorder(string sortName)
    {
        var comma = sortName.IndexOf(',', StringComparison.Ordinal);

        return comma < 0
            ? sortName
            : sortName[(comma + 1)..].Trim() + " " + sortName[..comma].Trim();
    }

    private static MatchConfidence Confidence(
        SeedWork work,
        ParsedRelease parsed,
        string? flavor,
        SeedManifestation manifestation)
    {
        var titleExact = Normalize(work.Title) == Normalize(parsed.Title);
        var flavorKnown = flavor is not null
            && string.Equals(manifestation.Flavor, flavor, StringComparison.Ordinal);

        return (titleExact, flavorKnown) switch
        {
            (true, true) => MatchConfidence.High,
            (true, false) => MatchConfidence.Medium,
            (false, true) => MatchConfidence.Medium,
            (false, false) => MatchConfidence.Low,
        };
    }

    private static MatchOutcome Matched(
        SeedManifestation manifestation,
        MatchConfidence confidence,
        IReadOnlyList<string> warnings) => new()
    {
        Units =
        [
            new MediaItemRef(
                BooksShape.Kind,
                BooksShape.ManifestationLevel,
                MediaItemId.FromInt64(manifestation.Id)),
        ],
        Confidence = confidence,
        Coordinates = CoordinateSet.Of(
            new CoordinateReading(
                BooksShape.ManifestationSpaceId,
                Coordinate.OfLabel(manifestation.CarrierDescription),
                CoordinateConfidence.Derived)),
        Warnings = warnings,
    };

    private static MatchOutcome Rejected(string reason) => new()
    {
        Units = [],
        Confidence = MatchConfidence.None,
        RejectionReason = reason,
    };

    private static string? Read(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null && metadata.TryGetValue(key, out var value) ? value : null;

    private static string StripSubtitle(string title)
    {
        var colon = title.IndexOf(':', StringComparison.Ordinal);

        return colon < 0 ? title : title[..colon];
    }

    private static string Normalize(string value)
    {
        var buffer = new char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToUpperInvariant(character);
            }
        }

        return new string(buffer, 0, length);
    }
}
