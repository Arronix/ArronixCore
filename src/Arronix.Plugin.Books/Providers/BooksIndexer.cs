using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Books.Seed;

namespace Arronix.Plugin.Books.Providers;

/// <summary>
/// A release source for books, standing in for a live one.
/// </summary>
/// <remarks>
/// Two search profiles rather than one, split by category band rather than by media kind: written copies
/// are carried under the book categories and spoken ones under audio. The extension knows that and the
/// platform does not have to, because eligibility is computed by intersecting declared category sets.
/// </remarks>
public sealed class BooksIndexer : IIndexer
{
    /// <summary>The local identifier this provider is registered under.</summary>
    public const string LocalId = "seed-index";

    /// <summary>The search profile identifier covering written copies.</summary>
    public const string WrittenProfileId = "written";

    /// <summary>The search profile identifier covering spoken copies.</summary>
    public const string SpokenProfileId = "spoken";

    private readonly ProviderId _id;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="context">The capability-scoped plugin context supplied by DI.</param>
    public BooksIndexer(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _id = ProviderId.Create(context.PluginId, LocalId);
    }

    /// <inheritdoc />
    public ProviderId Id => _id;

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Indexer;

    /// <summary>Gets the settings this provider declares.</summary>
    public static IReadOnlyList<SettingsField> SettingsSchema { get; } =
    [
        new SettingsField
        {
            FieldId = "base-url",
            Name = "URL",
            ValueKind = FieldValueKind.Link,
            Role = SettingRole.Endpoint,
            Required = true,
        },
        new SettingsField
        {
            FieldId = "api-key",
            Name = "API key",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,
            Sensitivity = SettingSensitivity.Secret,
            Required = true,
        },
        new SettingsField
        {
            FieldId = "categories",
            Name = "Categories",
            ValueKind = FieldValueKind.Integer,
            Role = SettingRole.CategorySet,
            Multivalued = true,
            Advanced = true,
            DefaultValue = "7000",
        },
    ];

    /// <summary>Gets the descriptor this provider is registered with.</summary>
    /// <returns>The descriptor.</returns>
    public static ProviderDescriptor Describe() => new()
    {
        LocalId = LocalId,
        Family = ProviderFamily.Indexer,
        Name = "Book index",
        Description = "The seeded release index this reference extension ships with.",
        Settings = SettingsSchema,
        Protocols = [DownloadProtocol.Torrent, DownloadProtocol.Usenet],
    };

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failures = new List<ValidationFailure>();

        if (!invocation.Definition.Settings.ContainsKey("base-url"))
        {
            failures.Add(new ValidationFailure("base-url", "A URL is required."));
        }

        if (!invocation.Definition.Settings.ContainsKey("api-key"))
        {
            failures.Add(new ValidationFailure("api-key", "An API key is required."));
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

        return Task.FromResult<IReadOnlyList<FacetValue>>([]);
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
                    ProfileId = WrittenProfileId,
                    Terms =
                    [
                        SearchTerm.FreeText,
                        SearchTerm.WorkTitle,
                        SearchTerm.Creator,
                        SearchTerm.Year,
                        SearchTerm.ExternalIdentifier,
                    ],
                    Categories = BooksShape.WrittenCategories,
                    Bindings =
                    [
                        new SearchTermBinding(SearchTerm.FreeText, null, "q"),
                        new SearchTermBinding(SearchTerm.WorkTitle, null, "title"),
                        new SearchTermBinding(SearchTerm.Creator, null, "author"),
                        new SearchTermBinding(SearchTerm.Year, null, "year"),
                    ],
                    SupportsRawSearch = true,
                },
                new SearchProfile
                {
                    ProfileId = SpokenProfileId,
                    Terms = [SearchTerm.FreeText, SearchTerm.WorkTitle, SearchTerm.Creator],
                    Categories = BooksShape.SpokenCategories,
                    Bindings =
                    [
                        new SearchTermBinding(SearchTerm.FreeText, null, "q"),
                        new SearchTermBinding(SearchTerm.WorkTitle, null, "title"),
                        new SearchTermBinding(SearchTerm.Creator, null, "author"),
                    ],
                },
            ],
            Categories = BooksShape.AllCategories,
            SupportsRss = true,
            SupportsPagination = true,
            MaxPageSize = 100,
            DefaultPageSize = 50,
            Privacy = IndexerPrivacy.SemiPrivate,
        });
    }

    /// <inheritdoc />
    public Task<ReleaseQueryResult> SearchAsync(
        ProviderInvocation invocation,
        ReleaseQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<ReleaseListing>();

        foreach (var work in BooksSeed.Works)
        {
            var writer = BooksSeed.Writers.First(candidate => candidate.Id == work.WriterId);

            if (query.FreeText.Length > 0
                && !query.FreeText.Contains(work.Title, StringComparison.OrdinalIgnoreCase)
                && !query.FreeText.Contains(writer.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var manifestation in BooksSeed.ManifestationsOf(work.Id))
            {
                var categories = string.Equals(
                    manifestation.Flavor,
                    BooksShape.SpokenFamilyId,
                    StringComparison.Ordinal)
                    ? BooksShape.SpokenCategories
                    : BooksShape.WrittenCategories;

                // The category gate, applied here as well as by the platform before dispatch. An index
                // that carries only book categories must not answer an audio query with a text file.
                if (query.Categories.Count > 0
                    && !query.Categories.Any(category => categories.Contains(category)))
                {
                    continue;
                }

                var size = manifestation.PageCount is { } pages
                    ? pages * 3L * 1024
                    : (long)(manifestation.RunningTime?.TotalMinutes ?? 300) * 900 * 1024;

                candidates.Add(new ReleaseListing(
                    ReleaseId.FromString(
                        string.Create(CultureInfo.InvariantCulture, $"{LocalId}:{manifestation.Id}")),
                    BooksQueryPlanner.CandidateTitle(work, manifestation),
                    new Uri(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"https://localhost/releases/{manifestation.Id}.torrent")),
                    _id.ToString(),
                    BooksShape.Kind,
                    size,
                    new DateTime(manifestation.ReleaseDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["flavor"] = manifestation.Flavor,
                        ["parts"] = manifestation.PartCount.ToString(CultureInfo.InvariantCulture),
                    }));
            }
        }

        var window = candidates.Skip(query.Offset).Take(query.Limit).ToList();

        return Task.FromResult(new ReleaseQueryResult(
            window,
            window.Count < candidates.Count - query.Offset,
            []));
    }

    /// <inheritdoc />
    public Task<ReleaseFetch> FetchAsync(
        ProviderInvocation invocation,
        Uri link,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ReleaseFetch(
            ReadOnlyMemory<byte>.Empty,
            null,
            "application/x-bittorrent"));
    }
}
