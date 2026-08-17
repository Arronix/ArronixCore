#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv;

/// <summary>
/// Resolves an episodic release title, file name or folder name onto one or more catalog units.
/// </summary>
/// <remarks>
/// <para>This is the switchboard, and it is the single most media-specific piece of code in the extension.
/// It has five branches — canonical ordinal pair, calendar date, flat ordinal, whole run, and the
/// provenance-sensitive alias overlay on top of two of them — and every branch reads the <b>release</b>, not
/// the library entry. An entry the catalog marks as flat-numbered still resolves a canonical pair
/// correctly, which is the behavior that makes a single addressing scheme per media kind untenable.</para>
/// <para>The alias overlay is gated on a conjunction: the entry must be marked as using it <b>and</b> the
/// text must have come from a release name. The same text resolves differently depending on where it came
/// from, and <see cref="MatchSource"/> is what carries that distinction across the boundary.</para>
/// <para>The declared span constraint is enforced here as well as by the store. A release may satisfy
/// several units, but never units in different runs — the reference implementation raises a dedicated
/// exception for that case, and refusing the match before it reaches the store is the cheaper place to
/// catch it.</para>
/// </remarks>
public sealed class TvReleaseMatcher : IReleaseMatcher
{
    private readonly TvCatalog _catalog;

    /// <summary>Creates a matcher over a catalog.</summary>
    /// <param name="catalog">The catalog to resolve against.</param>
    public TvReleaseMatcher(TvCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    public Task<MatchOutcome> MatchAsync(MatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Match(request));
    }

    private static MatchOutcome Reject(string reason) => new()
    {
        Units = [],
        Confidence = MatchConfidence.None,
        RejectionReason = reason
    };

    /// <summary>
    /// Resolves a request synchronously.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// Every branch of the resolution is a lookup against an in-memory catalog, so the work is genuinely
    /// synchronous. Exposing it saves the workbench proposal — which is on the same synchronous path — from
    /// blocking on a task that was already completed.
    /// </remarks>
    public MatchOutcome Match(MatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TvTitleParser.TryParse(request.Text, out var parsed) || parsed is null)
        {
            return Reject("The text does not state a position in any declared coordinate space.");
        }

        var warnings = new List<string>();
        var resolvedByIdentifier = false;
        var series = ResolveSeries(request, parsed, warnings, ref resolvedByIdentifier);

        if (series is null)
        {
            return Reject($"No library entry in the catalog matches '{parsed.SeriesTitle}'.");
        }

        // The alias overlay is consulted only when the entry opts into it AND the text came from a release
        // name. Both halves are required; either alone is not enough.
        var aliasEligible = series.UsesAliasNumbering && request.Source == MatchSource.ReleaseName;

        var resolution = parsed.Kind switch
        {
            TvReleaseKind.SeasonEpisode => ResolveOrdinalPair(series, parsed, aliasEligible),
            TvReleaseKind.AirDate => ResolveCalendar(series, parsed),
            TvReleaseKind.Absolute => ResolveFlat(series, parsed, aliasEligible),
            TvReleaseKind.FullSeason => ResolveWholeRun(series, parsed),
            _ => new Resolution([], MatchConfidence.None, false, "The release states no usable position.")
        };

        if (resolution.Rejection is { } rejection)
        {
            return Reject(rejection);
        }

        if (resolution.Units.Count == 0)
        {
            return Reject(
                $"'{series.Title}' has no unit at the position stated by '{request.Text}'.");
        }

        // The declared span constraint, enforced. One file may satisfy several units; it may never satisfy
        // units in different runs.
        var runs = resolution.Units.Select(unit => unit.SeasonNumber).Distinct().ToList();

        if (runs.Count > 1)
        {
            return Reject(
                "A single release cannot satisfy units in more than one run: the shape declares "
                + $"'{TvIds.AiredSpaceId}.{TvIds.SeasonComponentId}' as must-not-span, and this release "
                + $"resolves to runs {string.Join(", ", runs.Select(Format))}.");
        }

        if (resolution.UsedUnverifiedAlias)
        {
            warnings.Add(
                "The position was read from the release-community numbering, where this unit's mapping is "
                + "extrapolated rather than confirmed.");
        }

        if (parsed.IsMultiUnit || resolution.Units.Count > 1)
        {
            warnings.Add(
                $"This release satisfies {Format(resolution.Units.Count)} units. The binding permits it: "
                + "at most one file per unit, but not at most one unit per file.");
        }

