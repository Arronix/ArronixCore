using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Northmark.Shorts.Catalog;

/// <summary>The Northmark short-film catalog.</summary>
/// <remarks>
/// The item type is named once, in the contract this class closes; the registration reads the pairing back
/// from there. The catalog owns its identifier scheme and the marker spelling that scheme uses.
/// </remarks>
public sealed class ShortFilmCataloger : ICataloger<ShortFilm>
{
    private static readonly Regex Marker = new(
        @"\{northmark-(?<id>[0-9]{1,9})\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    /// <inheritdoc />
    public string CatalogScheme => "northmark";

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

    /// <inheritdoc />
    public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var readings = new List<ExternalIdReading>();

        foreach (Match match in Marker.Matches(text))
        {
            readings.Add(new ExternalIdReading(
                new ExternalId("northmark", match.Groups["id"].Value),
                match.Value,
                match.Index));
        }

        return readings;
    }

    /// <inheritdoc />
    /// <remarks>Nothing to reach: this catalog answers from the marker vocabulary it owns.</remarks>
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
    public Task<IReadOnlyList<ShortFilm>> SearchAsync(
        ProviderInvocation invocation,
        CatalogQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ShortFilm>>([]);

    /// <inheritdoc />
    public Task<ShortFilm?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ShortFilm?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation,
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExternalId>>([]);
}
