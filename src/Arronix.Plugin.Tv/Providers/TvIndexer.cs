
using System.Globalization;
using System.Linq;
using System.Text;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv.Providers;

/// <summary>
/// A deterministic episodic indexer that answers from the seeded catalog instead of from a network.
/// </summary>
/// <remarks>
/// <para>It exists to exercise the provider contracts end to end without a live tracker and without this
/// extension taking the <c>network</c> capability. It is also the clearest demonstration of the search
/// model: it declares <b>three</b> search profiles, and a media kind's search kind is eligible for one of
/// them exactly when its required terms are a subset of that profile's terms and their categories intersect.
/// Neither side names the other's nouns anywhere in that computation.</para>
/// <para>The releases it synthesizes deliberately include a multi-unit title and a whole-run title, so the
/// matcher's hardest branches are reachable from an end-to-end host test.</para>
/// </remarks>
public sealed class TvIndexer : IIndexer
{
    /// <summary>Settings field: the tracker's base address.</summary>
    public const string BaseUrlSetting = "baseUrl";

    /// <summary>Settings field: the API key.</summary>
    public const string ApiKeySetting = "apiKey";

    /// <summary>Settings field: the categories to search.</summary>
    public const string CategoriesSetting = "categories";

    /// <summary>Settings field: whether whole-run releases are offered.</summary>
    public const string OfferPacksSetting = "offerPacks";

    /// <summary>Option source resolving to the categories this indexer supports.</summary>
    public const string CategoryOptionSource = "categories";

    /// <summary>The provider's extension-local identifier.</summary>
    public const string LocalId = "catalog-feed";

    private const string SessionKeyLastQuery = "lastQuery";

    private static readonly (string Suffix, string Source, string Resolution, long Size)[] Editions =
    [
        ("Bluray-1080p", "BluRay", "1080p", 4_294_967_296L),
        ("WEBDL-1080p", "WEB-DL", "1080p", 2_147_483_648L),
        ("HDTV-720p", "HDTV", "720p", 1_073_741_824L)
    ];

    private readonly TvCatalog _catalog;
    private readonly TimeProvider _clock;

    // The extension's own release-identifier namespace, not a contract member: identity is the host's to
    // mint, and this qualified spelling is only used to keep synthetic release keys unique.
    private readonly ProviderId _id;

    /// <summary>Creates the indexer.</summary>
    /// <param name="context">The capability-scoped plugin context supplied by DI.</param>
    public TvIndexer(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _id = ProviderId.Create(context.PluginId, LocalId);
        _catalog = TvCatalog.CreateSeeded();
        _clock = context.Clock;
    }

    public TvIndexer(PluginId plugin, TvCatalog catalog, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(clock);
        _id = ProviderId.Create(plugin, LocalId);
        _catalog = catalog;
        _clock = clock;
    }