        var confidence = resolvedByIdentifier && resolution.Confidence >= MatchConfidence.High
            ? MatchConfidence.Exact
            : resolution.Confidence;

        return new MatchOutcome
        {
            Units = [.. resolution.Units.Select(TvCatalog.ReferenceTo)],
            Confidence = confidence,
            Coordinates = BuildCoordinates(parsed, resolution.Units[0], resolution.UsedAlias),
            Warnings = warnings
        };
    }

    private TvSeriesRecord? ResolveSeries(
        MatchRequest request,
        TvParsedTitle parsed,
        List<string> warnings,
        ref bool byIdentifier)
    {
        foreach (var externalId in request.ExternalIds)
        {
            if (_catalog.TryGetSeriesByExternalId(externalId, out var identified) && identified is not null)
            {
                byIdentifier = true;
                return identified;
            }
        }

        if (request.Scope is { } scope)
        {
            if (scope.Level == TvIds.SeriesLevel
                && _catalog.TryGetSeries(scope.Id.Value, out var scoped)
                && scoped is not null)
            {
                return scoped;
            }

            if (scope.Level == TvIds.EpisodeLevel
                && _catalog.TryGetEpisode(scope.Id.Value, out var scopedUnit)
                && scopedUnit is not null
                && _catalog.TryGetSeries(scopedUnit.SeriesId, out var owner)
                && owner is not null)
            {
                return owner;
            }
        }

        var candidates = _catalog.FindSeriesByTitle(parsed.SeriesTitle);

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (parsed.Year is not { } year)
        {
            warnings.Add(
                $"'{parsed.SeriesTitle}' matches {Format(candidates.Count)} library entries and the release "
                + "states no year.");
            return null;
        }

        var exact = candidates.Where(candidate => candidate.Year == year).ToList();

        return exact.Count == 1 ? exact[0] : null;
    }

    private Resolution ResolveOrdinalPair(TvSeriesRecord series, TvParsedTitle parsed, bool aliasEligible)
    {
        if (parsed.SeasonNumber is not { } season)
        {
            return new Resolution([], MatchConfidence.None, false, "The release states no outer ordinal.");
        }

        var units = new List<TvEpisodeRecord>();
        var usedAlias = false;
        var unverified = false;

        foreach (var episodeNumber in parsed.EpisodeNumbers)
        {
            // The alias space is tried first when it is eligible, because a release that uses it states
            // alias numbers and reading them as canonical ones silently grabs the wrong unit.
            if (aliasEligible
                && _catalog.TryGetByAlias(series.Id, season, episodeNumber, out var byAlias)
                && byAlias is not null)
            {
                units.Add(byAlias);
                usedAlias = true;
                unverified |= byAlias.AliasIsUnverified;
                continue;
            }

            if (_catalog.TryGetByAired(series.Id, season, episodeNumber, out var byAired)
                && byAired is not null)
            {
                units.Add(byAired);
                continue;
            }

            return new Resolution(
                [],
                MatchConfidence.None,
                false,
                $"'{series.Title}' has no unit at {Format(season)}.{Format(episodeNumber)}.");
        }

        var confidence = usedAlias
            ? unverified ? MatchConfidence.Low : MatchConfidence.Medium
            : MatchConfidence.High;

        return new Resolution(units, confidence, usedAlias, null) { UsedUnverifiedAlias = unverified };
    }

    private Resolution ResolveCalendar(TvSeriesRecord series, TvParsedTitle parsed)
    {
        if (parsed.AirDate is not { } airDate)
        {
            return new Resolution([], MatchConfidence.None, false, "The release states no date.");
        }

        var units = _catalog.FindByAirDate(series.Id, airDate);

        if (units.Count == 0)
        {
            return new Resolution(
                [],
                MatchConfidence.None,
                false,
                $"'{series.Title}' transmitted nothing on {airDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.");
        }

        // More than one unit on one date is legal and does happen - a double bill. The date is not a unique
        // key, which is why the calendar space is declared non-dense.
        return new Resolution(
            [.. units],
            units.Count == 1 ? MatchConfidence.High : MatchConfidence.Medium,
            false,
            null);
    }

    private Resolution ResolveFlat(TvSeriesRecord series, TvParsedTitle parsed, bool aliasEligible)
    {
        var units = new List<TvEpisodeRecord>();
        var usedAlias = false;
        var unverified = false;

        foreach (var absolute in parsed.AbsoluteNumbers)
        {
            if (aliasEligible
                && _catalog.TryGetByAliasAbsolute(series.Id, absolute, out var byAlias)
                && byAlias is not null)
            {
                units.Add(byAlias);
                usedAlias = true;
                unverified |= byAlias.AliasIsUnverified;
                continue;
            }

            if (_catalog.TryGetByAbsolute(series.Id, absolute, out var byAbsolute) && byAbsolute is not null)
            {
                units.Add(byAbsolute);
                continue;
            }

            return new Resolution(
                [],
                MatchConfidence.None,
                false,
                $"'{series.Title}' has no unit with flat ordinal {Format(absolute)}.");
        }

        if (units.Count == 0)
        {
            return new Resolution([], MatchConfidence.None, false, "The release states no flat ordinal.");
        }

        var confidence = usedAlias
            ? unverified ? MatchConfidence.Low : MatchConfidence.Medium
            : MatchConfidence.High;

        return new Resolution(units, confidence, usedAlias, null) { UsedUnverifiedAlias = unverified };
    }

    private Resolution ResolveWholeRun(TvSeriesRecord series, TvParsedTitle parsed)
    {
        if (parsed.SeasonNumber is not { } season)
        {
            return new Resolution([], MatchConfidence.None, false, "The release states no run.");
        }

        var units = _catalog.EpisodesInSeason(series.Id, season);

        return units.Count == 0
            ? new Resolution(
                [],
                MatchConfidence.None,
                false,
                $"'{series.Title}' has no run numbered {Format(season)}.")

            // A pack is a sequence span, not a level, and it resolves to every unit in the run. The scope
            // that expresses it names the axis, which no level identifier could.
            : new Resolution([.. units], MatchConfidence.High, false, null);
    }

    private static CoordinateSet BuildCoordinates(
        TvParsedTitle parsed,
        TvEpisodeRecord first,
        bool usedAlias)
    {
        var readings = new List<CoordinateReading>(3);

        // What the release asserted, in the space it asserted it in.
        switch (parsed.Kind)
        {
            case TvReleaseKind.SeasonEpisode when parsed.SeasonNumber is { } season
                && parsed.EpisodeNumbers.Count != 0:
                readings.Add(new CoordinateReading(
                    usedAlias ? TvIds.SceneSpaceId : TvIds.AiredSpaceId,
                    Coordinate.OfOrdinals(OrdinalPath.Of(season, parsed.EpisodeNumbers[0])),
                    CoordinateConfidence.Asserted));
                break;

            case TvReleaseKind.Absolute when parsed.AbsoluteNumbers.Count != 0:
                readings.Add(new CoordinateReading(
                    usedAlias ? TvIds.SceneAbsoluteSpaceId : TvIds.AbsoluteSpaceId,
                    Coordinate.OfOrdinals(OrdinalPath.Of(parsed.AbsoluteNumbers[0])),
                    CoordinateConfidence.Asserted));
                break;

            case TvReleaseKind.AirDate when parsed.AirDate is { } airDate:
                readings.Add(new CoordinateReading(
                    TvIds.AirDateSpaceId,
                    Coordinate.OfDate(airDate),
                    CoordinateConfidence.Asserted));
                break;

            case TvReleaseKind.FullSeason when parsed.SeasonNumber is { } run:
                readings.Add(new CoordinateReading(
                    TvIds.AiredSpaceId,
                    Coordinate.OfOrdinals(OrdinalPath.Of(run)),
                    CoordinateConfidence.Asserted));
                break;

            case TvReleaseKind.Unknown:
            default:
                break;
        }

        // And what the catalog says, which is what the rest of the platform measures against. Both are
        // kept: the asserted reading is the audit trail for how the match was made.
        var canonical = new CoordinateReading(
            TvIds.AiredSpaceId,
            Coordinate.OfOrdinals(OrdinalPath.Of(first.SeasonNumber, first.EpisodeNumber)),
            CoordinateConfidence.Verified);

        if (readings.Count != 0
            && string.Equals(readings[0].SpaceId, TvIds.AiredSpaceId, StringComparison.Ordinal))
        {
            readings[0] = canonical;
        }
        else
        {
            readings.Add(canonical);
        }

        return CoordinateSet.Of([.. readings]);
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record Resolution(
        IReadOnlyList<TvEpisodeRecord> Units,
        MatchConfidence Confidence,
        bool UsedAlias,
        string? Rejection)
    {
        public bool UsedUnverifiedAlias { get; init; }
    }
}
