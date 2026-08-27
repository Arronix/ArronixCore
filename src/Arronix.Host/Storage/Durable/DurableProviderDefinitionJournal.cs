using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;
using Arronix.Host.Providers;
using Microsoft.EntityFrameworkCore;

namespace Arronix.Host.Storage.Durable;

/// <summary>
/// The operator's configured provider instances, written to the local database.
/// </summary>
/// <remarks>
/// <para>
/// One write is one transaction over the definition and the settings and kind narrowing beneath it, so a
/// definition is never half-configured after a restart. Settings are the fields the provider itself
/// declared, keyed by the identifiers it declared them under; the platform invents none of them.
/// </para>
/// <para>
/// Everything the operator stated is kept, tags included. What is not kept is whether the definition's
/// implementation is currently loaded: that is recomputed against the registry every time the installation
/// changes, and a stored answer would be a stale one.
/// </para>
/// </remarks>
/// <param name="schema">The created schema, and the factory contexts come from.</param>
internal sealed class DurableProviderDefinitionJournal(StoreSchema schema)
    : IProviderDefinitionJournal
{
    private readonly IDbContextFactory<ArronixStoreContext> _contexts =
        (schema ?? throw new ArgumentNullException(nameof(schema))).Contexts;

    /// <inheritdoc />
    public IReadOnlyList<ProviderDefinition> Load()
    {
        using var store = _contexts.CreateDbContext();

        var rows = store.ProviderDefinitions.AsNoTracking().OrderBy(row => row.Id).ToList();

        if (rows.Count == 0)
        {
            return [];
        }

        var settings = store.ProviderDefinitionSettings.AsNoTracking().ToList();
        var kinds = store.ProviderDefinitionKinds.AsNoTracking().OrderBy(row => row.Ordinal).ToList();
        var tags = store.ProviderDefinitionTags.AsNoTracking().OrderBy(row => row.Ordinal).ToList();

        return
        [
            .. rows.Select(row => new ProviderDefinition
            {
                Id = row.Id,
                Provider = ParseProvider(row.Provider),
                Family = (ProviderFamily)row.Family,
                Name = row.Name,
                Enabled = row.Enabled,
                Priority = row.Priority,
                Settings = settings
                    .Where(setting => setting.DefinitionId == row.Id)
                    .ToDictionary(
                        static setting => setting.FieldId,
                        static setting => setting.Value,
                        StringComparer.Ordinal),
                MediaKinds =
                [
                    .. kinds
                        .Where(kind => kind.DefinitionId == row.Id)
                        .Select(static kind => MediaKindId.FromString(kind.Kind)),
                ],
                Tags =
                [
                    .. tags.Where(tag => tag.DefinitionId == row.Id).Select(static tag => tag.Value),
                ],
            }),
        ];
    }

    private static ProviderId ParseProvider(string value)
        => ProviderId.TryParse(value, out var id)
            ? id
            : throw new ArronixException(
                CoreErrorCode.InvalidConfiguration,
                $"A stored provider definition names '{value}', which is not a well-formed provider identifier.");

    /// <inheritdoc />
    public async ValueTask WriteAsync(
        ProviderDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await store.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var row = await store.ProviderDefinitions
            .FirstOrDefaultAsync(candidate => candidate.Id == definition.Id, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new ProviderDefinitionRow { Id = definition.Id };
            store.ProviderDefinitions.Add(row);
        }
        else
        {
            store.ProviderDefinitionSettings.RemoveRange(
                await store.ProviderDefinitionSettings
                    .Where(candidate => candidate.DefinitionId == definition.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false));

            store.ProviderDefinitionKinds.RemoveRange(
                await store.ProviderDefinitionKinds
                    .Where(candidate => candidate.DefinitionId == definition.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false));

            store.ProviderDefinitionTags.RemoveRange(
                await store.ProviderDefinitionTags
                    .Where(candidate => candidate.DefinitionId == definition.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false));
        }

        row.Provider = definition.Provider.Value;
        row.Family = (int)definition.Family;
        row.Name = definition.Name;
        row.Enabled = definition.Enabled;
        row.Priority = definition.Priority;

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (fieldId, value) in definition.Settings)
        {
            store.ProviderDefinitionSettings.Add(new ProviderDefinitionSettingRow
            {
                DefinitionId = definition.Id,
                FieldId = fieldId,
                Value = value,
            });
        }

        for (var ordinal = 0; ordinal < definition.MediaKinds.Count; ordinal++)
        {
            store.ProviderDefinitionKinds.Add(new ProviderDefinitionKindRow
            {
                DefinitionId = definition.Id,
                Ordinal = ordinal,
                Kind = definition.MediaKinds[ordinal].Value,
            });
        }

        for (var ordinal = 0; ordinal < definition.Tags.Count; ordinal++)
        {
            store.ProviderDefinitionTags.Add(new ProviderDefinitionTagRow
            {
                DefinitionId = definition.Id,
                Ordinal = ordinal,
                Value = definition.Tags[ordinal],
            });
        }

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var store = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await store.ProviderDefinitions
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return;
        }

        store.ProviderDefinitions.Remove(row);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
