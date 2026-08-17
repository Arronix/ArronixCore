// Provider, plugin and shape contracts are experimental; this extension implements all three.
#pragma warning disable ARX0013, ARX0014, ARX0015
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Books.Seed;

namespace Arronix.Plugin.Books.Providers;

/// <summary>
/// A catalog source for books, standing in for a live one.
/// </summary>
/// <remarks>
/// <para>
/// The selection policy is doing real work here, and books are the kind that proves why it has to. An
/// upstream bibliography is unbounded and mostly noise, so a fetch that returns everything and lets the
/// platform sort it out creates hundreds of rows nobody wants. The policy is therefore applied here, at
/// materialization: excluded works are never nodes in the first place.
/// </para>
/// <para>
/// All three facet kinds are consulted - an enumeration over languages, thresholds over prominence and
/// length, and flags for the heuristics - which is why an enumerated-only facet vocabulary would not have
/// been enough.
/// </para>
/// </remarks>
public sealed class BooksCataloger : ICataloger
{
    /// <summary>The local identifier this provider is registered under.</summary>
    public const string LocalId = "seed-catalog";

    private readonly ProviderId _id;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="plugin">The extension registering it, which qualifies the provider identifier.</param>
    public BooksCataloger(PluginId plugin) => _id = ProviderId.Create(plugin, LocalId);

    /// <inheritdoc />
    public ProviderId Id => _id;

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Cataloger;

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities =>
        CatalogerCapabilities.Search
        | CatalogerCapabilities.BulkFetch
        | CatalogerCapabilities.Discovery;

    /// <summary>Gets the settings this provider declares.</summary>
    public static IReadOnlyList<SettingsField> SettingsSchema { get; } =
    [
        new SettingsField
        {
            FieldId = "base-url",
            Name = "Base URL",
            ValueKind = FieldValueKind.Link,
            Role = SettingRole.Endpoint,
            Required = true,
            DefaultValue = "https://www.goodreads.com",
        },
        new SettingsField
        {
            FieldId = "api-key",
            Name = "API key",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,
            Sensitivity = SettingSensitivity.Secret,
        },
        new SettingsField
        {
            FieldId = "selection-policy",
            Name = "Metadata profile",
            ValueKind = FieldValueKind.Reference,
            Role = SettingRole.SelectionPolicyRef,
            HelpText = "Which books of a followed author should exist at all.",
            HelpWarning =
                "Tightening this removes books that no longer qualify, unless they were added by hand or "
                + "have files on disk.",
        },
    ];

    /// <summary>Gets the descriptor this provider is registered with.</summary>
    /// <returns>The descriptor.</returns>
    public static ProviderDescriptor Describe() => new()
    {
        LocalId = LocalId,
        Family = ProviderFamily.Cataloger,
        Name = "Book catalog",
        Description = "The seeded book catalog this reference extension ships with.",
        Settings = SettingsSchema,
    };

