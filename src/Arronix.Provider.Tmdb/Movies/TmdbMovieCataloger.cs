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
    private static readonly PluginId Package = PluginId.FromString("tmdb");

    private readonly IPluginContext _context;

    /// <summary>Creates the cataloger. The host activates this exact constructor through the admitted plugin context.</summary>
    /// <param name="context">Everything this provider may reach.</param>
    public TmdbMovieCataloger(IPluginContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public ProviderId Id { get; } = ProviderId.Create(Package, "tmdb-movies");

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Cataloger;

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
    /// <exception cref="MovieMaterializationNotSupportedException">
    /// TMDb matched at least one movie. See that exception's documentation for why this provider stops
    /// here instead of returning one.
    /// </exception>
    public async Task<IReadOnlyList<Movie>> SearchAsync(
        ProviderInvocation invocation, CatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await BuildClient(invocation).SearchMoviesAsync(query.Text, page: 1, cancellationToken)
            .ConfigureAwait(false);
        var matches = page.Results?.Count ?? 0;

        return matches == 0 ? [] : throw MovieMaterializationBoundary.For("search/movie", matches);
    }

    /// <inheritdoc />
    /// <exception cref="MovieMaterializationNotSupportedException">
    /// TMDb has the requested movie. See that exception's documentation for why this provider stops here
    /// instead of returning one.
    /// </exception>
    public async Task<Movie?> GetAsync(
        ProviderInvocation invocation, ExternalId id, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(id.Value, out var tmdbId))
        {
            return null;
        }

        var details = await BuildClient(invocation).GetMovieDetailsAsync(tmdbId, cancellationToken)
            .ConfigureAwait(false);

        return details is null ? null : throw MovieMaterializationBoundary.For($"movie/{tmdbId}", matchCount: 1);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var client = BuildClient(invocation);
        var sinceDate = DateOnly.FromDateTime(since.UtcDateTime);
        var changed = new List<ExternalId>();
        var page = 1;

        while (true)
        {
            var response = await client.GetChangedMoviesAsync(sinceDate, page, cancellationToken)
                .ConfigureAwait(false);

            foreach (var entry in response.Results ?? [])
            {
                changed.Add(ExternalId.Of(TmdbIdentity.Scheme, entry.Id));
            }

            if (response.TotalPages <= 0 || page >= response.TotalPages)
            {
                break;
            }

            page++;
        }

        return changed;
    }

    /// <inheritdoc />
    public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => TmdbIdentity.Read(text);

    private TmdbMovieClient BuildClient(ProviderInvocation invocation) =>
        new(_context.RequireHttp(), TmdbProviderSettings.Read(invocation.Definition));
}
