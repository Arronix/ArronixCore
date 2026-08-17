#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0014 // Extension contracts are experimental; PluginId is one.
#pragma warning disable ARX0015 // Provider contracts are experimental; a media extension is their intended implementer.

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv.Providers;

/// <summary>
/// An episodic metadata source backed by the seeded catalog.
/// </summary>
/// <remarks>
/// <para>The surveyed applications shared no metadata-source code at all — not one file, not one relative
/// path — but their signatures rhymed exactly: "fetch a parent and its children in one call, keyed by an
/// external identifier". This type is that shape, and the graph it returns is the generalization of the
/// two-element tuple every one of them returned.</para>
/// <para>Two details are load-bearing. The search result is discriminated by <see cref="MediaLevelId"/>, so
/// a caller can search for entries and units in one call without an untyped list. And each node carries a
/// <see cref="CoordinateSet"/>, so a unit arrives from the metadata pipeline already carrying every
/// addressing scheme the source knows — including the ones the host will never interpret.</para>
/// </remarks>
public sealed class TvCataloger : ICataloger
{
    /// <summary>Settings field: whether to include out-of-run entries.</summary>
    public const string IncludeOutOfRunSetting = "includeOutOfRun";

    /// <summary>Settings field: which regional certification set to prefer.</summary>
    public const string CertificationCountrySetting = "certificationCountry";

    /// <summary>Option source resolving to the supported certification regions.</summary>
    public const string CertificationOptionSource = "certification-countries";

    /// <summary>The provider's extension-local identifier.</summary>
    public const string LocalId = "catalog";

    private readonly TvCatalog _catalog;

    /// <summary>Creates the metadata source.</summary>
    /// <param name="plugin">The owning extension, which qualifies the provider identifier.</param>
    /// <param name="catalog">The catalog answering every call.</param>
    public TvCataloger(PluginId plugin, TvCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        Id = ProviderId.Create(plugin, LocalId);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public ProviderId Id { get; }

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Cataloger;

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities =>
        CatalogerCapabilities.Search | CatalogerCapabilities.BulkFetch;

    /// <summary>Builds the registration this provider is added to the registry with.</summary>
    /// <param name="plugin">The owning extension.</param>
    /// <param name="catalog">The catalog backing the provider.</param>
    /// <returns>The registration.</returns>
    public static CatalogerRegistration Registration(PluginId plugin, TvCatalog catalog)
        => new(Describe(), new TvCataloger(plugin, catalog));

    /// <summary>Builds the descriptor, including the declarative settings schema.</summary>
    /// <returns>The descriptor.</returns>
    public static ProviderDescriptor Describe() => new()
    {
        LocalId = LocalId,
        Family = ProviderFamily.Cataloger,
        Name = "Episodic catalog",
        Description = "Resolves episodic metadata from the extension's own catalog. Issues no network request.",
        Settings =
        [
            new SettingsField
            {
                FieldId = IncludeOutOfRunSetting,
                Name = "Include entries outside the numbered run",
                ValueKind = FieldValueKind.Boolean,
                Role = SettingRole.Flag,
                DefaultValue = "true",
                HelpWarning = "Excluding them removes rows that already have files linked to them."
            },
            new SettingsField
            {
                FieldId = CertificationCountrySetting,
                Name = "Certification region",
                ValueKind = FieldValueKind.Enumerated,
                Role = SettingRole.Enumeration,
                DefaultValue = "US",
                Choices =
                [
                    new FacetValue("US", "United States"),
                    new FacetValue("GB", "United Kingdom"),
                    new FacetValue("JP", "Japan")
                ],
                OptionSourceId = CertificationOptionSource
            }
        ]
    };

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_catalog.Series.Count != 0
            ? ValidationOutcome.Success
            : ValidationOutcome.Failed(new ValidationFailure(
                null,
                "The catalog is empty.",
                ValidationSeverity.Error,
                CoreErrorCode.CatalogerSearchFailed)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionSourceId);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<FacetValue> options = string.Equals(
            optionSourceId,
            CertificationOptionSource,
            StringComparison.Ordinal)
            ?
            [
                new FacetValue("US", "United States"),
                new FacetValue("GB", "United Kingdom"),
                new FacetValue("JP", "Japan")
            ]
            : [];

        return Task.FromResult(options);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(
        ProviderInvocation invocation,
        MetadataQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Level is { } level && level != TvIds.SeriesLevel)
        {
            return Task.FromResult<IReadOnlyList<MetadataSearchHit>>([]);
        }

        if (query.Id is { } identifier)
        {
            IReadOnlyList<MetadataSearchHit> byId =
                _catalog.TryGetSeriesByExternalId(identifier, out var found) && found is not null
                    ? [ToHit(found)]
                    : [];

            return Task.FromResult(byId);
        }

        var needle = TvTitleNormalizer.Normalize(query.Text);

        IReadOnlyList<MetadataSearchHit> matches = needle.Length == 0
            ? []
            : [.. _catalog.Series
                .Where(series =>
                    TvTitleNormalizer.Normalize(series.Title).Contains(needle, StringComparison.Ordinal))
                .Select(ToHit)];

        return Task.FromResult(matches);
    }

