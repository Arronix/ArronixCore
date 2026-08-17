// Provider and shape contracts are experimental; this extension is a reference implementer of both.
#pragma warning disable ARX0013, ARX0014, ARX0015
using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Music.Seed;

namespace Arronix.Plugin.Music.Providers;

/// <summary>
/// A catalog source for music, standing in for a live one.
/// </summary>
/// <remarks>
/// <para>
/// It reads the same seeded catalog the item source projects, which is what makes it a stub. Everything
/// else about it is real: the search result is discriminated by level rather than untyped, so a caller
/// never has to test the runtime type of what came back, and the fetch takes the resolved selection
/// policy so that "which works of this performer should exist at all" is answered before a single row is
/// created rather than by deleting rows afterwards.
/// </para>
/// <para>
/// Delta synchronization is declared absent rather than implemented and ignored. Two of the four surveyed
/// applications have it and two do not; a capability flag is how a caller finds out without trying.
/// </para>
/// </remarks>
public sealed class MusicCataloger : ICataloger
{
    /// <summary>The local identifier this provider is registered under.</summary>
    public const string LocalId = "seed-catalog";

    private readonly ProviderId _id;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="plugin">The extension registering it, which qualifies the provider identifier.</param>
    public MusicCataloger(PluginId plugin) => _id = ProviderId.Create(plugin, LocalId);

    /// <inheritdoc />
    public ProviderId Id => _id;

    /// <inheritdoc />
    public ProviderFamily Family => ProviderFamily.Cataloger;

    /// <inheritdoc />
    public CatalogerCapabilities Capabilities =>
        CatalogerCapabilities.Search | CatalogerCapabilities.BulkFetch;

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
            DefaultValue = "https://musicbrainz.org",
            HelpText = "Where the catalog is served from.",
        },
        new SettingsField
        {
            FieldId = "contact",
            Name = "Contact address",
            ValueKind = FieldValueKind.Text,
            Role = SettingRole.Value,
            Sensitivity = SettingSensitivity.UserName,
            HelpText = "Sent with each request so the service can identify this installation.",
        },
        new SettingsField
        {
            FieldId = "selection-policy",
            Name = "Metadata profile",
            ValueKind = FieldValueKind.Reference,
            Role = SettingRole.SelectionPolicyRef,
            HelpText = "Which albums of a followed artist should exist at all.",
        },
    ];

    /// <summary>Gets the descriptor this provider is registered with.</summary>
    /// <returns>The descriptor.</returns>
    public static ProviderDescriptor Describe() => new()
    {
        LocalId = LocalId,
        Family = ProviderFamily.Cataloger,
        Name = "Music catalog",
        Description = "The seeded music catalog this reference extension ships with.",
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

        var hits = new List<MetadataSearchHit>();
        var wanted = query.Text ?? string.Empty;

        if (query.Level is null || query.Level == MusicShape.PerformerLevel)
        {
            hits.AddRange(MusicSeed.Performers
                .Where(performer => Contains(performer.Name, wanted))
                .Select(performer => new MetadataSearchHit(
                    ExternalId.Of(MusicShape.CatalogScheme, performer.CatalogId),
                    MusicShape.PerformerLevel,
                    performer.Name,
                    null,
                    performer.Disambiguation,
                    [])));
        }

        if (query.Level is null || query.Level == MusicShape.WorkLevel)
        {
            hits.AddRange(MusicSeed.Works
                .Where(work => Contains(work.Title, wanted))
                .Select(work => new MetadataSearchHit(
                    ExternalId.Of(MusicShape.CatalogScheme, work.CatalogId),
                    MusicShape.WorkLevel,
                    work.Title,
                    work.ReleaseDate.Year,
                    work.Disambiguation,
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

        var performer = MusicSeed.Performers.FirstOrDefault(
            candidate => string.Equals(candidate.CatalogId, id.Value, StringComparison.Ordinal));

        if (performer is null)
        {
            return Task.FromResult<MetadataGraph?>(null);
        }

        var nodes = new List<MetadataNode>
        {
            new(
                MusicShape.PerformerLevel,
                ExternalId.Of(MusicShape.CatalogScheme, performer.CatalogId),
                null,
                performer.Name,
                new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [MusicFields.Name] = FieldValue.OfText(performer.Name),
                    [MusicFields.SortName] = FieldValue.OfText(performer.SortName),
                    [MusicFields.PerformerKind] = FieldValue.OfEnumerated(performer.PerformerKind),
                },
                CoordinateSet.Empty),
        };

        foreach (var work in MusicSeed.Works.Where(candidate => candidate.PerformerId == performer.Id))
        {
            // The policy is applied here, at materialization, not afterwards by deleting rows. Anything
            // the policy excludes is simply never a node.
            if (!IsAllowed(policy, MusicFacets.PrimaryKind, work.PrimaryKind))
            {
                continue;
            }

            nodes.Add(new MetadataNode(
                MusicShape.WorkLevel,
                ExternalId.Of(MusicShape.CatalogScheme, work.CatalogId),
                ExternalId.Of(MusicShape.CatalogScheme, performer.CatalogId),
                work.Title,
                new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    [MusicFields.Title] = FieldValue.OfText(work.Title),
                    [MusicFields.ReleaseDate] = FieldValue.OfDate(work.ReleaseDate),
                    [MusicFields.PrimaryKind] = FieldValue.OfEnumerated(work.PrimaryKind),
                },
                CoordinateSet.Empty));

            foreach (var pressing in MusicSeed.PressingsOf(work.Id))
            {
                if (!IsAllowed(policy, MusicFacets.Provenance, pressing.Provenance))
                {
                    continue;
                }

                nodes.Add(new MetadataNode(
                    MusicShape.PressingLevel,
                    ExternalId.Of(MusicShape.CatalogScheme, pressing.CatalogId),
                    ExternalId.Of(MusicShape.CatalogScheme, work.CatalogId),
                    pressing.Title,
                    new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                    {
                        [MusicFields.Title] = FieldValue.OfText(pressing.Title),
                        [MusicFields.Provenance] = FieldValue.OfEnumerated(pressing.Provenance),
                    },
                    CoordinateSet.Empty));

                foreach (var recording in MusicSeed.RecordingsOf(pressing.Id))
                {
                    nodes.Add(new MetadataNode(
                        MusicShape.RecordingLevel,
                        ExternalId.Of(MusicShape.CatalogScheme, recording.CatalogId),
                        ExternalId.Of(MusicShape.CatalogScheme, pressing.CatalogId),
                        recording.Title,
                        new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                        {
                            [MusicFields.Title] = FieldValue.OfText(recording.Title),
                            [MusicFields.Duration] = FieldValue.OfDuration(recording.Duration),
                        },
                        CoordinateSet.Of(
                            new CoordinateReading(
                                MusicShape.CarrierPositionSpaceId,
                                Coordinate.OfOrdinals(
                                    OrdinalPath.Of(recording.Carrier, recording.Position)),
                                CoordinateConfidence.Verified))));
                }
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

        // Declared absent rather than faked: the capability flag says so, and a caller that respects it
        // never gets here.
        return Task.FromResult<IReadOnlyList<ExternalId>>([]);
    }

    private static bool IsAllowed(SelectionPolicy policy, string facetId, string value)
    {
        if (!policy.AllowedValues.TryGetValue(facetId, out var allowed) || allowed.Count == 0)
        {
            return true;
        }

        return allowed.Contains(value, StringComparer.Ordinal);
    }

    private static bool Contains(string subject, string wanted) =>
        wanted.Length == 0 || subject.Contains(wanted, StringComparison.OrdinalIgnoreCase);
}
