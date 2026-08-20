using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Plugins;
using System.Linq;


namespace Arronix.Host.Languages;

/// <summary>The admitted language implementations, keyed by the BCP 47 tag they own.</summary>
public sealed class LanguageDefinitionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RegisteredLanguage> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets a stable snapshot ordered by language tag.</summary>
    public IReadOnlyList<ILanguageDefinition> All
    {
        get
        {
            lock (_gate)
            {
                return
                [
                    .. _definitions.Values
                        .OrderBy(static entry => entry.Definition.Language.Code, StringComparer.OrdinalIgnoreCase)
                        .Select(static entry => entry.Definition),
                ];
            }
        }
    }

    /// <summary>Admits one implementation, refusing two owners for the same language tag.</summary>
    public void Register(PluginId plugin, ILanguageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Language.Code);

        lock (_gate)
        {
            if (_definitions.TryGetValue(definition.Language.Code, out var existing))
            {
                throw new InvalidOperationException(
                    $"Language '{definition.Language.Code}' is already owned by extension "
                    + $"'{existing.Plugin}'; extension '{plugin}' cannot replace it by load order.");
            }

            _definitions.Add(definition.Language.Code, new RegisteredLanguage(plugin, definition));
        }
    }

    /// <summary>Finds the exact language, then its unqualified base language.</summary>
    public ILanguageDefinition? Find(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

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
    public void RemoveByPlugin(PluginId plugin)
    {
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

    private sealed record RegisteredLanguage(PluginId Plugin, ILanguageDefinition Definition);
}
