using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Host.Media.Typed;

// Shape, plugin and typed-media contracts are experimental; the binder is where they meet the registry.
#pragma warning disable ARX0013
#pragma warning disable ARX0014
#pragma warning disable ARX0020

namespace Arronix.Host.Media;

/// <summary>
/// Turns one captured type pair into one registered media kind: derive, validate, build the engines, admit.
/// </summary>
/// <remarks>
/// <para>
/// This is the typed registration path end to end. The registration's two type arguments are reopened, the
/// item type is read and its configuration call replayed to produce the descriptors, the result is resolved
/// into a <see cref="ValidatedDefinition"/> (shape first, then every section cross-reference), each engine
/// the catalog carries is constructed for the kind, and the whole is admitted through
/// <see cref="MediaKindRegistry.TryRegister"/> as an ordinary <see cref="MediaKindContribution"/> — which is
/// what makes the two registration paths one pipeline: downstream of the registry there is no typed kind,
/// only a registered kind whose seams happen to be host engines.
/// </para>
/// <para>
/// Refusal is atomic and complete. A model that fails validation, an engine slot the build does not fill, an
/// engine that refuses the model while compiling it, or an engine that reports the wrong kind each refuse
/// the whole contribution with every defect found, and nothing is registered — the same all-or-none rule the
/// registry itself enforces. Refusal is always a defect list and never an exception, because this runs
/// inside extension loading and a throw would take the pipeline down rather than quarantine one extension.
/// </para>
/// <para>
/// Derivation is inside the gate, not before it. A host bug that derives an invalid shape is refused by the
/// same rules a plugin's mistake would be, because a gate that trusts its own side is not a gate.
/// </para>
/// </remarks>
public sealed class MediaTypeBinder
{
    private readonly MediaKindRegistry _kinds;
    private readonly DefinitionEngineCatalog _engines;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaTypeBinder"/> class.
    /// </summary>
    /// <param name="kinds">The registry admitted kinds go into.</param>
    /// <param name="engines">The engines this build can execute a kind's model with.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public MediaTypeBinder(MediaKindRegistry kinds, DefinitionEngineCatalog engines)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(engines);