    /// <summary>Builds the descriptor, including the declarative settings schema.</summary>
    /// <returns>The descriptor.</returns>
    public static ProviderDescriptor Describe() => new()
    {
        LocalId = LocalId,
        Name = "Episodic catalog feed",
        Description = "Answers episodic searches from the extension's own catalog. Issues no network request.",
        Protocols = [DownloadProtocol.Torrent, DownloadProtocol.Usenet],
        Settings =
        [
            new SettingsField
            {
                FieldId = BaseUrlSetting,
                Name = "URL",
                ValueKind = FieldValueKind.Link,
                Role = SettingRole.Endpoint,
                Required = true,
                DefaultValue = "https://catalog.invalid",
                HelpText = "Retained so the settings schema is realistic. Nothing is ever requested from it."
            },
            new SettingsField
            {
                FieldId = ApiKeySetting,
                Name = "API key",
                ValueKind = FieldValueKind.Text,
                Role = SettingRole.Value,
                Sensitivity = SettingSensitivity.Secret,
                Required = true
            },
            new SettingsField
            {
                FieldId = CategoriesSetting,
                Name = "Categories",
                ValueKind = FieldValueKind.Integer,
                Role = SettingRole.CategorySet,
                Multivalued = true,
                DefaultValue = "5000",
                OptionSourceId = CategoryOptionSource
            },
            new SettingsField
            {
                FieldId = OfferPacksSetting,
                Name = "Offer whole-run releases",
                ValueKind = FieldValueKind.Boolean,
                Role = SettingRole.Flag,
                Advanced = true,
                DefaultValue = "true",
                HelpText = "A whole-run release satisfies every unit in the run at once."
            }
        ],
        Presets =
        [
            new ProviderPreset(
                "usenet",
                "Usenet",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CategoriesSetting] = "5000",
                    [OfferPacksSetting] = "true"
                })
        ]
    };

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failures = new List<ValidationFailure>();
        var settings = invocation.Definition.Settings;

        if (!settings.TryGetValue(BaseUrlSetting, out var baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            failures.Add(new ValidationFailure(
                BaseUrlSetting,
                "An absolute URL is required.",
                ValidationSeverity.Error,
                CoreErrorCode.InvalidConfiguration));
        }

        if (!settings.TryGetValue(ApiKeySetting, out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            failures.Add(new ValidationFailure(
                ApiKeySetting,
                "An API key is required.",
                ValidationSeverity.Error,
                CoreErrorCode.MissingConfiguration));
        }

        return Task.FromResult(
            failures.Count == 0 ? ValidationOutcome.Success : ValidationOutcome.Failed([.. failures]));
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
            CategoryOptionSource,
            StringComparison.Ordinal)
            ?
            [
                new FacetValue("5000", "TV"),
                new FacetValue("5030", "TV/SD"),
                new FacetValue("5040", "TV/HD"),
                new FacetValue("5045", "TV/UHD"),
                new FacetValue("5070", "TV/Anime")
            ]
            : [];

        return Task.FromResult(options);
    }

    /// <inheritdoc />
    public Task<IndexerProfile> DescribeAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new IndexerProfile
        {
            SearchProfiles =
            [
                new SearchProfile
                {
                    ProfileId = "ordinal",

                    // Serves the unit and whole-run search kinds: both require the ordinal term and this
                    // profile has it. The bindings are where the wire spelling lives, and they are the
                    // indexer protocol's business, not the media kind's.
                    Terms = [SearchTerm.FreeText, SearchTerm.WorkTitle, SearchTerm.Ordinal],
                    Categories = [.. Categories],
                    SupportsRawSearch = true,
                    Bindings =
                    [
                        new SearchTermBinding(SearchTerm.FreeText, null, "q"),
                        new SearchTermBinding(SearchTerm.WorkTitle, null, "title"),
                        new SearchTermBinding(SearchTerm.Ordinal, TvIds.SeasonComponentId, "season"),
                        new SearchTermBinding(SearchTerm.Ordinal, TvIds.EpisodeComponentId, "ep")
                    ]
                },
                new SearchProfile
                {
                    ProfileId = "identifier",
                    Terms = [SearchTerm.ExternalIdentifier, SearchTerm.FreeText, SearchTerm.Ordinal],
                    Categories = [.. Categories],
                    Bindings =
                    [
                        new SearchTermBinding(SearchTerm.ExternalIdentifier, TvIds.TvdbScheme, "tvdbid"),
                        new SearchTermBinding(SearchTerm.ExternalIdentifier, TvIds.ImdbScheme, "imdbid")
                    ]
                },
                new SearchProfile
                {
                    ProfileId = "text",

                    // Serves the calendar search kind, which declares no required term at all. Three of the
                    // surveyed cross-application adapters gate on categories alone, and the model has to
                    // allow that rather than force every kind to invent a required term.
                    Terms = [SearchTerm.FreeText, SearchTerm.WorkTitle],
                    Categories = [.. Categories],
                    SupportsRawSearch = true
                }
            ],
            Categories = [.. Categories],
            SupportsRss = true,
            SupportsPagination = true,
            MaxPageSize = 100,
            DefaultPageSize = 50,
            Flags = ["freeleech", "scene"],
            Privacy = IndexerPrivacy.SemiPrivate
        });
    }

    /// <inheritdoc />
    public async Task<ReleaseQueryResult> SearchAsync(
        ProviderInvocation invocation,
        ReleaseQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        // The term gate, provider-side. A query using a term this provider never declared short-circuits to
        // an empty result rather than issuing a malformed request.
        var supported = new HashSet<SearchTerm>
        {
            SearchTerm.FreeText, SearchTerm.WorkTitle, SearchTerm.Ordinal, SearchTerm.ExternalIdentifier,
            SearchTerm.Date
        };

        var unsupported = query.Arguments
            .Select(argument => argument.Term)
            .Where(term => !supported.Contains(term))
            .Distinct()
            .ToList();

        if (unsupported.Count != 0)
        {
            return new ReleaseQueryResult(
                [],
                false,
                [$"This indexer cannot be asked for: {string.Join(", ", unsupported)}."]);
        }

        await invocation.Session
            .SetAsync(SessionKeyLastQuery, query.FreeText, TimeSpan.FromHours(1), cancellationToken)
            .ConfigureAwait(false);

        var series = Resolve(query);

        if (series is null)
        {
            return new ReleaseQueryResult([], false, []);
        }

        var offerPacks = !invocation.Definition.Settings.TryGetValue(OfferPacksSetting, out var packs)
            || !string.Equals(packs, "false", StringComparison.OrdinalIgnoreCase);

        var releases = Synthesize(series, query, offerPacks).ToList();
        var limited = releases.Skip(query.Offset).Take(Math.Max(1, query.Limit)).ToList();

        return new ReleaseQueryResult(limited, limited.Count < releases.Count - query.Offset, []);
    }

    /// <inheritdoc />
    public Task<ReleaseFetch> FetchAsync(
        ProviderInvocation invocation,
        Uri link,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = Encoding.UTF8.GetBytes($"arronix-stub-nzb:{link}");

        return Task.FromResult(new ReleaseFetch(payload, "catalog.nzb", "application/x-nzb"));
    }

    private static IReadOnlyList<CategoryId> Categories { get; } =
    [
        CategoryId.FromInt(5000),
        CategoryId.FromInt(5030),
        CategoryId.FromInt(5040),
        CategoryId.FromInt(5045),
        CategoryId.FromInt(5070)
    ];

    private IEnumerable<ReleaseListing> Synthesize(
        TvSeriesRecord series,
        ReleaseQuery query,
        bool offerPacks)
    {
        var published = _clock.GetUtcNow().UtcDateTime;
        var title = TvTitleNormalizer.ToQueryText(series.Title).Replace(' ', '.');
        var units = ReadRequestedUnits(series, query);

        foreach (var unit in units)
        {
            foreach (var (suffix, source, resolution, size) in Editions)
            {
                yield return Candidate(
                    $"{title}.{Position(series, unit)}.{resolution}.{source}.x264-CATALOG",
                    $"{series.TvdbId}/{unit.Id}/{suffix}",
                    size,
                    published);
            }
        }

        // A multi-unit release. This is the case the one-file-to-many-units binding exists for, and putting
        // one in the synthetic feed means an end-to-end test reaches it without hand-crafting a title.
        if (units.Count != 0 && units[0].SeasonNumber != TvIds.SpecialsOrdinal)
        {
            var first = units[0];
            var next = _catalog.TryGetByAired(
                series.Id,
                first.SeasonNumber,
                first.EpisodeNumber + 1,
                out var second) && second is not null
                ? second
                : null;

            if (next is not null)
            {
                yield return Candidate(
                    $"{title}.S{Pad(first.SeasonNumber)}E{Pad(first.EpisodeNumber)}"
                    + $"E{Pad(next.EpisodeNumber)}.1080p.WEB-DL.x264-CATALOG",
                    $"{series.TvdbId}/{first.Id}-{next.Id}/multi",
                    3_221_225_472L,
                    published);
            }
        }

        if (!offerPacks || units.Count == 0)
        {
            yield break;
        }

        var run = units[0].SeasonNumber;

        yield return Candidate(
            $"{title}.S{Pad(run)}.COMPLETE.1080p.BluRay.x264-CATALOG",
            $"{series.TvdbId}/S{run}/pack",
            21_474_836_480L,
            published);
    }

    private ReleaseListing Candidate(string title, string key, long size, DateTime published) => new(
        ReleaseId.FromString($"{_id}/{key}"),
        title,
        new Uri($"https://catalog.invalid/download/{Uri.EscapeDataString(key)}"),
        _id.ToString(),
        TvIds.MediaKind,
        size,
        published,
        new Uri($"https://catalog.invalid/details/{Uri.EscapeDataString(key)}"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seeders"] = "17",
            ["leechers"] = "2",
            ["protocol"] = DownloadProtocol.Usenet.ToString()
        });

    private IReadOnlyList<TvEpisodeRecord> ReadRequestedUnits(TvSeriesRecord series, ReleaseQuery query)
    {
        int? season = null;
        int? episode = null;
        DateOnly? date = null;

        foreach (var argument in query.Arguments)
        {
            switch (argument.Term)
            {
                case SearchTerm.Ordinal when string.Equals(
                    argument.ComponentId,
                    TvIds.SeasonComponentId,
                    StringComparison.Ordinal):
                    season = (int?)argument.Value.Number;
                    break;

                case SearchTerm.Ordinal when string.Equals(
                    argument.ComponentId,
                    TvIds.EpisodeComponentId,
                    StringComparison.Ordinal):
                    episode = (int?)argument.Value.Number;
                    break;

                case SearchTerm.Date:
                    date = argument.Value.Date;
                    break;

                default:
                    break;
            }
        }

        if (date is { } airDate)
        {
            return _catalog.FindByAirDate(series.Id, airDate);
        }

        if (season is { } run && episode is { } number)
        {
            return _catalog.TryGetByAired(series.Id, run, number, out var unit) && unit is not null
                ? [unit]
                : [];
        }

        if (season is { } wholeRun)
        {
            return _catalog.EpisodesInSeason(series.Id, wholeRun);
        }

        return _catalog.EpisodesOf(series.Id);
    }

    private static string Position(TvSeriesRecord series, TvEpisodeRecord unit)
    {
        if (series.AddressingScheme == TvAddressingSchemes.Calendar && unit.AirDate is { } airDate)
        {
            return airDate.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        }

        if (series.AddressingScheme == TvAddressingSchemes.Flat && unit.AbsoluteNumber is { } absolute)
        {
            return $"- {absolute.ToString("000", CultureInfo.InvariantCulture)}";
        }

        return $"S{Pad(unit.SeasonNumber)}E{Pad(unit.EpisodeNumber)}";
    }

    private static string Pad(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    private TvSeriesRecord? Resolve(ReleaseQuery query)
    {
        foreach (var argument in query.Arguments)
        {
            if (argument.Term == SearchTerm.ExternalIdentifier
                && argument.Value.External is { } externalId
                && _catalog.TryGetSeriesByExternalId(externalId, out var identified)
                && identified is not null)
            {
                return identified;
            }
        }

        var titleText = query.FreeText;

        foreach (var argument in query.Arguments)
        {
            if (argument.Term == SearchTerm.WorkTitle && argument.Value.Text is { } text)
            {
                titleText = text;
                break;
            }
        }

        var byTitle = _catalog.FindSeriesByTitle(titleText);

        if (byTitle.Count == 1)
        {
            return byTitle[0];
        }

        var needle = TvTitleNormalizer.Normalize(titleText);

        return needle.Length == 0
            ? null
            : _catalog.Series.FirstOrDefault(series =>
                needle.StartsWith(TvTitleNormalizer.Normalize(series.Title), StringComparison.Ordinal));
    }
}
