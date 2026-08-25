using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;


namespace Arronix.Host.Providers;

/// <summary>
/// Decides which release sources can answer a question, and asks the ones that can.
/// </summary>
/// <remarks>
/// <para>
/// Eligibility is pure set intersection over two declarations that name none of each other's concepts. A
/// media kind declares the terms it needs to ask by and the categories its content sits in; a release source
/// declares the terms it accepts and the categories it carries. Neither has ever heard of the other, and
/// adding a fifth media kind changes nothing here.
/// </para>
/// <para>
/// Two gates, at the two places a surveyed application proves they belong. The category gate runs host-side
/// before any network call, so a source that carries nothing relevant is never contacted at all. The term
/// gate is the source's own business and runs inside it, where a query using an undeclared term
/// short-circuits to an empty result rather than issuing a malformed request.
/// </para>
/// <para>
/// A media kind may declare no required terms at all and be perfectly served by free text plus categories.
/// Three of one surveyed application's seven adapters work exactly that way, so the model has to allow it,
/// and it does.
/// </para>
/// </remarks>
/// <param name="providers">Where implementations are looked up.</param>
/// <param name="definitions">Where configured definitions are read from.</param>
/// <param name="status">Which definitions are currently in service.</param>
/// <param name="tests">Where invocations are built.</param>
public sealed class IndexerDispatcher(
    ProviderRegistry providers,
    ProviderDefinitionStore definitions,
    ProviderStatusStore status,
    ProviderTestService tests)
{
    private readonly ProviderRegistry _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly ProviderDefinitionStore _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ProviderStatusStore _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly ProviderTestService _tests = tests ?? throw new ArgumentNullException(nameof(tests));

    /// <summary>
    /// Determines whether a release source can serve a media kind's declared search.
    /// </summary>
    /// <param name="search">What the media kind needs to ask.</param>
    /// <param name="profile">What the release source accepts.</param>
    /// <returns><see langword="true"/> when the source is eligible.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static bool IsEligible(SearchKind search, SearchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(profile);

        // Every required term must be accepted, and the categories must overlap. Provider-specific category
        // identifiers are excluded from the overlap test because they mean different things at different
        // sources; only the shared taxonomy can decide eligibility.
        return search.RequiredTerms.All(term => profile.Terms.Contains(term))
            && search.Categories.Any(category => !category.IsProviderSpecific && profile.Categories.Contains(category));
    }

    /// <summary>
    /// Lists the configured release sources that can serve a query.
    /// </summary>
    /// <param name="kind">The media kind the query belongs to.</param>
    /// <param name="searchKindId">Which of the kind's declared searches is being run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The eligible definitions, best priority first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    public async Task<IReadOnlyList<ProviderDefinition>> EligibleAsync(
        RegisteredMediaKind kind,
        string searchKindId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);

        var search = kind.Shape.RequireSearchKind(searchKindId);
        var eligible = new List<ProviderDefinition>();

        foreach (var definition in _definitions.Query(ProviderFamily.Indexer, kind.Kind, enabledOnly: true))
        {
            if (!_status.IsAvailable(definition.Id)
                || !_providers.TryLease<IIndexer>(definition.Provider, out var leased))
            {
                continue;
            }

            using var held = leased;
            var profile = await DescribeAsync(held.Value, definition, cancellationToken).ConfigureAwait(false);

            if (profile is not null && profile.SearchProfiles.Any(candidate => IsEligible(search, candidate)))
            {
                eligible.Add(definition);
            }
        }

        return [.. eligible.OrderBy(definition => definition.Priority).ThenBy(definition => definition.Id)];
    }

    /// <summary>
    /// Runs a query against every eligible release source.
    /// </summary>
    /// <param name="kind">The media kind the query belongs to.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Everything that came back, with a warning per source that failed.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="kind"/> or <paramref name="query"/> is <see langword="null"/>.
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "One release source failing must not fail the search; the failure is recorded against that source and reported as a warning beside the results that did arrive.")]
    public async Task<ReleaseQueryResult> SearchAsync(
        RegisteredMediaKind kind,
        ReleaseQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(query);

        var releases = new List<ReleaseListing>();
        var warnings = new List<string>();
        var partial = false;

        foreach (var definition in await EligibleAsync(kind, query.SearchKindId, cancellationToken).ConfigureAwait(false))
        {
            if (!_providers.TryLease<IIndexer>(definition.Provider, out var leased))
            {
                continue;
            }

            // Held across the search: an indexer disposed mid-call would be a plugin object torn down
            // while its own method is running.
            using var held = leased;
            var indexer = held.Value;

            try
            {
                var result = PluginBoundary.Snapshot(
                    await indexer
                        .SearchAsync(_tests.Invocation(definition), query, cancellationToken)
                        .ConfigureAwait(false));

                releases.AddRange(result.Releases);
                warnings.AddRange(result.Warnings);
                partial |= result.IsPartialResult;
                _status.RecordSuccess(definition.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception failure)
            {
                _status.RecordFailure(definition.Id);
                warnings.Add($"'{definition.Name}' did not answer: {failure.Message}");
                partial = true;
            }
        }

        return new ReleaseQueryResult(releases, partial, warnings);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A source that cannot describe itself is treated as ineligible rather than as an error; the failure is recorded against it and the search proceeds with the sources that can.")]
    private async Task<IndexerProfile?> DescribeAsync(
        IIndexer indexer,
        ProviderDefinition definition,
        CancellationToken cancellationToken)
    {
        try
        {
            // Copied inside the caller's lease: a profile's collections are the extension's own, and this
            // one is read after the call returns.
            return PluginBoundary.Snapshot(
                await indexer
                    .DescribeAsync(_tests.Invocation(definition), cancellationToken)
                    .ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _status.RecordFailure(definition.Id);
            return null;
        }
    }
}
