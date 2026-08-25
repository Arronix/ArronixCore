using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Common.Contributions;
using Arronix.Host.Configuration;
using Arronix.Host.Intent;
using Arronix.Plugins.Registry;
using Microsoft.Extensions.Options;


namespace Arronix.Host.Media;

/// <summary>
/// Admits media kinds and answers questions about the ones that were admitted.
/// </summary>
/// <remarks>
/// <para>
/// Admission is the gate. A contribution is validated, resolved, projected and bundled here, and either all
/// of that succeeds and the kind becomes visible or none of it does and the contributing extension is
/// quarantined. There is no state in which half a media kind is registered, which is what lets every reader
/// treat a registered kind as sound without checking.
/// </para>
/// <para>
/// Two extensions claiming one media kind is refused rather than resolved. A tie-break — last wins, first
/// wins, highest version wins — would make which extension owns a kind depend on folder enumeration order,
/// and the operator would have no way to see that it had happened.
/// </para>
/// </remarks>
/// <param name="library">The deployment's library settings, for the affordances that depend on them.</param>
/// <param name="publication">The shared extension-publication boundary.</param>
public sealed class MediaKindRegistry(
    IOptions<LibraryOptions> library,
    PluginPublicationGate publication) : IMediaKindRegistry
{
    private readonly ConcurrentDictionary<MediaKindId, RegisteredMediaKind> _kinds = new();
    private readonly LibraryOptions _library = library?.Value ?? throw new ArgumentNullException(nameof(library));
    private readonly PluginPublicationGate _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    private volatile bool _releaseSourceConfigured;

    /// <summary>Creates a standalone registry with its own publication boundary.</summary>
    public MediaKindRegistry(IOptions<LibraryOptions> library)
        : this(library, new PluginPublicationGate())
    {
    }

    /// <summary>Gets the publication boundary this registry participates in.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

    /// <inheritdoc />
    public IReadOnlyList<RegisteredMediaKind> All
    {
        get
        {
            using var publication = _publication.EnterRead();
            return [.. _kinds.Values.OrderBy(kind => kind.Kind.Value, StringComparer.Ordinal)];
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Takes a kind together with its contributing extension's ticket.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="leased">The kind and its ticket. Dispose it when the work using it has finished.</param>
    /// <returns><see langword="false"/> when no such kind is published.</returns>
    /// <remarks>
    /// Everything executable a kind carries is extension code, so anything that runs one of its seams takes
    /// this rather than <see cref="TryGet"/> and holds it across every await in the call.
    /// </remarks>
    internal bool TryLease(MediaKindId kind, [NotNullWhen(true)] out Leased<RegisteredMediaKind>? leased)
    {
        using var publication = _publication.EnterRead();

        if (_kinds.TryGetValue(kind, out var registered))
        {
            leased = new Leased<RegisteredMediaKind>(registered, Ticket(registered));
            return true;
        }

        leased = null;
        return false;
    }

    /// <summary>Takes the ticket a published kind's extension must still be able to give.</summary>
    private static IDisposable? Ticket(RegisteredMediaKind registered)
    {
        if (registered.Lifetime is not { } lifetime)
        {
            return null;
        }

        if (lifetime.TryEnter(out var ticket))
        {
            return ticket;
        }

        throw new InvalidOperationException(
            $"Media kind '{registered.Shape.Kind}' is still published while extension '{registered.Plugin}' "
            + "is closed to invocation. Removing a contribution and closing its runtime are one transition "
            + "under the publication write gate, so this is a lifecycle defect rather than an ordinary race.");
    }

    public bool TryGet(MediaKindId kind, [NotNullWhen(true)] out RegisteredMediaKind? registered)
    {
        using var publication = _publication.EnterRead();
        return _kinds.TryGetValue(kind, out registered);
    }

    /// <inheritdoc />
    public RegisteredMediaKind Require(MediaKindId kind)
    {
        using var publication = _publication.EnterRead();
        return _kinds.TryGetValue(kind, out var registered)
            ? registered
            : throw new ArronixException(
                CoreErrorCode.MediaKindNotFound,
                $"No media kind '{kind}' is registered.");
    }

    /// <summary>
    /// Admits one contribution.
    /// </summary>
    /// <param name="contribution">Everything the extension contributed for one kind.</param>
    /// <param name="registered">The admitted kind when admission succeeded; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Every reason admission failed. Empty exactly when it succeeded.</param>
    /// <returns><see langword="true"/> when the kind was admitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contribution"/> is <see langword="null"/>.</exception>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "Admission returns three results — whether it succeeded, the admitted kind and the complete defect list — and the caller quarantines the extension on the third.")]
    internal bool TryRegister(
        MediaKindContribution contribution,
        out RegisteredMediaKind? registered,
        out IReadOnlyList<ShapeDefect> defects)
    {
        if (!TryPrepare(contribution, out registered, out defects, lifetime: null))
        {
            return false;
        }

        if (TryPublish(registered!, out defects))
        {
            return true;
        }

        registered = null;
        return false;
    }

    /// <summary>Builds and validates one kind without making it visible.</summary>
    internal bool TryPrepare(
        MediaKindContribution contribution,
        out RegisteredMediaKind? registered,
        out IReadOnlyList<ShapeDefect> defects,
        IInvocationLifetime? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(contribution);

        registered = null;

        if (!ValidatedShape.TryValidate(contribution.Shape, out var shape, out var shapeDefects))
        {
            defects = shapeDefects;
            return false;
        }

        var kind = shape!.Kind;

        using (_publication.EnterRead())
        {
            if (_kinds.TryGetValue(kind, out var incumbent))
            {
                defects =
                [
                    new ShapeDefect(
                        "kind",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Media kind '{kind}' is already provided by extension '{incumbent.Plugin}'."),
                        CoreErrorCode.MediaKindConflict),
                ];
                return false;
            }
        }

        // Everything below can enumerate extension-supplied declaration collections. It deliberately runs
        // after the collision snapshot's read lease is released; final publication rechecks the collision.
        var intent = contribution.Intent ?? new PluginIntentSurface { MediaKind = kind };
        var intentDefects = IntentSurfaceValidator.Validate(shape, intent);

        if (intentDefects.Count > 0)
        {
            defects = intentDefects;
            return false;
        }

        registered = new RegisteredMediaKind(
            contribution,
            shape,
            intent,
            new MediaKindProjection(shape, contribution.PluginVersion, contribution.Capabilities),
            BuildDescriptor(contribution, shape, intent, RootFolderConfigured, _releaseSourceConfigured),
            lifetime);

        defects = [];
        return true;
    }

    /// <summary>Publishes one already-built kind, rechecking ownership at the publication boundary.</summary>
    internal bool TryPublish(
        RegisteredMediaKind candidate,
        out IReadOnlyList<ShapeDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        using var publication = _publication.EnterWrite();

        if (!_kinds.TryAdd(candidate.Kind, candidate))
        {
            _kinds.TryGetValue(candidate.Kind, out var incumbent);
            defects =
            [
                new ShapeDefect(
                    "kind",
                    incumbent is null
                        ? $"Media kind '{candidate.Kind}' was claimed while this extension was being published."
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"Media kind '{candidate.Kind}' is already provided by extension '{incumbent.Plugin}'."),
                    CoreErrorCode.MediaKindConflict),
            ];
            return false;
        }

        defects = [];
        return true;
    }

    /// <summary>Removes exactly one published candidate and never a later replacement.</summary>
    internal bool Remove(RegisteredMediaKind candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        using var publication = _publication.EnterWrite();
        return ((ICollection<KeyValuePair<MediaKindId, RegisteredMediaKind>>)_kinds)
            .Remove(new KeyValuePair<MediaKindId, RegisteredMediaKind>(candidate.Kind, candidate));
    }

    /// <summary>
    /// Withdraws every media kind an extension contributed.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    /// <returns>The number of kinds withdrawn.</returns>
    internal int RemoveByPlugin(PluginId plugin)
    {
        using var publication = _publication.EnterWrite();
        var removed = 0;

        foreach (var kind in _kinds.Values.Where(registered => registered.Plugin == plugin).ToList())
        {
            if (_kinds.TryRemove(kind.Kind, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Rebuilds the cached wire bundles after a deployment fact they derive from has changed.
    /// </summary>
    /// <param name="releaseSourceConfigured">Whether at least one enabled release source now exists.</param>
    /// <remarks>
    /// Two of the ten affordances depend on configuration rather than on a shape. Rather than recomputing
    /// the bundle on every read — it is the largest response the platform serves — the registry is told when
    /// the facts change. The alternative, deriving them at read time, would put a provider-store query in
    /// the path of every catalog page.
    /// </remarks>
    internal void Refresh(bool releaseSourceConfigured)
    {
        while (true)
        {
            KeyValuePair<MediaKindId, RegisteredMediaKind>[] snapshot;
            using (_publication.EnterRead())
            {
                snapshot = [.. _kinds];
            }

            // Descriptor derivation walks extension-authored data. Do all of it without stopping readers or
            // publishers, then install only if the exact registry snapshot is still authoritative.
            var rootFolderConfigured = RootFolderConfigured;
            var rebuilt = snapshot
                .Select(entry => new KeyValuePair<RegisteredMediaKind, MediaKindDescriptor>(
                    entry.Value,
                    BuildDescriptor(
                        entry.Value,
                        entry.Value.Shape,
                        entry.Value.Intent,
                        rootFolderConfigured,
                        releaseSourceConfigured)))
                .ToArray();

            using var publication = _publication.EnterWrite();
            if (_kinds.Count != snapshot.Length
                || snapshot.Any(entry => !_kinds.TryGetValue(entry.Key, out var current)
                    || !ReferenceEquals(current, entry.Value)))
            {
                continue;
            }

            _releaseSourceConfigured = releaseSourceConfigured;
            foreach (var entry in rebuilt)
            {
                entry.Key.Descriptor = entry.Value;
            }

            return;
        }
    }

    private bool RootFolderConfigured => _library.RootFolders.Count > 0;

    private static MediaKindDescriptor BuildDescriptor(
        MediaKindContribution contribution,
        ValidatedShape shape,
        PluginIntentSurface intent,
        bool rootFolderConfigured,
        bool releaseSourceConfigured)
        => Describe(
            contribution.Plugin,
            contribution.Capabilities,
            shape,
            intent,
            rootFolderConfigured,
            releaseSourceConfigured);

    private static MediaKindDescriptor BuildDescriptor(
        RegisteredMediaKind registered,
        ValidatedShape shape,
        PluginIntentSurface intent,
        bool rootFolderConfigured,
        bool releaseSourceConfigured)
        => Describe(
            registered.Plugin,
            registered.Capabilities,
            shape,
            intent,
            rootFolderConfigured,
            releaseSourceConfigured);

    private static MediaKindDescriptor Describe(
        PluginId plugin,
        CapabilitySet capabilities,
        ValidatedShape shape,
        PluginIntentSurface intent,
        bool rootFolderConfigured,
        bool releaseSourceConfigured)
    {
        var levels = new List<LevelPresentation>(shape.Levels.Count);

        foreach (var level in shape.Levels)
        {
            levels.Add(new LevelPresentation
            {
                Level = level.Id,
                Name = level.Name,
                PluralName = level.PluralName,
                Fields = level.Fields,
                Affordances = AffordanceCalculator.ForLevel(
                    shape,
                    level,
                    capabilities,
                    rootFolderConfigured,
                    releaseSourceConfigured),

                // An action with no target level applies at every level; one with a target appears only
                // there. Resolving that here means no consumer has to know the rule.
                Actions =
                [
                    .. intent.Actions.Where(action =>
                        action.TargetLevelId is null || action.TargetLevelId == level.Id),
                ],
            });
        }

        return new MediaKindDescriptor
        {
            Kind = shape.Kind,
            Name = shape.Declaration.Name,
            PluralName = shape.Declaration.PluralName,
            Shape = shape.Declaration,
            Levels = levels,
            Intent = intent,
            Capabilities = [.. capabilities.WithImplied().Enumerate().Select(CapabilityNames.ToWireName)],
            Plugin = plugin,
        };
    }
}
