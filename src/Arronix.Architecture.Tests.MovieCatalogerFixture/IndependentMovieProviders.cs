using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;

namespace Arronix.Architecture.Tests.MovieCatalogerFixture;

/// <summary>A movie cataloger written the way a separately shipped vendor package would write one.</summary>
/// <remarks>
/// <para>
/// There is one thing to notice, and it is the shape of the file rather than anything it does: the item
/// relationship is stated once, as <c>ICataloger&lt;Movie&gt;</c>, and the <c>Movie</c> it names arrives
/// from the movies media domain assembly. No definition, no parser, no module and no generated projection is
/// referenced, and none is needed - which is the property the package split exists to create.
/// </para>
/// <para>
/// The transport is deliberately absent. This fixture proves what a provider must be able to compile
/// against; a real vendor cataloger with an HTTP boundary is G05's work, and calling this one provider
/// coverage would be a category error.
/// </para>
/// </remarks>
public sealed class IndependentMovieCataloger : ICataloger<Movie>
{
    private static readonly PluginId Package = PluginId.FromString("fixture.movies.cataloger");

    /// <inheritdoc />
    public ProviderId Id { get; } = ProviderId.Create(Package, "independent");

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Cataloger;

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

    /// <summary>Gets the identifier namespace this fixture owns, spelled by the provider and nowhere else.</summary>
    public static string Scheme => "fixture";

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
    /// <remarks>
    /// The return type is the media kind's own item, fully shaped, including the movie-owned lifecycle
    /// object whose stage the availability selection reads. A field dictionary is not an alternative
    /// spelling of this signature.
    /// </remarks>
    public Task<IReadOnlyList<Movie>> SearchAsync(
        ProviderInvocation invocation,
        CatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult<IReadOnlyList<Movie>>([Shape(query.Text, new DateOnly(2024, 3, 1))]);
    }

    /// <inheritdoc />
    public Task<Movie?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Movie?>(Shape(id.Value, new DateOnly(2024, 3, 1)));

    /// <inheritdoc />
    public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation,
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExternalId>>([]);

    /// <inheritdoc />
    /// <remarks>
    /// Recognition is local, deterministic and owned here. The Movies package does not know this spelling,
    /// which is the boundary the non-generic cataloger floor exists to keep.
    /// </remarks>
    public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var marker = "{" + Scheme + "-";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return [];
        }

        var open = start + marker.Length;
        var close = text.IndexOf('}', open);

        return close <= open
            ? []
            : [new ExternalIdReading(
                ExternalId.Of(Scheme, text[open..close]),
                text[start..(close + 1)],
                start)];
    }

    /// <summary>Builds a complete movie, so the fixture proves the exact shape rather than a stub.</summary>
    /// <param name="title">The movie title.</param>
    /// <param name="digital">The digital release date.</param>
    /// <returns>The shaped movie.</returns>
    internal static Movie Shape(string title, DateOnly digital) => new()
    {
        Key = MediaItemId.FromInt64(title.Length),
        Title = title,
        Year = digital.Year,
        Lifecycle = new MovieReleaseTimeline
        {
            Digital = digital,
            EvaluatedOn = digital.AddDays(1)
        }
    };
}

/// <summary>A curated movie list, paired to the same item type by the same single generic argument.</summary>
public sealed class IndependentMovieCurator : ICurator<Movie>
{
    private static readonly PluginId Package = PluginId.FromString("fixture.movies.cataloger");

    /// <inheritdoc />
    public ProviderId Id { get; } = ProviderId.Create(Package, "independent-list");

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Curator;

    /// <inheritdoc />
    public TimeSpan MinimumRefreshInterval => TimeSpan.FromHours(6);

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
    public Task<CuratedListFetch<Movie>> FetchAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CuratedListFetch<Movie>(
            [IndependentMovieCataloger.Shape("Curated", new DateOnly(2023, 11, 2))],
            AnyFailure: false,
            Warnings: []));
}

/// <summary>
/// The provider package's entry module.
/// </summary>
/// <remarks>
/// One closed registration each, naming <c>Movie</c> once. The package requires the movies package by
/// identifier and never references its executable assembly, so the <c>Movie</c> these generics close over is
/// the one the installation admitted rather than a copy shipped here.
/// </remarks>
public sealed class IndependentMovieProviderModule : IPluginModule
{
    /// <inheritdoc />
    public PluginId Id { get; } = PluginId.FromString("fixture.movies.provider");

    /// <inheritdoc />
    public string Name => "Independent movie provider";

    /// <inheritdoc />
    public string Version => "0.1.0";

    /// <inheritdoc />
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Registry
            .AddCataloger<Movie, IndependentMovieCataloger>(new ProviderDescriptor
            {
                LocalId = "independent",
                Family = ProviderFamily.Cataloger,
                Name = "Independent movie cataloger",
                Settings = [],
            })
            .AddCurator<Movie, IndependentMovieCurator>(new ProviderDescriptor
            {
                LocalId = "independent-curator",
                Family = ProviderFamily.Curator,
                Name = "Independent movie curator",
                Settings = [],
            });
    }
}
