using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Contributions;
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

    /// <summary>
    /// Gets every admitted language, ordered by tag, each with the ticket that keeps it callable.
    /// </summary>
    /// <remarks>
    /// A language definition is plugin code — comparison, folding, sort and query rules all run inside it —
    /// so it is handed out leased. Dispose the set, not its elements one at a time.
    /// </remarks>
    internal LeasedSet<ILanguageDefinition> LeaseAll()
    {
        var leased = new List<Leased<ILanguageDefinition>>();
        var set = new LeasedSet<ILanguageDefinition>(leased);

        try
        {
            using var publication = _publication.EnterRead();

            lock (_gate)
            {
                foreach (var entry in _definitions.Values
                             .OrderBy(static entry => entry.Code, StringComparer.OrdinalIgnoreCase))
                {
                    leased.Add(new Leased<ILanguageDefinition>(entry.Definition, Ticket(entry)));
                }
            }
        }
        catch
        {
            set.Dispose();
            throw;
        }

        return set;
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
        out string? error,
        IInvocationLifetime? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var language = definition.Language;
        ArgumentNullException.ThrowIfNull(language);
        var code = language.Code;
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        candidate = new RegisteredLanguage(plugin, code, definition, lifetime);

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

    /// <summary>
    /// Gets the admitted implementations, ordered by tag, for inspection rather than invocation.
    /// </summary>
    /// <remarks>
    /// Internal: reading identity is safe, calling one is not. Every path that runs a language-owned
    /// operation goes through <see cref="Lease"/> or <see cref="LeaseAll"/>.
    /// </remarks>
    internal IReadOnlyList<ILanguageDefinition> All
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

    /// <summary>Gets the tags an implementation is admitted for, ordered.</summary>
    public IReadOnlyList<string> Codes
    {
        get
        {
            using var publication = _publication.EnterRead();
            lock (_gate)
            {
                return [.. _definitions.Keys.OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>Finds the exact language, then its unqualified base language, and leases it.</summary>
    /// <param name="language">The language wanted.</param>
    /// <returns>The leased implementation, or <see langword="null"/> when no extension owns that tag.</returns>
    internal Leased<ILanguageDefinition>? Lease(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        using var publication = _publication.EnterRead();
        lock (_gate)
        {
            if (_definitions.TryGetValue(language.Code, out var exact))
            {
                return new Leased<ILanguageDefinition>(exact.Definition, Ticket(exact));
            }

            var separator = language.Code.IndexOf('-', StringComparison.Ordinal);

            return separator > 0 && _definitions.TryGetValue(language.Code[..separator], out var baseLanguage)
                ? new Leased<ILanguageDefinition>(baseLanguage.Definition, Ticket(baseLanguage))
                : null;
        }
    }

    /// <summary>Takes the ticket a published language's extension must still be able to give.</summary>
    private static IDisposable? Ticket(RegisteredLanguage entry)
    {
        if (entry.Lifetime is not { } lifetime)
        {
            return null;
        }

        if (lifetime.TryEnter(out var ticket))
        {
            return ticket;
        }

        throw new InvalidOperationException(
            $"Language '{entry.Code}' is still published while extension '{entry.Plugin}' is closed to "
            + "invocation. Removing a contribution and closing its runtime are one transition under the "
            + "publication write gate, so this is a lifecycle defect rather than an ordinary race.");
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
        ILanguageDefinition Definition,
        IInvocationLifetime? Lifetime = null);
}