        _kinds = kinds;
        _engines = engines;
    }

    /// <summary>
    /// Admits one captured typed media kind.
    /// </summary>
    /// <param name="contribution">The registration and who contributed it.</param>
    /// <param name="registered">The admitted kind when admission succeeded; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Every reason admission failed. Empty exactly when it succeeded.</param>
    /// <returns><see langword="true"/> when the kind was admitted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="contribution"/> is <see langword="null"/>.</exception>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "Admission returns three results — whether it succeeded, the admitted kind and the complete defect list — and the caller quarantines the extension on the third.")]
    public bool TryRegister(
        TypedContribution contribution,
        out RegisteredMediaKind? registered,
        out IReadOnlyList<ShapeDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(contribution);

        return TryRegister(
            contribution.Plugin,
            contribution.PluginVersion,
            contribution.Capabilities,
            contribution.Registration.Bind(DerivationBinder.Instance),
            out registered,
            out defects);
    }

    /// <summary>
    /// Admits one already-derived media kind.
    /// </summary>
    /// <param name="plugin">The contributing extension.</param>
    /// <param name="pluginVersion">Its version, verbatim from its manifest.</param>
    /// <param name="capabilities">The capabilities granted to it, after implication.</param>
    /// <param name="derived">The derived runtime model.</param>
    /// <param name="registered">The admitted kind when admission succeeded; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Every reason admission failed. Empty exactly when it succeeded.</param>
    /// <returns><see langword="true"/> when the kind was admitted.</returns>
    /// <remarks>
    /// The half of admission that follows derivation, split out so it can be exercised against a model the
    /// caller chose. Every rule below is a rule about a model, not about how one was obtained.
    /// </remarks>
    internal bool TryRegister(
        PluginId plugin,
        string pluginVersion,
        CapabilitySet capabilities,
        IMediaType derived,
        out RegisteredMediaKind? registered,
        out IReadOnlyList<ShapeDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(derived);

        registered = null;

        if (!ValidatedDefinition.TryValidate(derived, out var validated, out defects))
        {
            return false;
        }

        var found = new List<ShapeDefect>();
        var items = Build(_engines.ItemStore, validated!, "engines.itemStore", "item store (E9)", found);
        var parser = Build(_engines.Parser, validated!, "engines.parser", "title parse engine (E2)", found);
        var matcher = Build(_engines.Matcher, validated!, "engines.matcher", "match engine (E5)", found);
        var planner = Build(_engines.QueryPlanner, validated!, "engines.queryPlanner", "query templater (E6)", found);

        // Optional slots: the ladder-derived quality default and the bare naming default are complete
        // behaviors without a seam instance, so an absent factory is not a defect here. A factory that is
        // present and refuses still is one.
        var quality = Build(_engines.Quality, validated!, "engines.quality", "quality evaluator (E4)", found, required: false);
        var naming = Build(_engines.Naming, validated!, "engines.naming", "naming renderer (E7)", found, required: false);

        CheckKind(items?.MediaKind, validated!.Kind, "engines.itemStore", found);
        CheckKind(parser?.MediaKind, validated.Kind, "engines.parser", found);
        CheckKind(matcher?.MediaKind, validated.Kind, "engines.matcher", found);
        CheckKind(planner?.MediaKind, validated.Kind, "engines.queryPlanner", found);
        CheckKind(quality?.MediaKind, validated.Kind, "engines.quality", found);
        CheckKind(naming?.MediaKind, validated.Kind, "engines.naming", found);

        if (found.Count > 0)
        {
            defects = found;
            return false;
        }

        var bundle = new MediaKindContribution
        {
            Plugin = plugin,
            PluginVersion = pluginVersion,
            Capabilities = capabilities,
            Shape = derived.Shape,
            Items = items!,
            Intent = derived.Intent,
            Matcher = matcher,
            QueryPlanner = planner,
            Parser = parser,
            Quality = quality,
            Naming = naming,
            Definition = validated,
        };

        return _kinds.TryRegister(bundle, out registered, out defects);
    }

    private static TSeam? Build<TSeam>(
        Func<ValidatedDefinition, TSeam?>? factory,
        ValidatedDefinition definition,
        string path,
        string engineName,
        List<ShapeDefect> defects,
        bool required = true)
        where TSeam : class
    {
        if (factory is null)
        {
            if (required)
            {
                defects.Add(new ShapeDefect(
                    path,
                    $"No host {engineName} is registered in this build, so the definition's behavior cannot be executed. The definition is refused rather than admitted without it.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            return null;
        }

        try
        {
            return factory(definition);
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            // An engine compiles its kind's declaration at construction and refuses loudly when a row it
            // cannot execute survived the gate — a dangling identifier, a template the grammar does not
            // accept. That refusal is a defect in the definition, so it is reported as one: the two
            // narrow exception types are exactly what the engines document themselves as throwing, and
            // anything else is a fault in the host that must not be disguised as a bad declaration.
            //
            // Catching here is what keeps this class's promise. Admission is called from inside extension
            // loading, and an exception escaping it would take the load pipeline down instead of
            // quarantining one extension with a reason a reader can act on.
            defects.Add(new ShapeDefect(
                path,
                $"The host {engineName} refused the definition: {failure.Message}",
                CoreErrorCode.PluginShapeInvalid));
            return null;
        }
    }

    private static void CheckKind(
        MediaKindId? reported,
        MediaKindId declared,
        string path,
        List<ShapeDefect> defects)
    {
        if (reported is { } kind && kind != declared)
        {
            defects.Add(new ShapeDefect(
                path,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The engine reports media kind '{kind}' for a kind declaring '{declared}'."),
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    /// <summary>
    /// Reopens a registration's type arguments and runs the derivation with them in scope.
    /// </summary>
    /// <remarks>
    /// Stateless, so one instance serves every kind. This is the only place in the host that needs both type
    /// arguments statically; everything else holds the kind-blind registration.
    /// </remarks>
    private sealed class DerivationBinder : IMediaTypeBinder<IMediaType>
    {
        internal static readonly DerivationBinder Instance = new();

        /// <inheritdoc />
        public IMediaType Bind<TItem, TType>()
            where TItem : IMediaItem
            where TType : IMediaType<TItem>
            => MediaTypeModelFactory.Build<TItem, TType>();
    }
}
