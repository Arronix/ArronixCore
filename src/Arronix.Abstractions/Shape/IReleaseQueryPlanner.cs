using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Turns "acquire these items" into the queries that will find them.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the per-domain overload sets the surveyed release sources carry, which range from one method
/// to seven. Overload resolution is a compile-time mechanism and cannot survive an extension boundary at
/// all: adding a media kind would mean editing a core interface.
/// </para>
/// <para>
/// Alternative titles, translations, credited-name rewrites and subtitle stripping are all this method's
/// business and none of them are the host's. The host contributes ordering, dispatch and de-duplication.
/// </para>
/// </remarks>
public interface IReleaseQueryPlanner
{
    /// <summary>
    /// Gets the media kind this planner serves.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Plans the queries for one acquisition.
    /// </summary>
    /// <param name="request">What is being acquired, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ordered query tiers.</returns>
    Task<ReleaseQueryPlan> PlanAsync(AcquisitionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// What is being acquired, and why.
/// </summary>
public sealed record AcquisitionRequest
{
    /// <summary>
    /// Gets the media kind being acquired.
    /// </summary>
    public required MediaKindId MediaKind { get; init; }

    /// <summary>
    /// Gets the <see cref="SearchKind.SearchKindId"/> being performed.
    /// </summary>
    public required string SearchKindId { get; init; }

    /// <summary>
    /// Gets the items being acquired.
    /// </summary>
    public required IReadOnlyList<MediaItemRef> Units { get; init; }

    /// <summary>
    /// Gets what prompted the acquisition.
    /// </summary>
    public required SearchOrigin Origin { get; init; }

    /// <summary>
    /// Gets the languages the governing profile accepts. What makes translated-title fan-out
    /// affordable: a planner emits alias queries only for spellings in an accepted language, instead of
    /// one query per known translation per source per sweep. Empty means no language filter.
    /// </summary>
    public IReadOnlyList<Language> AcceptedLanguages { get; init; } = [];
}

/// <summary>
/// What prompted an acquisition.
/// </summary>
/// <remarks>
/// The origin changes how hard the platform tries and how much it is willing to spend: a periodic sweep
/// of new releases behaves differently from a request a user is waiting on.
/// </remarks>
public enum SearchOrigin
{
    /// <summary>A periodic sweep of what a source has recently published.</summary>
    Rss = 0,

    /// <summary>An automatic search the platform decided to run.</summary>
    Automatic = 1,

    /// <summary>A search a user asked for and is not waiting on.</summary>
    UserInvoked = 2,

    /// <summary>A search a user is waiting on, and whose results they will choose from.</summary>
    Interactive = 3,

    /// <summary>A release pushed to the platform from outside.</summary>
    ReleasePush = 4
}

/// <summary>
/// An ordered set of query tiers. The first tier that yields any release wins.
/// </summary>
/// <param name="Tiers">The tiers, most preferred first.</param>
/// <remarks>
/// Generalizes the fallback chain every surveyed release source hand-rolls: try the identifier search,
/// fall back to a text search. Expressing it as data lets the host run it identically for every kind and
/// stop as soon as it has an answer.
/// </remarks>
public sealed record ReleaseQueryPlan(IReadOnlyList<ReleaseQueryTier> Tiers);

/// <summary>
/// One tier of a query plan. Every query in a tier is dispatched.
/// </summary>
/// <param name="Queries">The queries making up the tier.</param>
public sealed record ReleaseQueryTier(IReadOnlyList<ReleaseQuery> Queries);

/// <summary>
/// One query against one release source.
/// </summary>
public sealed record ReleaseQuery
{
    /// <summary>
    /// Gets the media kind the query is being made for.
    /// </summary>
    public required MediaKindId MediaKind { get; init; }

    /// <summary>
    /// Gets the <see cref="SearchKind.SearchKindId"/> the query implements.
    /// </summary>
    public required string SearchKindId { get; init; }

    /// <summary>
    /// Gets the unstructured query text.
    /// </summary>
    public required string FreeText { get; init; }

    /// <summary>
    /// Gets alternative spellings of the same subject, supplied by the extension and never invented by
    /// the host.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// Gets the structured arguments, which a source uses when it declares the matching terms.
    /// </summary>
    public IReadOnlyList<SearchArgument> Arguments { get; init; } = [];

    /// <summary>
    /// Gets the categories results are expected in.
    /// </summary>
    public IReadOnlyList<CategoryId> Categories { get; init; } = [];

    /// <summary>
    /// Gets the greatest number of results wanted from one page.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Gets how many results to skip.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets what prompted the acquisition this query belongs to.
    /// </summary>
    public required SearchOrigin Origin { get; init; }
}

/// <summary>
/// One structured argument of a query.
/// </summary>
/// <param name="Term">Which kind of argument this is.</param>
/// <param name="Value">The argument's value.</param>
/// <param name="ComponentId">
/// The coordinate component the value addresses, when <paramref name="Term"/> is
/// <see cref="SearchTerm.Ordinal"/>.
/// </param>
public readonly record struct SearchArgument(SearchTerm Term, FieldValue Value, string? ComponentId = null);