    /// <inheritdoc />
    public Task<MetadataGraph?> GetAsync(
        ProviderInvocation invocation,
        ExternalId id,
        SelectionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_catalog.TryGetSeriesByExternalId(id, out var series) || series is null)
        {
            return Task.FromResult<MetadataGraph?>(null);
        }

        var parentId = ExternalId.Of(
            TvIds.TvdbScheme,
            series.TvdbId.ToString(CultureInfo.InvariantCulture));

        var nodes = new List<MetadataNode>
        {
            new(
                TvIds.SeriesLevel,
                parentId,
                null,
                series.Title,
                SeriesFields(series),
                CoordinateSet.Empty)
        };

        // The resolved third profile axis, applied. The declared facet is a visibility facet, so excluding
        // out-of-run entries here would be the destructive reading the shape explicitly rejects - which is
        // why this only honors an explicit instruction and never assumes one.
        var includeOutOfRun = !policy.Flags.TryGetValue(TvIds.SeasonKindFacetId, out var allow) || allow;

        foreach (var unit in _catalog.EpisodesOf(series.Id))
        {
            if (!includeOutOfRun && unit.IsOutOfRun)
            {
                continue;
            }

            nodes.Add(new MetadataNode(
                TvIds.EpisodeLevel,
                ExternalId.Of(
                    TvIds.TvdbScheme,
                    $"{series.TvdbId.ToString(CultureInfo.InvariantCulture)}"
                    + $"-{unit.Id.ToString(CultureInfo.InvariantCulture)}"),
                parentId,
                unit.Title,
                UnitFields(unit),

                // Every coordinate the source knows travels with the node. The host carries them and never
                // interprets them; only this extension knows what any of them mean.
                TvCatalog.CoordinatesOf(unit)));
        }

        return Task.FromResult<MetadataGraph?>(new MetadataGraph(nodes));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
        ProviderInvocation invocation,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Delta sync is not among the declared capabilities, so the honest answer is an empty list.
        return Task.FromResult<IReadOnlyList<ExternalId>>([]);
    }

    private static Dictionary<string, FieldValue> SeriesFields(TvSeriesRecord series)
    {
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [TvSeriesFields.Title] = FieldValue.OfText(series.Title),
            [TvSeriesFields.SortTitle] = FieldValue.OfText(series.SortTitle),
            [TvSeriesFields.Year] = FieldValue.OfInteger(series.Year),
            [TvSeriesFields.Status] = FieldValue.OfEnumerated(series.Status),
            [TvSeriesFields.AddressingScheme] = FieldValue.OfEnumerated(series.AddressingScheme),
            [TvSeriesFields.UsesAliasNumbering] = FieldValue.OfBoolean(series.UsesAliasNumbering)
        };

        if (series.Network is not null)
        {
            fields[TvSeriesFields.Network] = FieldValue.OfText(series.Network);
        }

        if (series.FirstAired is { } firstAired)
        {
            fields[TvSeriesFields.FirstAired] = FieldValue.OfDate(firstAired);
        }

        return fields;
    }

    private static Dictionary<string, FieldValue> UnitFields(TvEpisodeRecord unit)
    {
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            [TvEpisodeFields.Title] = FieldValue.OfText(unit.Title),
            [TvEpisodeFields.Position] = FieldValue.OfOrdinal(
                OrdinalPath.Of(unit.SeasonNumber, unit.EpisodeNumber)),
            [TvEpisodeFields.SequenceKind] = FieldValue.OfEnumerated(
                unit.IsOutOfRun ? TvSequenceKinds.OutOfRun : TvSequenceKinds.Regular)
        };

        if (unit.AirDate is { } airDate)
        {
            fields[TvEpisodeFields.AirDate] = FieldValue.OfDate(airDate);
        }

        if (unit.RuntimeMinutes > 0)
        {
            fields[TvEpisodeFields.Runtime] = FieldValue.OfDuration(TimeSpan.FromMinutes(unit.RuntimeMinutes));
        }

        return fields;
    }

    private static MetadataSearchHit ToHit(TvSeriesRecord series) => new(
        ExternalId.Of(TvIds.TvdbScheme, series.TvdbId.ToString(CultureInfo.InvariantCulture)),
        TvIds.SeriesLevel,
        series.Title,
        series.Year,
        series.Network,
        series.Poster is null ? [] : [new ArtworkRef("poster", series.Poster, 680, 1000)]);
}