    /// <inheritdoc />
    public Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            invocation.Definition.Settings.ContainsKey("base-url")
                ? ValidationOutcome.Success
                : ValidationOutcome.Failed(
                    new ValidationFailure("base-url", "A base URL is required.")));
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
    public Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(
        ProviderInvocation invocation,
        MetadataQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var wanted = query.Text ?? string.Empty;
        var hits = new List<MetadataSearchHit>();

        // Discriminated by level. A search box that can return writers or works is exactly the case that
        // forced an untyped result in the surveyed original, and the level makes it typed for free.
        if (query.Level is null || query.Level == BooksShape.WriterLevel)
        {
            hits.AddRange(BooksSeed.Writers
                .Where(writer => Contains(writer.Name, wanted))
                .Select(writer => new MetadataSearchHit(
                    ExternalId.Of(BooksShape.CatalogScheme, writer.CatalogId),
                    BooksShape.WriterLevel,
                    writer.Name,
                    null,
                    null,
                    [])));
        }

        if (query.Level is null || query.Level == BooksShape.WorkLevel)
        {
            hits.AddRange(BooksSeed.Works
                .Where(work => Contains(work.Title, wanted))
                .Select(work => new MetadataSearchHit(
                    ExternalId.Of(BooksShape.CatalogScheme, work.CatalogId),
                    BooksShape.WorkLevel,
                    work.Title,
                    work.ReleaseDate.Year,
                    work.Subtitle,
                    [])));
        }

        return Task.FromResult<IReadOnlyList<MetadataSearchHit>>(hits);
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

        var writer = BooksSeed.Writers.FirstOrDefault(
            candidate => string.Equals(candidate.CatalogId, id.Value, StringComparison.Ordinal));

        if (writer is null)
        {
            return Task.FromResult<MetadataGraph?>(null);
        }

        var nodes = new List<MetadataNode>
        {
            new(
                BooksShape.WriterLevel,
                ExternalId.Of(BooksShape.CatalogScheme, writer.CatalogId),
                null,
                writer.Name,
                new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [BooksFields.Name] = FieldValue.OfText(writer.Name),
                    [BooksFields.SortName] = FieldValue.OfText(writer.SortName),
                },
                CoordinateSet.Empty),
        };

        foreach (var work in BooksSeed.Works.Where(candidate => candidate.WriterId == writer.Id))
        {
            if (!WorkSurvives(work, policy))
            {
                continue;
            }

            var manifestations = BooksSeed.ManifestationsOf(work.Id)
                .Where(manifestation => ManifestationSurvives(manifestation, policy))
                .ToList();

            // A work whose every manifestation was filtered away is not a work worth a row.
            if (manifestations.Count == 0)
            {
                continue;
            }

            nodes.Add(new MetadataNode(
                BooksShape.WorkLevel,
                ExternalId.Of(BooksShape.CatalogScheme, work.CatalogId),
                ExternalId.Of(BooksShape.CatalogScheme, writer.CatalogId),
                work.Title,
                new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [BooksFields.Title] = FieldValue.OfText(work.Title),
                    [BooksFields.ReleaseDate] = FieldValue.OfDate(work.ReleaseDate),
                    [BooksFields.Popularity] = FieldValue.OfDecimal(work.Popularity),
                },
                CoordinateSet.Of(
                    new CoordinateReading(
                        BooksShape.WorkSpaceId,
                        Coordinate.Singleton,
                        CoordinateConfidence.Verified))));

            foreach (var manifestation in manifestations)
            {
                nodes.Add(new MetadataNode(
                    BooksShape.ManifestationLevel,
                    ExternalId.Of(BooksShape.CatalogScheme, manifestation.CatalogId),
                    ExternalId.Of(BooksShape.CatalogScheme, work.CatalogId),
                    manifestation.Title,
                    new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                    {
                        [BooksFields.Title] = FieldValue.OfText(manifestation.Title),
                        [BooksFields.Flavor] = FieldValue.OfEnumerated(manifestation.Flavor),
                        [BooksFields.Language] = FieldValue.OfLanguage(
                            new Language(manifestation.LanguageCode, manifestation.LanguageCode)),
                    },
                    CoordinateSet.Of(
                        new CoordinateReading(
                            BooksShape.ManifestationSpaceId,
                            Coordinate.OfLabel(manifestation.CarrierDescription),
                            CoordinateConfidence.Asserted))));
            }
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

        return Task.FromResult<IReadOnlyList<ExternalId>>([]);
    }

    private static bool WorkSurvives(SeedWork work, SelectionPolicy policy)
    {
        if (Threshold(policy, BooksFacets.MinimumPopularity) is { } minimum
            && work.Popularity < minimum)
        {
            return false;
        }

        if (Flag(policy, BooksFacets.SkipCollectionSecondary))
        {
            var memberships = BooksSeed.MembershipsOf(work.Id);

            if (memberships.Count > 0 && !memberships.Any(membership => membership.IsPrimary))
            {
                return false;
            }
        }

        if (Flag(policy, BooksFacets.SkipPartsAndSets) && IsPartOrSet(work))
        {
            return false;
        }

        return true;
    }

    private static bool ManifestationSurvives(SeedManifestation manifestation, SelectionPolicy policy)
    {
        if (policy.AllowedValues.TryGetValue(BooksFacets.Language, out var languages)
            && languages.Count > 0
            && !languages.Contains(manifestation.LanguageCode, StringComparer.Ordinal))
        {
            return false;
        }

        if (Threshold(policy, BooksFacets.MinimumPages) is { } minimumPages
            && manifestation.PageCount is { } pages
            && pages < minimumPages)
        {
            return false;
        }

        if (Flag(policy, BooksFacets.SkipUnidentified)
            && manifestation.BookNumber is null
            && manifestation.RetailerId is null)
        {
            return false;
        }

        return true;
    }

    private static bool IsPartOrSet(SeedWork work)
    {
        var memberships = BooksSeed.MembershipsOf(work.Id);

        if (memberships.Count == 0)
        {
            return false;
        }

        var labeled = memberships.Where(membership => membership.Position.Length > 0).ToList();

        // A collection label that is present but parses as nothing numeric is the signature of an omnibus
        // or a fragment: "1-3", "1 of 3". A label that is simply absent says nothing either way.
        return labeled.Count > 0
            && labeled.TrueForAll(membership => !double.TryParse(
                membership.Position,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out _));
    }

    private static double? Threshold(SelectionPolicy policy, string facetId) =>
        policy.Thresholds.TryGetValue(facetId, out var value) ? value : null;

    private static bool Flag(SelectionPolicy policy, string facetId) =>
        policy.Flags.TryGetValue(facetId, out var value) && value;

    private static bool Contains(string subject, string wanted) =>
        wanted.Length == 0 || subject.Contains(wanted, StringComparison.OrdinalIgnoreCase);
}
