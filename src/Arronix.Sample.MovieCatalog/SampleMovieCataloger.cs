using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;

namespace Arronix.Sample.MovieCatalog;

/// <summary>
/// A credential-free movie catalog, so that an installation can be evaluated before any account exists.
/// </summary>
/// <remarks>
/// <para>
/// This is an ordinary <see cref="ICataloger{TItem}"/> over the movies media domain's own item type,
/// shipped in its own package and admitted through the ordinary loader. It holds the identity authority
/// for the <c>sample</c> scheme, states exactly one identifier in that scheme on every item it returns,
/// and recognizes its own markers in a release name. Nothing here knows about Host, the API or the client.
/// </para>
/// <para>
/// It needs no settings, which is the whole point: an evaluator configures it by adding it, and the catalog
/// answers. Its content is fixed, so a search, a refresh and a restart all agree with each other.
/// </para>
/// </remarks>
public sealed class SampleMovieCataloger : ICataloger<Movie>
{
    /// <inheritdoc />
    public string CatalogScheme => SampleMovies.Scheme;

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ValidationOutcome.Success);

    /// <inheritdoc />
    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FacetValue>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<Movie>> SearchAsync(
        ProviderInvocation invocation,
        CatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Id is { } requested)
        {
            return Task.FromResult<IReadOnlyList<Movie>>(
                SampleMovies.Find(requested) is { } exact ? [exact] : []);
        }

        var text = query.Text?.Trim() ?? string.Empty;

        // An empty search is the honest way to browse a small fixed catalog: it returns everything rather
        // than nothing, so the first thing an evaluator sees is the catalog itself.
        return Task.FromResult<IReadOnlyList<Movie>>(text.Length == 0
            ? [.. SampleMovies.All]
            : [.. SampleMovies.All.Where(movie => Matches(movie, text))]);
    }

    /// <inheritdoc />
    public Task<Movie?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SampleMovies.Find(id));

    /// <inheritdoc />
    public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation,
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExternalId>>([]);

    /// <inheritdoc />
    public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var readings = new List<ExternalIdReading>();

        foreach (var movie in SampleMovies.All)
        {
            foreach (var id in movie.ExternalIds.Values)
            {
                var marker = $"{SampleMovies.Scheme}:{id.Value}";
                var offset = text.IndexOf(marker, StringComparison.Ordinal);

                if (offset >= 0)
                {
                    readings.Add(new ExternalIdReading(id, marker, offset));
                }
            }
        }

        return readings;
    }

    private static bool Matches(Movie movie, string text) =>
        movie.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
        || (movie.Overview?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
        || movie.Genres.Any(genre => genre.Contains(text, StringComparison.OrdinalIgnoreCase))
        || movie.Keywords.Any(keyword => keyword.Contains(text, StringComparison.OrdinalIgnoreCase));
}
