using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;

namespace Proof.Movies.Catalog;

/// <summary>A deterministic external cataloger used only by the G07B proof harness.</summary>
/// <remarks>
/// Revision is read from the ordinary configured provider definition on every call. There is no static
/// state or call-count behaviour: the same request and definition always produce the same Movie.
/// </remarks>
public sealed class ProofMovieCataloger : ICataloger<Movie>
{
    /// <summary>The one setting this proof cataloger requires.</summary>
    public const string RevisionField = "revision";

    private const string Scheme = "proof";
    private const string Value = "42";

    /// <inheritdoc />
    public string CatalogScheme => Scheme;

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Revision(invocation.Definition) is null
            ? ValidationOutcome.Failed(new ValidationFailure(
                RevisionField,
                "The proof catalog requires revision 1 or 2."))
            : ValidationOutcome.Success);

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

        var revision = Revision(invocation.Definition);
        if (revision is null || (query.Id is { } requested && !Owns(requested)))
        {
            return Task.FromResult<IReadOnlyList<Movie>>([]);
        }

        return Task.FromResult<IReadOnlyList<Movie>>([MovieFor(revision.Value)]);
    }

    /// <inheritdoc />
    public Task<Movie?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        CancellationToken cancellationToken = default)
    {
        var revision = Revision(invocation.Definition);
        return Task.FromResult(revision is { } selected && Owns(id) ? MovieFor(selected) : null);
    }

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
        const string marker = "proof:42";
        var offset = text.IndexOf(marker, StringComparison.Ordinal);
        return offset < 0 ? [] : [new ExternalIdReading(ExternalId.Of(Scheme, Value), marker, offset)];
    }

    private static bool Owns(ExternalId id) =>
        string.Equals(id.Scheme, Scheme, StringComparison.Ordinal) && string.Equals(id.Value, Value, StringComparison.Ordinal);

    private static int? Revision(ProviderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Settings.TryGetValue(RevisionField, out var text) && int.TryParse(text, out var revision)
            && revision is 1 or 2
                ? revision
                : null;
    }

    private static Movie MovieFor(int revision) => new()
    {
        ExternalIds = ExternalIdSet.Of(ExternalId.Of(Scheme, Value)),
        Title = revision == 1 ? "Proof Movie Revision One" : "Proof Movie Revision Two",
        Year = 2024,
        Overview = revision == 1
            ? "Provider-owned revision one catalog facts."
            : "Provider-owned revision two catalog facts.",
        CatalogState = CatalogRecordState.Active,
        Lifecycle = new MovieReleaseTimeline
        {
            Digital = revision == 1 ? new DateOnly(2024, 1, 1) : new DateOnly(2024, 2, 1),
            EvaluatedOn = new DateOnly(2024, 3, 1),
        },
        Genres = revision == 1 ? ["Proof one"] : ["Proof two"],
        Keywords = ["g07b", $"revision-{revision}"],
    };
}
