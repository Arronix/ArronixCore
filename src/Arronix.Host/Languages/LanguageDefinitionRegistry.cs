using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Registry;
using System.Linq;


namespace Arronix.Host.Languages;

/// <summary>The admitted language implementations, keyed by the BCP 47 tag they own.</summary>
public sealed class LanguageDefinitionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RegisteredLanguage> _definitions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly PluginPublicationGate _publication;

    /// <summary>Creates a standalone language registry with its own publication boundary.</summary>
    public LanguageDefinitionRegistry()
        : this(new PluginPublicationGate())
    {
    }

    /// <summary>Creates a language registry participating in one publication boundary.</summary>
    public LanguageDefinitionRegistry(PluginPublicationGate publication)
    {
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    }

    /// <summary>Gets the publication boundary this registry participates in.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

    /// <summary>Gets a stable snapshot ordered by language tag.</summary>
    public IReadOnlyList<ILanguageDefinition> All
    {
        get
        {
            using var publication = _publication.EnterRead();
            lock (_gate)
            {
                return
                [
                    .. _definitions.Values
                        .OrderBy(static entry => entry.Code, StringComparer.OrdinalIgnoreCase)
                        .Select(static entry => entry.Definition),
                ];
            }
        }
    }

    /// <summary>Admits one implementation, refusing two owners for the same language tag.</summary>
    internal void Register(PluginId plugin, ILanguageDefinition definition)
    {
        if (!TryPrepare(plugin, definition, out var candidate, out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (!TryPublish(candidate!, out error))
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>Activates no code; snapshots and validates an already-activated language candidate.</summary>
    internal bool TryPrepare(
        PluginId plugin,
        ILanguageDefinition definition,
        out RegisteredLanguage? candidate,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var language = definition.Language;
        ArgumentNullException.ThrowIfNull(language);
        var code = language.Code;
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        candidate = new RegisteredLanguage(plugin, code, definition);

        using var publication = _publication.EnterRead();
        lock (_gate)
        {
            if (_definitions.TryGetValue(candidate.Code, out var existing))
            {
                error = $"Language '{candidate.Code}' is already owned by extension "
                    + $"'{existing.Plugin}'; extension '{plugin}' cannot replace it by load order.";
                candidate = null;
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Publishes one already-built language candidate.</summary>
    internal bool TryPublish(RegisteredLanguage candidate, out string? error)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        using var publication = _publication.EnterWrite();
        lock (_gate)
        {
            if (_definitions.TryGetValue(candidate.Code, out var existing))
            {
                error = $"Language '{candidate.Code}' is already owned by extension "
                    + $"'{existing.Plugin}'; extension '{candidate.Plugin}' cannot replace it by load order.";
                return false;
            }

            _definitions.Add(candidate.Code, candidate);
        }

        error = null;
        return true;
    }

    /// <summary>Removes exactly one language candidate and never a later replacement.</summary>
    internal bool Remove(RegisteredLanguage candidate)
    {
        using var publication = _publication.EnterWrite();
        lock (_gate)
        {
            return _definitions.TryGetValue(candidate.Code, out var current)
                && ReferenceEquals(current, candidate)
                && _definitions.Remove(candidate.Code);
        }
    }

    /// <summary>Finds the exact language, then its unqualified base language.</summary>
    public ILanguageDefinition? Find(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        using var publication = _publication.EnterRead();
        lock (_gate)
        {
            if (_definitions.TryGetValue(language.Code, out var exact))
            {
                return exact.Definition;
            }

            var separator = language.Code.IndexOf('-', StringComparison.Ordinal);
            return separator > 0
                && _definitions.TryGetValue(language.Code[..separator], out var baseLanguage)
                    ? baseLanguage.Definition
                    : null;
        }
    }

    /// <summary>Withdraws every language implementation contributed by one extension.</summary>
    internal void RemoveByPlugin(PluginId plugin)
    {
        using var publication = _publication.EnterWrite();
        lock (_gate)
        {
            foreach (var code in _definitions
                         .Where(pair => pair.Value.Plugin == plugin)
                         .Select(static pair => pair.Key)
                         .ToArray())
            {
                _definitions.Remove(code);
            }
        }
    }

    internal sealed record RegisteredLanguage(
        PluginId Plugin,
        string Code,
        ILanguageDefinition Definition);
}
