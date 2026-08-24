using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Settings;
using Arronix.Provider.Tmdb.Transport;

namespace Arronix.Provider.Tmdb.Movies;

/// <summary>The TMDb-owned <c>ICurator&lt;Movie&gt;</c> binding, sourced from TMDb's popular-movies list.</summary>
public sealed class TmdbMovieCurator : ICurator<Movie>
{
    private static readonly PluginId Package = PluginId.FromString("tmdb");

    private readonly IPluginContext _context;

    /// <summary>Creates the curator. The host activates this exact constructor through the admitted plugin context.</summary>
    /// <param name="context">Everything this provider may reach.</param>
    public TmdbMovieCurator(IPluginContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public ProviderId Id { get; } = ProviderId.Create(Package, "tmdb-popular");

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Curator;

    /// <inheritdoc />
    public TimeSpan MinimumRefreshInterval => TimeSpan.FromHours(6);

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
    /// TMDb's popular-movies list has at least one entry. See that exception's documentation for why this
    /// provider stops here instead of returning one.
    /// </exception>
    public async Task<CuratedListFetch<Movie>> FetchAsync(
        ProviderInvocation invocation, CancellationToken cancellationToken = default)
    {
        var page = await BuildClient(invocation).DiscoverPopularAsync(page: 1, cancellationToken)
            .ConfigureAwait(false);
        var matches = page.Results?.Count ?? 0;

        return matches == 0
            ? new CuratedListFetch<Movie>([], AnyFailure: false, Warnings: [])
            : throw MovieMaterializationBoundary.For("movie/popular", matches);
    }

    private TmdbMovieClient BuildClient(ProviderInvocation invocation) =>
        new(_context.RequireHttp(), TmdbProviderSettings.Read(invocation.Definition));
}
