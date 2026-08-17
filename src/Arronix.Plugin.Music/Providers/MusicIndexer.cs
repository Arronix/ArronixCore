// Provider and shape contracts are experimental; this extension is a reference implementer of both.
#pragma warning disable ARX0013, ARX0014, ARX0015
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Music.Seed;

namespace Arronix.Plugin.Music.Providers;

/// <summary>
/// A release source for music, standing in for a live one.
/// </summary>
/// <remarks>
/// <para>
/// The interesting part is not the fabricated results, it is the profile. This provider declares which
/// terms it can be asked and which interop categories it carries, and says nothing whatsoever about media
/// kinds. A kind declares which terms it needs and which categories it belongs to, and says nothing about
/// indexers. Whether the two can work together is set intersection, computed by the platform, and neither
/// side ever names the other's nouns.
/// </para>
/// <para>
/// It is also stateless. Every call takes its invocation, so the definition being served, the session it
/// may keep and the correlation identifier to log under all arrive as arguments. There is no per-definition
/// field to race on because there is nowhere to put one.
/// </para>
/// </remarks>
public sealed class MusicIndexer : IIndexer
{
    /// <summary>The local identifier this provider is registered under.</summary>
    public const string LocalId = "seed-index";

    /// <summary>The search profile identifier covering searches for one work.</summary>
    public const string WorkProfileId = "work";

    /// <summary>The search profile identifier covering searches across a performer's output.</summary>
    public const string CatalogProfileId = "catalog";

    private readonly ProviderId _id;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="plugin">The extension registering it, which qualifies the provider identifier.</param>
    public MusicIndexer(PluginId plugin) => _id = ProviderId.Create(plugin, LocalId);

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
            HelpText = "Where the index is served from.",
        },
        new SettingsField
        {
            FieldId = "api-key",
            Name = "API key",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,

            // One declaration, three obligations: never serialized outward, redacted from telemetry, and
            // presented as a credential rather than as text.
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
            DefaultValue = "3000",
        },
        new SettingsField
        {
            FieldId = "seed-time",
            Name = "Minimum seed time",
            ValueKind = FieldValueKind.Duration,
            Role = SettingRole.Duration,
            Advanced = true,
            Unit = "minutes",
        },
    ];

    /// <summary>Gets the descriptor this provider is registered with.</summary>
    /// <returns>The descriptor.</returns>
    public static ProviderDescriptor Describe() => new()
    {
        LocalId = LocalId,
        Family = ProviderFamily.Indexer,
        Name = "Music index",
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
                    ProfileId = WorkProfileId,
                    Terms =
                    [
                        SearchTerm.FreeText,
                        SearchTerm.WorkTitle,
                        SearchTerm.Creator,
                        SearchTerm.Year,
                        SearchTerm.ExternalIdentifier,
                    ],
                    Categories = MusicShape.AudioCategories,
                    Bindings =
                    [
                        new SearchTermBinding(SearchTerm.FreeText, null, "q"),
                        new SearchTermBinding(SearchTerm.WorkTitle, null, "album"),
                        new SearchTermBinding(SearchTerm.Creator, null, "artist"),
                        new SearchTermBinding(SearchTerm.Year, null, "year"),
                    ],
                    SupportsRawSearch = true,
                },
                new SearchProfile
                {
                    ProfileId = CatalogProfileId,
                    Terms = [SearchTerm.FreeText, SearchTerm.Creator],
                    Categories = MusicShape.AudioCategories,
                    Bindings =
                    [
                        new SearchTermBinding(SearchTerm.FreeText, null, "q"),
                        new SearchTermBinding(SearchTerm.Creator, null, "artist"),
                    ],
                },
            ],
            Categories = MusicShape.AudioCategories,
            SupportsRss = true,
            SupportsPagination = true,
            MaxPageSize = 100,
            DefaultPageSize = 50,
            Flags = ["freeleech", "internal"],
            Privacy = IndexerPrivacy.Private,
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

        var text = query.FreeText;
        var candidates = new List<ReleaseCandidate>();

        foreach (var work in MusicSeed.Works)
        {
            var performer = MusicSeed.Performers.First(candidate => candidate.Id == work.PerformerId);

            if (text.Length > 0
                && !text.Contains(work.Title, StringComparison.OrdinalIgnoreCase)
                && !text.Contains(performer.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var pressing in MusicSeed.PressingsOf(work.Id))
            {
                var title = MusicQueryPlanner.CandidateTitle(work, pressing);
                var recordings = MusicSeed.RecordingsOf(pressing.Id).Count;

                candidates.Add(new ReleaseCandidate(
                    ReleaseId.FromString(
                        string.Create(CultureInfo.InvariantCulture, $"{LocalId}:{pressing.Id}")),
                    title,
                    new Uri(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"https://localhost/releases/{pressing.Id}.torrent")),
                    _id.ToString(),
                    MusicShape.Kind,
                    recordings * 35L * 1024 * 1024,
                    new DateTime(pressing.ReleaseDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    null,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["pressing"] = pressing.CatalogId,
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
