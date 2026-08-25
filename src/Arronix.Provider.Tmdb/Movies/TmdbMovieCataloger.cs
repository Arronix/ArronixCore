using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Identity;
using Arronix.Provider.Tmdb.Settings;
using Arronix.Provider.Tmdb.Transport;

namespace Arronix.Provider.Tmdb.Movies;

/// <summary>The TMDb-owned <c>ICataloger&lt;Movie&gt;</c> binding.</summary>
/// <remarks>
/// The movie item relationship is stated once, as <see cref="ICataloger{Movie}"/>; no part of this
/// provider references the Movies extension's parser, module, definition, or generated projections.
/// </remarks>
public sealed class TmdbMovieCataloger : ICataloger<Movie>
{
    private readonly IPluginContext _context;

    /// <summary>Creates the cataloger. The host activates this exact constructor through the admitted plugin context.</summary>
    /// <param name="context">Everything this provider may reach.</param>
    public TmdbMovieCataloger(IPluginContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public string CatalogScheme => TmdbIdentity.Scheme;

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search | CatalogerCapabilities.DeltaSync;

    /// <inheritdoc />
    public async Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation, CancellationToken cancellationToken = default) =>
        await BuildClient(invocation).TestAuthenticationAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation, string optionSourceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);

    /// <inheritdoc />
    /// <remarks>An explicit identifier performs a direct lookup and ignores the search text.</remarks>
    public async Task<IReadOnlyList<Movie>> SearchAsync(
        ProviderInvocation invocation, CatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Id is { } id)
        {
            var resolved = await GetAsync(invocation, id, cancellationToken).ConfigureAwait(false);
            return resolved is null ? [] : [resolved];
        }

        var settings = TmdbProviderSettings.Read(invocation.Definition);
        var page = await new TmdbMovieClient(_context.RequireHttp(), settings)
            .SearchMoviesAsync(query.Text, page: 1, cancellationToken)
            .ConfigureAwait(false);
        var evaluatedOn = DateOnly.FromDateTime(_context.Clock.GetUtcNow().UtcDateTime);
        var matches = new List<Movie>();

        foreach (var result in page.Results ?? [])
        {
            matches.Add(TmdbMovieMapper.ToMovie(result, settings, evaluatedOn));
        }

        return matches;
    }

    /// <inheritdoc />
    /// <remarks>Wrong-scheme and malformed identifiers are rejected before any network request.</remarks>
    public async Task<Movie?> GetAsync(
        ProviderInvocation invocation, ExternalId id, CancellationToken cancellationToken = default)
    {
        if (!TryResolveTmdbId(id, out var tmdbId))
        {
            return null;
        }

        var settings = TmdbProviderSettings.Read(invocation.Definition);
        var details = await new TmdbMovieClient(_context.RequireHttp(), settings)
            .GetMovieDetailsAsync(tmdbId, cancellationToken)
            .ConfigureAwait(false);

        return details is null
            ? null
            : TmdbMovieMapper.ToMovie(
                details,
                settings,
                DateOnly.FromDateTime(_context.Clock.GetUtcNow().UtcDateTime));
    }

    /// <inheritdoc />
    /// <remarks>Partitions the range into TMDb's 14-day windows and removes duplicate identifiers.</remarks>
    public async Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var client = BuildClient(invocation);
        var sinceDate = DateOnly.FromDateTime(since.UtcDateTime);
        var untilDate = DateOnly.FromDateTime(_context.Clock.GetUtcNow().UtcDateTime);

        var changed = new List<ExternalId>();
        var seen = new HashSet<ExternalId>();

        foreach (var (windowStart, windowEnd) in TmdbChangeWindow.Partition(sinceDate, untilDate))
        {
            var page = 1;

            while (true)
            {
                var response = await client
                    .GetChangedMoviesAsync(windowStart, windowEnd, page, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var entry in response.Results ?? [])
                {
                    if (!TmdbIdentity.IsCanonicalId(entry.Id))
                    {
                        throw new TmdbResponseFormatException(
                            "movie/changes", $"reported a malformed changed-movie id ({entry.Id}).");
                    }

                    var id = ExternalId.Of(TmdbIdentity.Scheme, entry.Id);
                    if (seen.Add(id))
                    {
                        changed.Add(id);
                    }
                }

                if (response.TotalPages <= 0 || page >= response.TotalPages)
                {
                    break;
                }

                page++;
            }
        }

        return changed;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => TmdbIdentity.Read(text);

    /// <summary>Resolves only canonical identifiers in this cataloger's scheme.</summary>
    private static bool TryResolveTmdbId(ExternalId id, out int tmdbId)
    {
        tmdbId = default;

        return string.Equals(id.Scheme, TmdbIdentity.Scheme, StringComparison.OrdinalIgnoreCase)
            && TmdbIdentity.TryParseId(id.Value, out tmdbId);
    }

    private TmdbMovieClient BuildClient(ProviderInvocation invocation) =>
        new(_context.RequireHttp(), TmdbProviderSettings.Read(invocation.Definition));
}
