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
using Arronix.Host.Configuration;
using Arronix.Host.Intent;
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
public sealed class MediaKindRegistry(IOptions<LibraryOptions> library) : IMediaKindRegistry
{
    private readonly ConcurrentDictionary<MediaKindId, RegisteredMediaKind> _kinds = new();
    private readonly LibraryOptions _library = library?.Value ?? throw new ArgumentNullException(nameof(library));
    private volatile bool _releaseSourceConfigured;

    /// <inheritdoc />
    public IReadOnlyList<RegisteredMediaKind> All
        => [.. _kinds.Values.OrderBy(kind => kind.Kind.Value, StringComparer.Ordinal)];

    /// <inheritdoc />
    public bool TryGet(MediaKindId kind, [NotNullWhen(true)] out RegisteredMediaKind? registered)
        => _kinds.TryGetValue(kind, out registered);

    /// <inheritdoc />
    public RegisteredMediaKind Require(MediaKindId kind)
        => _kinds.TryGetValue(kind, out var registered)
            ? registered
            : throw new ArronixException(
                CoreErrorCode.MediaKindNotFound,
                $"No media kind '{kind}' is registered.");

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
    public bool TryRegister(
        MediaKindContribution contribution,
        out RegisteredMediaKind? registered,
        out IReadOnlyList<ShapeDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(contribution);

        registered = null;

        if (!ValidatedShape.TryValidate(contribution.Shape, out var shape, out var shapeDefects))
        {
            defects = shapeDefects;
            return false;
        }

        var kind = shape!.Kind;

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

        var intent = contribution.Intent ?? new PluginIntentSurface { MediaKind = kind };
        var intentDefects = IntentSurfaceValidator.Validate(shape, intent);

        if (intentDefects.Count > 0)
        {
            defects = intentDefects;
            return false;
        }

        var candidate = new RegisteredMediaKind(
            contribution,
            shape,
            intent,
            new MediaKindProjection(shape, contribution.PluginVersion, contribution.Capabilities),
            BuildDescriptor(contribution, shape, intent, RootFolderConfigured, _releaseSourceConfigured));

        if (!_kinds.TryAdd(kind, candidate))
        {
            defects =
            [
                new ShapeDefect(
                    "kind",
                    $"Media kind '{kind}' was claimed by another extension while this one was being admitted.",
                    CoreErrorCode.MediaKindConflict),
            ];
            return false;
        }

        registered = candidate;
        defects = [];
        return true;
    }

    /// <summary>
    /// Withdraws every media kind an extension contributed.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    /// <returns>The number of kinds withdrawn.</returns>
    public int RemoveByPlugin(PluginId plugin)
    {
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
    public void Refresh(bool releaseSourceConfigured)
    {
        _releaseSourceConfigured = releaseSourceConfigured;

        foreach (var registered in _kinds.Values)
        {
            registered.Descriptor = BuildDescriptor(
                registered,
                registered.Shape,
                registered.Intent,
                RootFolderConfigured,
                releaseSourceConfigured);
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
