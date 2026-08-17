// The media-shape contracts are experimental; this extension is a reference implementer.
#pragma warning disable ARX0013
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Music.Seed;

namespace Arronix.Plugin.Music;

/// <summary>
/// Decides which items a release name, a file name or a folder name refers to.
/// </summary>
/// <remarks>
/// <para>
/// The single most kind-specific step there is, and the platform never attempts it. What makes music's
/// version distinctive is that the answer is not one item: a name resolves to a <em>pressing</em>, and
/// the items it satisfies are that pressing's whole running order. Two pressings of the same work give
/// two different answers to the same string, which is why the choice of pressing has to be part of the
/// match and not a detail settled afterwards.
/// </para>
/// <para>
/// Provenance matters. A string taken from a release name is scene-shaped and may use a different running
/// order to the catalog's; a string taken from a tag is catalog-shaped. The source is carried on the
/// request rather than inferred, so the same method serves the grab path and the import path.
/// </para>
/// </remarks>
public sealed class MusicReleaseMatcher : IReleaseMatcher
{
    private readonly MusicReleaseParser _parser = new();

    /// <inheritdoc />
    public MediaKindId MediaKind => MusicShape.Kind;

    /// <inheritdoc />
    public Task<MatchOutcome> MatchAsync(
        MatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var externalId in request.ExternalIds)
        {
            if (TryMatchExternal(externalId, out var byId))
            {
                return Task.FromResult(byId);
            }
        }

        var parsed = request.Parsed ?? _parser.Parse(request.Text);

        if (parsed is null)
        {
            return Task.FromResult(Rejected("The name does not read as a music release."));
        }

        var performerHint = parsed.AdditionalMetadata is not null
            && parsed.AdditionalMetadata.TryGetValue(MusicReleaseParser.CreatorKey, out var creator)
                ? creator
                : null;

        var work = FindWork(parsed.Title, performerHint, request.Scope);

        if (work is null)
        {
            return Task.FromResult(Rejected("No album in the catalog answers to that title."));
        }

        var pressing = ChoosePressing(work, parsed, request.Source);

        if (pressing is null)
        {
            return Task.FromResult(Rejected("The album has no pressing to match against."));
        }

        var recordings = MusicSeed.RecordingsOf(pressing.Id);

        var warnings = new List<string>();

        if (!pressing.IsCatalogPreference)
        {
            // Said out loud rather than left to be discovered: accepting this match is what re-points the
            // work at a different running order.
            warnings.Add(
                "This match selects a pressing other than the catalog's preferred one, which changes "
                + "what completeness is counted against.");
        }

        return Task.FromResult(new MatchOutcome
        {
            Units =
            [
                .. recordings.Select(recording => new MediaItemRef(
                    MusicShape.Kind,
                    MusicShape.RecordingLevel,
                    MediaItemId.FromInt64(recording.Id))),
            ],
            Confidence = Confidence(work, parsed, pressing.IsCatalogPreference),
            Coordinates = CoordinateSet.Empty,
            Warnings = warnings,
        });
    }

    private static bool TryMatchExternal(ExternalId externalId, out MatchOutcome outcome)
    {
        outcome = Rejected("Unknown identifier.");

        if (!string.Equals(externalId.Scheme, MusicShape.CatalogScheme, StringComparison.Ordinal))
        {
            return false;
        }

        var pressing = MusicSeed.Pressings.FirstOrDefault(
            candidate => string.Equals(candidate.CatalogId, externalId.Value, StringComparison.Ordinal));

        if (pressing is null)
        {
            return false;
        }

        outcome = new MatchOutcome
        {
            Units =
            [
                .. MusicSeed.RecordingsOf(pressing.Id).Select(recording => new MediaItemRef(
                    MusicShape.Kind,
                    MusicShape.RecordingLevel,
                    MediaItemId.FromInt64(recording.Id))),
            ],
            Confidence = MatchConfidence.Exact,
        };

        return true;
    }

    private static SeedWork? FindWork(string title, string? performerHint, MediaItemRef? scope)
    {
        var candidates = MusicSeed.Works.AsEnumerable();

        if (scope is { } reference)
        {
            var scopedPerformer = ScopeToPerformer(reference);
            if (scopedPerformer is { } performerId)
            {
                candidates = candidates.Where(work => work.PerformerId == performerId);
            }
        }
        else if (performerHint is not null)
        {
            var performer = MusicSeed.Performers.FirstOrDefault(
                candidate => Normalize(candidate.Name) == Normalize(performerHint));

            if (performer is not null)
            {
                candidates = candidates.Where(work => work.PerformerId == performer.Id);
            }
        }

        var wanted = Normalize(title);

        return candidates.FirstOrDefault(work => Normalize(work.Title) == wanted)
            ?? candidates.FirstOrDefault(work => Normalize(work.Title).StartsWith(wanted, StringComparison.Ordinal));
    }

    private static long? ScopeToPerformer(MediaItemRef reference)
    {
        if (reference.Level == MusicShape.PerformerLevel)
        {
            return reference.Id.Value;
        }

        if (reference.Level == MusicShape.WorkLevel)
        {
            return MusicSeed.Works.FirstOrDefault(work => work.Id == reference.Id.Value)?.PerformerId;
        }

        return null;
    }

    private static SeedPressing? ChoosePressing(SeedWork work, ParsedRelease parsed, MatchSource source)
    {
        var pressings = MusicSeed.PressingsOf(work.Id);

        if (pressings.Count == 0)
        {
            return null;
        }

        // A name that carries a pressing's own distinguishing words picks that pressing outright.
        var named = pressings.FirstOrDefault(pressing =>
            !string.Equals(pressing.Title, work.Title, StringComparison.Ordinal)
            && Normalize(parsed.Title).Contains(
                Normalize(pressing.Title),
                StringComparison.Ordinal));

        if (named is not null)
        {
            return named;
        }

        // Otherwise a year in the name is the strongest available discriminator, and it is only trusted
        // from a release name - a tag's year is the work's, not the pressing's.
        if (source == MatchSource.ReleaseName
            && parsed.Year is { } yearText
            && int.TryParse(yearText, out var year))
        {
            var dated = pressings.FirstOrDefault(pressing => pressing.ReleaseDate.Year == year);
            if (dated is not null)
            {
                return dated;
            }
        }

        return pressings.FirstOrDefault(pressing => pressing.IsCatalogPreference) ?? pressings[0];
    }

    private static MatchConfidence Confidence(SeedWork work, ParsedRelease parsed, bool preferred)
    {
        var titleMatches = Normalize(work.Title) == Normalize(parsed.Title);

        return (titleMatches, preferred) switch
        {
            (true, true) => MatchConfidence.High,
            (true, false) => MatchConfidence.Medium,
            (false, _) => MatchConfidence.Low,
        };
    }

    private static MatchOutcome Rejected(string reason) => new()
    {
        Units = [],
        Confidence = MatchConfidence.None,
        RejectionReason = reason,
    };

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
